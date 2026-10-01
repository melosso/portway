using System.Text;
using Microsoft.Extensions.Options;
using PortwayApi.Helpers;
using PortwayApi.Services;
using PortwayApi.Services.Caching;
using PortwayApi.Services.Files;
using PortwayApi.Services.Telemetry;
using PortwayApi.Tests.Support;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// Encrypted file ids and folder confinement for download, delete and list
/// </summary>
public sealed class FileIdConfinementTests : IDisposable
{
    private readonly string _storageDir;
    private readonly FileHandlerService _handler;

    public FileIdConfinementTests()
    {
        _storageDir = Path.Combine(Path.GetTempPath(), $"portway_fileid_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_storageDir);

        var cacheOptions = new CacheOptions { Enabled = false };
        var cacheManager = new CacheManager(
            new StaticOptionsMonitor<CacheOptions>(cacheOptions),
            new MemoryCacheProvider(Options.Create(cacheOptions)),
            new MetricsService(),
            new PortwayMetrics());

        var fileOptions = new StaticOptionsMonitor<FileStorageOptions>(new FileStorageOptions
        {
            StorageDirectory = _storageDir,
            UseMemoryCache = false,
        });

        _handler = new FileHandlerService(fileOptions, cacheManager, Serilog.Log.Logger);
    }

    public void Dispose()
    {
        _handler.Dispose();
        if (Directory.Exists(_storageDir)) Directory.Delete(_storageDir, recursive: true);
    }

    private static MemoryStream Body(string text = "secret") => new(Encoding.UTF8.GetBytes(text));

    private static string LegacyId(string env, string path) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{env}:{path}")).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    [Fact]
    public async Task UploadIdIsEncrypted()
    {
        var fileId = await _handler.UploadFileAsync("prod", "invoices", "march.pdf", Body());

        Assert.StartsWith(FileIdProtector.Prefix, fileId);
        Assert.DoesNotContain("march", Encoding.UTF8.GetString(Convert.FromBase64String(Pad(fileId[FileIdProtector.Prefix.Length..]))));
    }

    [Fact]
    public async Task UploadKeepsFolder()
    {
        await _handler.UploadFileAsync("prod", "invoices/ACME", "march.pdf", Body());

        Assert.True(File.Exists(Path.Combine(_storageDir, "prod", "invoices", "ACME", "march.pdf")));
    }

    [Fact]
    public async Task DownloadInsideFolder()
    {
        var fileId = await _handler.UploadFileAsync("prod", "invoices", "march.pdf", Body("hello"));

        var (stream, filename, _) = await _handler.DownloadFileAsync(fileId, "prod", "invoices");

        Assert.Equal("invoices/march.pdf", filename);
        Assert.Equal("hello", new StreamReader(stream).ReadToEnd());
    }

    [Fact]
    public async Task DownloadOtherFolderRefused()
    {
        var fileId = await _handler.UploadFileAsync("prod", "invoices/GLOBEX", "march.pdf", Body());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _handler.DownloadFileAsync(fileId, "prod", "invoices/ACME"));
    }

