namespace PortwayApi.Tests.Concurrency;

using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PortwayApi.Classes;
using PortwayApi.Services.Caching;
using PortwayApi.Services.Files;
using PortwayApi.Services.Telemetry;
using PortwayApi.Tests.Support;
using Xunit;

/// <summary>
/// Stress tests that hammer shared mutable state from many threads at once.
/// Each one fails deterministically against the pre-fix code within a couple of seconds.
/// </summary>
[Collection(ConcurrencyCollection.Name)]
public class ConcurrencyStressTests
{
    private static readonly TimeSpan RunFor = TimeSpan.FromSeconds(2);
    private static readonly int Readers = Math.Max(4, Environment.ProcessorCount);

    /// <summary>Runs readers and writers together, collecting every exception either side throws</summary>
    private static ConcurrentBag<Exception> Hammer(Action reader, Action writer, int readerCount)
    {
        var errors = new ConcurrentBag<Exception>();
        using var cts = new CancellationTokenSource(RunFor);
        var ct = cts.Token;

        var tasks = new List<Task>();

        for (var i = 0; i < readerCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try { reader(); }
                    catch (Exception ex) { errors.Add(ex); return; }
                }
            }, CancellationToken.None));
        }

        tasks.Add(Task.Run(() =>
        {
            while (!ct.IsCancellationRequested)
            {
                try { writer(); }
                catch (Exception ex) { errors.Add(ex); return; }
            }
        }, CancellationToken.None));

        Task.WaitAll([.. tasks]);
        return errors;
    }

    private static string Describe(IEnumerable<Exception> errors) =>
        string.Join("\n", errors.Take(5).Select(e => $"{e.GetType().Name}: {e.Message}"));

    // EnvironmentSettings: the allowed-environment list is cleared and refilled by Reload()
    // while request threads read it. A reader must never see the list empty or torn.
    [Fact]
    public void EnvironmentSettings_ReloadUnderLoad_AllowlistStaysIntact()
    {
        using var temp = new TempDirectory("envsettings_stress");
        var settingsPath = temp.Combine("settings.json");
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(new
        {
            Environment = new { ServerName = "test-server", AllowedEnvironments = new[] { "prod", "dev", "qa" } }
        }));

        var settings = new EnvironmentSettings();
        typeof(EnvironmentSettings)
            .GetField("_settingsPath", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(settings, settingsPath);
        settings.Reload();

        Assert.True(settings.IsEnvironmentAllowed("prod"), "precondition: prod is allowed before the stress run");

        var falseNegatives = 0;

        var errors = Hammer(
            reader: () =>
            {
                if (!settings.IsEnvironmentAllowed("prod"))
                    Interlocked.Increment(ref falseNegatives);

                if (settings.GetAllowedEnvironments().Count != 3)
                    Interlocked.Increment(ref falseNegatives);
            },
            writer: settings.Reload,
            readerCount: Readers);

        Assert.True(errors.IsEmpty, $"readers threw during reload:\n{Describe(errors)}");
        Assert.True(
            Volatile.Read(ref falseNegatives) == 0,
            $"allowlist was empty or partial for {Volatile.Read(ref falseNegatives)} reads during reload; " +
            "requests would have been rejected with 'environment not allowed'");
    }

    // EndpointHandler: reload nulls the static caches while readers are between
    // "load if needed" and "return the field", producing a NullReferenceException.
    [Fact]
    public void EndpointHandler_ReloadUnderLoad_NeverNullDerefs()
    {
        // Prime every cache so the readers take the already-loaded fast path
        EndpointHandler.GetSqlEndpoints();
        EndpointHandler.GetProxyEndpoints();
        EndpointHandler.GetSqlWebhookEndpoints();
        EndpointHandler.GetFileEndpoints();
        EndpointHandler.GetStaticEndpoints();

        var errors = Hammer(
            reader: () =>
            {
                _ = EndpointHandler.GetSqlEndpoints().Count;
                _ = EndpointHandler.GetProxyEndpoints().Count;
                _ = EndpointHandler.GetSqlWebhookEndpoints().Count;
                _ = EndpointHandler.GetFileEndpoints().Count;
                _ = EndpointHandler.GetStaticEndpoints().Count;
                _ = EndpointHandler.GetCompositeDefinitions(
                        EndpointHandler.GetEndpoints(Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Proxy")))
                    .Count;
            },
            writer: () =>
            {
                EndpointHandler.ReloadAllEndpoints();
                EndpointHandler.ReloadEndpointType(EndpointType.SQL);
                EndpointHandler.ReloadEndpointType(EndpointType.Proxy);
            },
            readerCount: Readers);

        Assert.True(errors.IsEmpty, $"endpoint readers threw while a reload was in flight:\n{Describe(errors)}");
    }

    // FileHandlerService: _currentMemoryUsage was mutated with a non-atomic += / -=, so
    // concurrent uploads and deletes lose updates and the counter drifts away from what is
    // really cached, which silently disables the MaxTotalMemoryCacheMB cap.
    [Fact]
    public async Task FileHandlerService_ConcurrentUploadsAndDeletes_CounterMatchesCacheContents()
    {
        using var temp = new TempDirectory("filehandler_stress");
        var service = BuildFileHandler(temp.Path);

        const int rounds = 12;
        const int filesPerRound = 64;
        var payload = Encoding.UTF8.GetBytes(new string('x', 512));

        for (var round = 0; round < rounds; round++)
        {
            var ids = new ConcurrentBag<string>();

            await Parallel.ForAsync(0, filesPerRound, new ParallelOptions { MaxDegreeOfParallelism = 32 },
                async (i, ct) =>
                {
                    using var stream = new MemoryStream(payload);
                    ids.Add(await service.UploadFileAsync("prod", $"r{round}_f{i}.txt", stream, overwrite: true));
                });

            // Delete half of them concurrently so the counter is driven in both directions at once
            var toDelete = ids.Take(filesPerRound / 2).ToList();
            await Parallel.ForEachAsync(toDelete, new ParallelOptions { MaxDegreeOfParallelism = 32 },
                async (id, ct) => await service.DeleteFileAsync(id));
        }

        Assert.Equal(service.MeasuredMemoryUsage, service.CurrentMemoryUsage);
    }

    // MemoryCacheProvider keyed its semaphores by proxy cache key, which contains the full
    // request URL. Distinct URLs therefore leaked a SemaphoreSlim each, forever.
    [Fact]
    public async Task MemoryCacheProvider_ManyDistinctLockKeys_StaysBounded()
    {
        var provider = new MemoryCacheProvider(Options.Create(new CacheOptions()));

        const int distinctKeys = 20_000;

        for (var i = 0; i < distinctKeys; i++)
        {
            using var handle = await provider.AcquireLockAsync(
                $"proxy:prod:/api/items?page={i}",
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10));

            Assert.NotNull(handle);
        }

        Assert.True(
            provider.TrackedLockCount < distinctKeys,
            $"lock table grew to {provider.TrackedLockCount} entries for {distinctKeys} distinct keys; it is never pruned");
    }

    // The lock table must still serialise callers that ask for the same key.
    [Fact]
    public async Task MemoryCacheProvider_SameKey_StillMutuallyExclusive()
    {
        var provider = new MemoryCacheProvider(Options.Create(new CacheOptions()));

        var inside = 0;
        var overlaps = 0;

        await Parallel.ForAsync(0, 64, async (_, ct) =>
        {
            using var handle = await provider.AcquireLockAsync(
                "contended-key", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10), ct);

            Assert.NotNull(handle);

            if (Interlocked.Increment(ref inside) != 1)
                Interlocked.Increment(ref overlaps);

            await Task.Delay(1, ct);
            Interlocked.Decrement(ref inside);
        });

        Assert.Equal(0, Volatile.Read(ref overlaps));
    }

    private static FileHandlerService BuildFileHandler(string storageDirectory)
    {
        var cacheOptions = new CacheOptions { Enabled = false };
        var cacheManager = new CacheManager(
            new StaticOptionsMonitor<CacheOptions>(cacheOptions),
            new MemoryCacheProvider(Options.Create(cacheOptions)),
            new PortwayApi.Services.MetricsService(),
            new PortwayMetrics());

        var fileOptions = new StaticOptionsMonitor<FileStorageOptions>(new FileStorageOptions
        {
            StorageDirectory = storageDirectory,
            UseMemoryCache = true,
            MaxTotalMemoryCacheMB = 512,
        });

        return new FileHandlerService(fileOptions, cacheManager, Serilog.Log.Logger);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public class ConcurrencyCollection
{
    public const string Name = "concurrency-stress";
}
