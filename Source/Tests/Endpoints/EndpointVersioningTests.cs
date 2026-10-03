using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PortwayApi.Auth;
using PortwayApi.Classes;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// Versioned endpoints: v{n} folders, /api/{env}/v{n}/ routes, version scoped tokens
/// </summary>
public sealed class EndpointVersioningTests : ApiTestBase, IDisposable
{
    private static readonly string[] EndpointDirs =
    [
        Path.Combine("endpoints", "Static", "VerTest"),
        Path.Combine("endpoints", "Static", "VerFlat"),
        Path.Combine("endpoints", "Static", "v3"),
        Path.Combine("endpoints", "Files", "VerDocs"),
    ];

    public EndpointVersioningTests()
    {
        WriteStatic(Path.Combine(EndpointDirs[0], "Items"), "one", deprecated: true);
        WriteStatic(Path.Combine(EndpointDirs[0], "Items", "v2"), "two");
        WriteStatic(Path.Combine(EndpointDirs[0], "Legacy"), "root");
        WriteStatic(Path.Combine(EndpointDirs[0], "Legacy", "v1"), "v1-folder");
        WriteStatic(EndpointDirs[1], "flat-one");
        WriteStatic(Path.Combine(EndpointDirs[1], "v2"), "flat-two");
        WriteStatic(Path.Combine(EndpointDirs[2], "Things"), "namespace-v3");
        WriteFile(EndpointDirs[3], "verdocs");
        WriteFile(Path.Combine(EndpointDirs[3], "v2"), "verdocs-v2");
        EndpointHandler.ReloadAllEndpoints();
        SetAllowedEnvironments("500");

        Token("scoped-v1", "VerTest/Items");
        Token("scoped-v2", "VerTest/Items@v2");
        Token("scoped-all", "VerTest/Items*");
        Token("files-one", "files/VerDocs");
        Token("files-v2", "files/VerDocs@v2");
        Token("files-legacy", "files");
    }

    public new void Dispose()
    {
        foreach (var dir in EndpointDirs.Where(Directory.Exists))
            Directory.Delete(dir, recursive: true);
        EndpointHandler.ReloadAllEndpoints();
        base.Dispose();
    }