    [Fact]
    public async Task FolderPrefixIsNotAFolder()
    {
        var fileId = await _handler.UploadFileAsync("prod", "invoicesX", "march.pdf", Body());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _handler.DownloadFileAsync(fileId, "prod", "invoices"));
    }

    [Fact]
    public async Task DeleteOtherFolderRefused()
    {
        var fileId = await _handler.UploadFileAsync("prod", "invoices/GLOBEX", "march.pdf", Body());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _handler.DeleteFileAsync(fileId, "prod", "invoices/ACME"));
        Assert.True(File.Exists(Path.Combine(_storageDir, "prod", "invoices", "GLOBEX", "march.pdf")));
    }

    [Fact]
    public async Task ForgedLegacyIdRefusedInFolder()
    {
        await _handler.UploadFileAsync("prod", "invoices/GLOBEX", "march.pdf", Body());

        await Assert.ThrowsAnyAsync<Exception>(() => _handler.DownloadFileAsync(LegacyId("prod", "invoices/GLOBEX/march.pdf"), "prod", "invoices/ACME"));
        await Assert.ThrowsAnyAsync<Exception>(() => _handler.DownloadFileAsync(LegacyId("prod", "march.pdf"), "prod", "invoices/ACME"));
    }

    [Fact]
    public async Task LegacyIdStillWorksWithoutFolder()
    {
        await _handler.UploadFileAsync("prod", "march.pdf", Body("old"));

        var (stream, filename, _) = await _handler.DownloadFileAsync(LegacyId("prod", "march.pdf"), "prod");

        Assert.Equal("march.pdf", filename);
        Assert.Equal("old", new StreamReader(stream).ReadToEnd());
    }

    [Fact]
    public async Task TamperedIdRejected()
    {
        var fileId = await _handler.UploadFileAsync("prod", "invoices", "march.pdf", Body());
        var chars = fileId.ToCharArray();
        chars[^3] = chars[^3] == 'A' ? 'B' : 'A';

        await Assert.ThrowsAsync<ArgumentException>(() => _handler.DownloadFileAsync(new string(chars), "prod", "invoices"));
    }

    [Theory]
    [InlineData("../other")]
    [InlineData("invoices/../../etc")]
    [InlineData("/etc")]
    [InlineData("a//b")]
    [InlineData("a\\b")]
    [InlineData("./a")]
    public async Task UnsafeFolderRejected(string folder)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _handler.UploadFileAsync("prod", folder, "march.pdf", Body()));
    }

    [Fact]
    public async Task ListStaysInFolder()
    {
        await _handler.UploadFileAsync("prod", "invoices/ACME", "a.pdf", Body());
        await _handler.UploadFileAsync("prod", "invoices/GLOBEX", "b.pdf", Body());
        await _handler.UploadFileAsync("prod", "ACME.pdf", Body());

        var files = (await _handler.ListFilesAsync("prod", "invoices/ACME", null)).ToList();

        var only = Assert.Single(files);
        Assert.Equal("invoices/ACME/a.pdf", only.FileName);
        var (stream, _, _) = await _handler.DownloadFileAsync(only.FileId, "prod", "invoices/ACME");
        stream.Dispose();
    }

    [Fact]
    public async Task ListPrefixCannotLeaveFolder()
    {
        await _handler.UploadFileAsync("prod", "invoices/GLOBEX", "b.pdf", Body());

        await Assert.ThrowsAsync<ArgumentException>(() => _handler.ListFilesAsync("prod", "invoices/ACME", "../GLOBEX"));
    }

    [Fact]
    public void ProtectRoundTrips()
    {
        var keys = FileIdProtector.DeriveKeys("k1");
        var id = FileIdProtector.Protect("prod", "a/b.pdf", keys);

        Assert.Equal(id, FileIdProtector.Protect("prod", "a/b.pdf", keys));
        Assert.True(FileIdProtector.TryUnprotect(id, keys, out var env, out var path));
        Assert.Equal(("prod", "a/b.pdf"), (env, path));
        Assert.False(FileIdProtector.TryUnprotect(id, FileIdProtector.DeriveKeys("k2"), out _, out _));
    }

    [Fact]
    public async Task AbsoluteUploadDownloads()
    {
        var root = Path.Combine(_storageDir, "abs-root");
        var fileId = await _handler.UploadFileToAbsolutePathAsync("prod", Path.Combine(root, "in", "march.pdf"), Body("abs"), root);

        var (stream, _, _) = await _handler.DownloadFileAsync(fileId, "prod", root);
        Assert.Equal("abs", new StreamReader(stream).ReadToEnd());

        var listed = Assert.Single(await _handler.ListFilesAsync("prod", root, null));
        Assert.Equal(fileId, listed.FileId);

        await _handler.DeleteFileAsync(fileId, "prod", root);
        Assert.False(File.Exists(Path.Combine(root, "in", "march.pdf")));
    }

    [Fact]
    public async Task AbsoluteFileStaysInItsRoot()
    {
        var root = Path.Combine(_storageDir, "abs-a");
        var fileId = await _handler.UploadFileToAbsolutePathAsync("prod", Path.Combine(root, "x.pdf"), Body("abs"), root);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _handler.DownloadFileAsync(fileId, "prod", Path.Combine(_storageDir, "abs-b")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _handler.DownloadFileAsync(fileId, "prod", root + "x"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _handler.DownloadFileAsync(fileId, "prod", "relative"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _handler.DownloadFileAsync(fileId, "prod"));
    }

    [Fact]
    public async Task RelativeFileRefusedOnAbsoluteEndpoint()
    {
        var fileId = await _handler.UploadFileAsync("prod", "x.pdf", Body("rel"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _handler.DownloadFileAsync(fileId, "prod", Path.Combine(_storageDir, "abs-root")));
    }

    private static string Pad(string base64Url)
    {
        var s = base64Url.Replace('-', '+').Replace('_', '/');
        return s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
    }
}