    private static void WriteStatic(string dir, string marker, bool deprecated = false)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "content.json"), JsonSerializer.Serialize(new[] { new { marker } }));
        File.WriteAllText(Path.Combine(dir, "entity.json"), JsonSerializer.Serialize(new
        {
            ContentType = "application/json",
            ContentFile = "content.json",
            AllowedEnvironments = new[] { "500" },
            Deprecated = deprecated,
            DeprecatedSince = deprecated ? "2026-01-01T00:00:00Z" : null,
            Sunset = deprecated ? "2027-01-01T00:00:00Z" : null
        }));
    }

    private static void WriteFile(string dir, string baseDirectory)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "entity.json"), JsonSerializer.Serialize(new
        {
            StorageType = "Local",
            BaseDirectory = Path.Combine(Path.GetTempPath(), "portway-version-tests", baseDirectory),
            AllowedEnvironments = new[] { "500" }
        }));
    }

    private void Token(string token, string scopes)
    {
        _mockTokenService.Setup(s => s.VerifyTokenAsync(token)).ReturnsAsync(true);
        _mockTokenService.Setup(s => s.GetTokenDetailsByTokenAsync(token)).ReturnsAsync(new AuthToken
        {
            Username = token,
            TokenHash = "hash",
            TokenSalt = "salt",
            AllowedScopes = scopes,
            AllowedEnvironments = "*",
        });
    }

    private async Task<HttpResponseMessage> Get(string url, string token = "test-token")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<string> Marker(string url)
    {
        var response = await Get(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(body).RootElement.EnumerateArray().First().GetProperty("marker").GetString()!;
    }

    [Fact]
    public async Task UnversionedRoute_ServesTheEndpointFolder() =>
        Assert.Equal("one", await Marker("/api/500/VerTest/Items"));

    [Fact]
    public async Task V1Route_IsTheUnversionedEndpoint() =>
        Assert.Equal("one", await Marker("/api/500/v1/VerTest/Items"));

    [Fact]
    public async Task VersionRoute_ServesTheVersionFolder() =>
        Assert.Equal("two", await Marker("/api/500/v2/VerTest/Items"));

    [Fact]
    public async Task UnknownVersion_IsNotFound() =>
        Assert.Equal(HttpStatusCode.NotFound, (await Get("/api/500/v9/VerTest/Items")).StatusCode);

    [Fact]
    public async Task FlatEndpoint_SupportsVersions()
    {
        Assert.Equal("flat-one", await Marker("/api/500/VerFlat"));
        Assert.Equal("flat-two", await Marker("/api/500/v2/VerFlat"));
    }

    [Fact]
    public async Task NamespaceNamedLikeAVersion_StillResolves() =>
        Assert.Equal("namespace-v3", await Marker("/api/500/v3/Things"));

    // a v1 folder defines v1 and the entity.json beside it is skipped
    [Fact]
    public async Task V1Folder_TakesPrecedenceOverTheEndpointFile()
    {
        Assert.Equal("v1-folder", await Marker("/api/500/VerTest/Legacy"));
        Assert.Equal("v1-folder", await Marker("/api/500/v1/VerTest/Legacy"));
        Assert.DoesNotContain(EndpointHandler.GetStaticEndpoints().Keys, k => k.Contains("@v1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolver_ReturnsVersionedName()
    {
        var resolved = PortwayApi.Api.EndpointController.ResolveEndpointIdentity(["v2", "VerTest", "Items", "extra"]);

        Assert.NotNull(resolved);
        Assert.Equal("VerTest", resolved.Value.Namespace);
        Assert.Equal("Items@v2", resolved.Value.Name);
        Assert.Equal(2, resolved.Value.NameIndex);
    }

    [Theory]
    [InlineData("scoped-v1", "/api/500/VerTest/Items", HttpStatusCode.OK)]
    [InlineData("scoped-v1", "/api/500/v1/VerTest/Items", HttpStatusCode.OK)]
    [InlineData("scoped-v1", "/api/500/v2/VerTest/Items", HttpStatusCode.Forbidden)]
    [InlineData("scoped-v2", "/api/500/v2/VerTest/Items", HttpStatusCode.OK)]
    [InlineData("scoped-v2", "/api/500/VerTest/Items", HttpStatusCode.Forbidden)]
    [InlineData("scoped-all", "/api/500/VerTest/Items", HttpStatusCode.OK)]
    [InlineData("scoped-all", "/api/500/v2/VerTest/Items", HttpStatusCode.OK)]
    public async Task Scope_IsPerVersion(string token, string url, HttpStatusCode expected) =>
        Assert.Equal(expected, (await Get(url, token)).StatusCode);

    [Theory]
    [InlineData("files-one", "/api/500/files/VerDocs/list", HttpStatusCode.OK)]
    [InlineData("files-one", "/api/500/v2/files/VerDocs/list", HttpStatusCode.Forbidden)]
    [InlineData("files-v2", "/api/500/v2/files/VerDocs/list", HttpStatusCode.OK)]
    [InlineData("files-v2", "/api/500/files/VerDocs/list", HttpStatusCode.Forbidden)]
    [InlineData("files-legacy", "/api/500/files/VerDocs/list", HttpStatusCode.OK)]
    [InlineData("files-legacy", "/api/500/v2/files/VerDocs/list", HttpStatusCode.OK)]
    [InlineData("scoped-all", "/api/500/files/VerDocs/list", HttpStatusCode.Forbidden)]
    public async Task FileScope_IsPerEndpointAndVersion(string token, string url, HttpStatusCode expected) =>
        Assert.Equal(expected, (await Get(url, token)).StatusCode);

    [Fact]
    public async Task DeprecatedVersion_SendsLifecycleHeaders()
    {
        var response = await Get("/api/500/VerTest/Items");

        Assert.Equal("@1767225600", response.Headers.GetValues("Deprecation").Single());
        Assert.Equal("Fri, 01 Jan 2027 00:00:00 GMT", response.Headers.GetValues("Sunset").Single());
        Assert.Equal("</api/500/v2/VerTest/Items>; rel=\"successor-version\"", response.Headers.GetValues("Link").Single());
        Assert.False((await Get("/api/500/v2/VerTest/Items")).Headers.Contains("Deprecation"));
    }

    private async Task<JsonDocument> Document(string url)
    {
        var response = await Get(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FullDocument_ListsEveryVersionWithUniqueOperations()
    {
        using var doc = await Document("/docs/openapi.json");
        var paths = doc.RootElement.GetProperty("paths");

        var v1 = paths.GetProperty("/api/{env}/VerTest/Items").GetProperty("get");
        var v2 = paths.GetProperty("/api/{env}/v2/VerTest/Items").GetProperty("get");
        Assert.NotEqual(v1.GetProperty("operationId").GetString(), v2.GetProperty("operationId").GetString());
        Assert.EndsWith("_v2", v2.GetProperty("operationId").GetString());
        Assert.EndsWith("(v2)", v2.GetProperty("summary").GetString());
        Assert.Equal("v2", v2.GetProperty("x-badges")[0].GetProperty("name").GetString());
        Assert.False(v1.TryGetProperty("x-badges", out var v1Badges) && v1Badges.EnumerateArray().Any(b => b.GetProperty("name").GetString() == "v1"));
        Assert.Equal(v1.GetProperty("tags")[0].GetString(), v2.GetProperty("tags")[0].GetString());
        Assert.True(paths.TryGetProperty("/api/{env}/v2/files/VerDocs", out _));

        var ids = paths.EnumerateObject().SelectMany(p => p.Value.EnumerateObject())
            .Where(o => o.Value.ValueKind == JsonValueKind.Object && o.Value.TryGetProperty("operationId", out _))
            .Select(o => o.Value.GetProperty("operationId").GetString()).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task VersionDocument_HoldsOnlyThatVersion()
    {
        using var v2 = await Document("/docs/openapi/v2/openapi.json");
        var v2Paths = v2.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("/api/{env}/v2/VerTest/Items", v2Paths);
        Assert.All(v2Paths, p => Assert.StartsWith("/api/{env}/v2/", p));
        Assert.EndsWith("/docs/openapi/v2/openapi.json", v2.RootElement.GetProperty("$self").GetString());
        Assert.Equal("v2", v2.RootElement.GetProperty("info").GetProperty("version").GetString());

        using var v1 = await Document("/docs/openapi/v1/openapi.json");
        var v1Paths = v1.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("/api/{env}/VerTest/Items", v1Paths);
        Assert.DoesNotContain("/api/{env}/v2/VerTest/Items", v1Paths);

        Assert.Equal(HttpStatusCode.NotFound, (await Get("/docs/openapi/v9/openapi.json")).StatusCode);
    }

    [Fact]
    public async Task DocsPage_OffersAVersionSwitch()
    {
        var html = await _client.GetStringAsync("/docs", TestContext.Current.CancellationToken);
        var config = System.Text.RegularExpressions.Regex.Match(html, "data-configuration='([^']*)'").Groups[1].Value;
        using var json = JsonDocument.Parse(config);

        var slugs = json.RootElement.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("slug").GetString()).ToList();
        Assert.Equal(["all", "v1", "v2"], slugs);
        Assert.DoesNotContain("data-url=", html);
    }

    [Theory]
    [InlineData("*", true)]
    [InlineData("CRM/Accounts, CRM/Accounts@v2, CRM/*, files/Images, composite/Orders", true)]
    [InlineData("GET:Products", false)]
    [InlineData("Product*, Products", true)]
    [InlineData("CRM/Accounts@v1", false)]
    [InlineData("CRM/Accounts@v0", false)]
    [InlineData("CRM Accounts", false)]
    [InlineData("CRM/../Accounts", false)]
    [InlineData("<script>", false)]
    public void ScopeValidator_AcceptsOnlyEndpointKeys(string scopes, bool valid) =>
        Assert.Equal(valid, PortwayApi.Helpers.TokenScopeValidator.Validate(scopes) is null);

    // hot reload under load never serves the wrong version or a transient refusal
    [Fact]
    public async Task ConcurrentRequestsDuringReload_ServeTheRightVersion()
    {
        var cases = new[]
        {
            ("/api/500/VerTest/Items", "scoped-v1", "one"),
            ("/api/500/v1/VerTest/Items", "scoped-all", "one"),
            ("/api/500/v2/VerTest/Items", "scoped-v2", "two"),
            ("/api/500/v2/VerFlat", "test-token", "flat-two"),
        };

        using var stop = new CancellationTokenSource();
        var reloads = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                EndpointHandler.ReloadAllEndpoints();
                await Task.Delay(5);
            }
        });

        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        await Parallel.ForEachAsync(Enumerable.Range(0, 400), new ParallelOptions { MaxDegreeOfParallelism = 16 }, async (i, ct) =>
        {
            var (url, token, expected) = cases[i % cases.Length];
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode != HttpStatusCode.OK || !body.Contains($"\"{expected}\""))
                failures.Add($"{url} {token}: {(int)response.StatusCode} {body[..Math.Min(body.Length, 80)]}");
        });

        stop.Cancel();
        await reloads;
        Assert.True(failures.IsEmpty, string.Join("\n", failures.Take(10)));
    }

    [Theory]
    [InlineData("Inventory/Products", "Inventory/Products", "files/Inventory/Products")]
    [InlineData("Inventory/Products@v2", "v2/Inventory/Products", "v2/files/Inventory/Products")]
    [InlineData("Images@v3", "v3/Images", "v3/files/Images")]
    public void KeysBecomeRoutes(string key, string route, string fileRoute)
    {
        Assert.Equal(route, EndpointVersion.RoutePath(key));
        Assert.Equal(fileRoute, EndpointVersion.FileRoutePath(key));
    }

    // links a versioned endpoint returns must route back to the same version
    [Fact]
    public async Task VersionedFileUpload_ReturnsARoutableUrl()
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent("hello"u8.ToArray()), "file", "hello.txt");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/500/v2/files/VerDocs") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var url = response.Headers.Location?.OriginalString ?? "";
        Assert.StartsWith("/api/500/v2/files/VerDocs/", url);
        Assert.Equal(HttpStatusCode.OK, (await Get(url)).StatusCode);
    }
}
