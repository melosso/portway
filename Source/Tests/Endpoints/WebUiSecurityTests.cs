using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// Integration tests for Web UI CSRF enforcement, audit trail and security posture endpoint
/// </summary>
[Collection("Integration")]
public class WebUiSecurityTests : IDisposable
{
    private const string AdminKey = "test-admin-key-0123456789-0123456789-0123456789";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _authDbPath;
    private readonly string _mcpDbPath;
    private readonly string _overridesPath;
    private readonly string? _overridesBefore;

    public WebUiSecurityTests()
    {
        // Settings writes land in one appsettings.overrides.json shared by the whole test run
        _overridesPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.overrides.json");
        _overridesBefore = File.Exists(_overridesPath) ? File.ReadAllText(_overridesPath) : null;

        var id = Guid.NewGuid().ToString("N");
        _authDbPath = Path.Combine(Path.GetTempPath(), $"portway_uisec_{id}_auth.db");
        _mcpDbPath = Path.Combine(Path.GetTempPath(), $"portway_uisec_{id}_mcp.db");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration(config =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Mcp:Enabled"] = "false",
                        ["WebUi:AdminApiKey"] = AdminKey,
                        // TestServer connections have no remote IP, so allow via PublicOrigins instead of the local-network check
                        ["WebUi:PublicOrigins:0"] = "http://localhost"
                    });
                });

                builder.ConfigureTestServices(services =>
                {
                    services.AddDbContext<PortwayApi.Auth.AuthDbContext>(opts =>
                        opts.UseSqlite($"Data Source={_authDbPath}"),
                        ServiceLifetime.Scoped, ServiceLifetime.Scoped);
                    services.AddDbContextFactory<PortwayApi.Services.Mcp.McpConfigDbContext>(opts =>
                        opts.UseSqlite($"Data Source={_mcpDbPath}"));
                    services.Configure<PortwayApi.Middleware.RateLimitSettings>(options => options.Enabled = false);
                    services.AddLogging(logging =>
                    {
                        logging.ClearProviders();
                        logging.SetMinimumLevel(LogLevel.Error);
                    });
                });
            });
    }

    public void Dispose()
    {
        _factory.Dispose();
        if (File.Exists(_authDbPath)) File.Delete(_authDbPath);
        if (File.Exists(_mcpDbPath)) File.Delete(_mcpDbPath);

        if (_overridesBefore is not null) File.WriteAllText(_overridesPath, _overridesBefore);
        else if (File.Exists(_overridesPath)) File.Delete(_overridesPath);
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    // The seeded "admin" account gets a random one-time password
    private const string SeededPassword = "T3st-console-pw-9f2b";

    private async Task<(string AuthCookie, string CsrfCookie)> LoginAsync(HttpClient client)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortwayApi.Auth.AuthDbContext>();
        var admin = await db.AdminUsers.FirstAsync(u => u.Username == "admin");
        admin.PasswordHash = PortwayApi.Auth.AdminUserService.HashPassword(SeededPassword);
        admin.MustChangePassword = false;
        await db.SaveChangesAsync();

        return await SignInAsync(client, "admin", SeededPassword);
    }

    /// <summary>
    /// Signs in, completing a first-sign-in password change when the account still owes one
    /// </summary>
    private static async Task<(string AuthCookie, string CsrfCookie)> SignInAsync(
        HttpClient client, string username, string password, string? newPassword = null)
    {
        var csrfResp = await client.GetFromJsonAsync<JsonElement>("/ui/api/auth/csrf");
        var oneTimeCsrf = csrfResp.GetProperty("csrf").GetString()!;

        var login = await client.PostAsJsonAsync("/ui/api/auth",
            new { username, password, csrf = oneTimeCsrf });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        if (body.TryGetProperty("must_change_password", out var must) && must.GetBoolean())
        {
            Assert.NotNull(newPassword);
            var second = await client.GetFromJsonAsync<JsonElement>("/ui/api/auth/csrf");
            login = await client.PostAsJsonAsync("/ui/api/auth/password", new
            {
                username,
                password,
                newPassword,
                csrf = second.GetProperty("csrf").GetString()!
            });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        string? authCookie = null, csrfCookie = null;
        foreach (var setCookie in login.Headers.GetValues("Set-Cookie"))
        {
            var pair = setCookie.Split(';')[0];
            if (pair.StartsWith("portway_auth=")) authCookie = pair["portway_auth=".Length..];
            if (pair.StartsWith("portway_csrf=")) csrfCookie = pair["portway_csrf=".Length..];
        }
        Assert.NotNull(authCookie);
        Assert.NotNull(csrfCookie);
        return (authCookie!, csrfCookie!);
    }

    private static HttpRequestMessage AuthedRequest(HttpMethod method, string url, string authCookie, string? csrfHeader = null, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        var cookies = $"portway_auth={authCookie}";
        if (csrfHeader != null)
        {
            cookies += $"; portway_csrf={csrfHeader}";
            req.Headers.Add("X-CSRF-Token", Uri.UnescapeDataString(csrfHeader));
        }
        req.Headers.Add("Cookie", cookies);
        if (body != null) req.Content = JsonContent.Create(body);
        return req;
    }

    [Fact]
    public async Task UnauthenticatedUiApiRequest_RedirectsToLogin()
    {
        var client = CreateClient();
        var resp = await client.GetAsync("/ui/api/settings", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Contains("/ui/login", resp.Headers.Location!.ToString());
    }

    [Fact]
    public async Task EmptyAdminApiKey_DeniesUiApiInsteadOfLeavingItUnguarded()
    {
        // Middleware must deny ui api routes itself now that it always runs
        using var offFactory = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(
                new Dictionary<string, string?> { ["WebUi:AdminApiKey"] = "" })));
        var client = offFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var resp = await client.GetAsync("/ui/api/environments/500", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task ExplicitWebUiEnabled_KeepsUiReachableAfterAdminKeyIsCleared()
    {
        // The settings page clears WebUi:AdminApiKey once real accounts exist; WebUi:Enabled must keep the console open
        using var onFactory = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(
                new Dictionary<string, string?> { ["WebUi:AdminApiKey"] = "", ["WebUi:Enabled"] = "true" })));
        var client = onFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var resp = await client.GetAsync("/ui/api/environments/500", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Contains("/ui/login", resp.Headers.Location!.ToString());
    }

    [Fact]
    public async Task UnopenableAuthDatabase_NeverServesUiApiUnauthenticated()
    {
        // auth.db unreadable (e.g. a bad bind mount) must not fall back to "no accounts yet, skip login"
        var brokenPath = Path.Combine(Path.GetTempPath(), $"portway_broken_authdb_{Guid.NewGuid():N}");
        Directory.CreateDirectory(brokenPath);
        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Mcp:Enabled"] = "false",
                    ["WebUi:AdminApiKey"] = AdminKey,
                    ["WebUi:PublicOrigins:0"] = "http://localhost"
                }));
                b.ConfigureTestServices(services =>
                {
                    services.AddDbContext<PortwayApi.Auth.AuthDbContext>(opts =>
                        opts.UseSqlite($"Data Source={brokenPath}"),
                        ServiceLifetime.Scoped, ServiceLifetime.Scoped);
                    services.AddLogging(logging => { logging.ClearProviders(); logging.SetMinimumLevel(LogLevel.Error); });
                });
            });

            HttpResponseMessage? resp = null;
            try
            {
                var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
                resp = await client.GetAsync("/ui/api/settings", TestContext.Current.CancellationToken);
            }
            catch
            {
                // Refusing to start at all is the desired outcome
                return;
            }

            Assert.NotEqual(HttpStatusCode.OK, resp.StatusCode);
        }
        finally
        {
            Directory.Delete(brokenPath, recursive: true);
        }
    }

    [Fact]
    public async Task MutationWithoutCsrfHeader_Returns403()
    {
        var client = CreateClient();
        var (authCookie, _) = await LoginAsync(client);

        var req = AuthedRequest(HttpMethod.Put, "/ui/api/environments/settings", authCookie, csrfHeader: null, body: new { });
        var resp = await client.SendAsync(req, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Contains("CSRF", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task MutationWithCsrfHeader_SucceedsAndIsAudited()
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        var put = AuthedRequest(HttpMethod.Put, "/ui/api/environments/settings", authCookie, csrfCookie,
            new { server_name = "localhost", allowed_environments = new[] { "500", "700" } });
        var resp = await client.SendAsync(put, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var auditReq = AuthedRequest(HttpMethod.Get, "/ui/api/audit", authCookie);
        var auditResp = await client.SendAsync(auditReq, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, auditResp.StatusCode);
        var audit = await auditResp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var entries = audit.GetProperty("entries").EnumerateArray().ToList();
        Assert.Contains(entries, e =>
            e.GetProperty("action").GetString() == "update" &&
            e.GetProperty("target_type").GetString() == "environment-settings");
    }

    [Theory]
    [InlineData("sql", """{"DatabaseObjectName":"Items","DatabaseSchema":"dbo"}""", true)]
    [InlineData("sql", """{"DatabaseSchema":"dbo"}""", false)]
    [InlineData("proxy", """{"Url":"http://localhost:8020/svc","Methods":["GET"]}""", true)]
    [InlineData("proxy", """{"Methods":["GET"]}""", false)]
    [InlineData("static", """{"ContentType":"text/csv","Namespace":"1bad"}""", false)]
    public async Task ValidateEndpoint_ChecksTypeRules(string type, string content, bool expectValid)
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        var req = AuthedRequest(HttpMethod.Post, $"/ui/api/endpoints/{type}/validate", authCookie, csrfCookie,
            new { content });
        var resp = await client.SendAsync(req, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(expectValid, json.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task ValidateEndpoint_InvalidJson_ReturnsInvalidWithError()
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        var req = AuthedRequest(HttpMethod.Post, "/ui/api/endpoints/sql/validate", authCookie, csrfCookie,
            new { content = "{ not json" });
        var resp = await client.SendAsync(req, TestContext.Current.CancellationToken);

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.False(json.GetProperty("valid").GetBoolean());
        Assert.Contains("Invalid JSON", json.GetProperty("errors")[0].GetString());
    }

    [Fact]
    public async Task ComposedPage_ContainsShellViewAndTitle()
    {
        var client = CreateClient();
        var (authCookie, _) = await LoginAsync(client);

        var req = AuthedRequest(HttpMethod.Get, "/ui/settings", authCookie);
        var resp = await client.SendAsync(req, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var html = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("<title>Settings · Portway</title>", html);
        Assert.Contains("toastContainer", html);          // shell
        Assert.Contains("id=\"securityBody\"", html);     // view fragment
        Assert.EndsWith("</html>", html.TrimEnd());       // footer
    }

    [Fact]
    public async Task SettingsPage_SecuritySectionHoldsOnlySecurityCards()
    {
        var client = CreateClient();
        var (authCookie, _) = await LoginAsync(client);

        var resp = await client.SendAsync(AuthedRequest(HttpMethod.Get, "/ui/settings", authCookie), TestContext.Current.CancellationToken);
        var html = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        var start = html.IndexOf("id=\"section-security\"", StringComparison.Ordinal);
        var end = html.IndexOf("class=\"settings-section\"", start, StringComparison.Ordinal);
        var security = html[start..end];
        Assert.Contains("id=\"securityBody\"", security);
        Assert.DoesNotContain("customizationBody", security);
        Assert.DoesNotContain("featuresBody", html);
        Assert.Contains("id=\"section-general\"", html);
    }

    [Fact]
    public async Task SettingsEndpoint_ReportsSecurityPosture()
    {
        var client = CreateClient();
        var (authCookie, _) = await LoginAsync(client);

        var req = AuthedRequest(HttpMethod.Get, "/ui/api/settings", authCookie);
        var resp = await client.SendAsync(req, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var security = json.GetProperty("security");
        Assert.True(security.GetProperty("webui_auth_enabled").GetBoolean());
        Assert.True(security.GetProperty("admin_accounts").GetInt32() > 0);
        Assert.True(security.GetProperty("csrf_protection").GetBoolean());
    }

    [Fact]
    public async Task ViewerAccount_CannotWriteSettings_ButCanRead()
    {
        var client = CreateClient();
        var (adminCookie, adminCsrf) = await LoginAsync(client);

        var create = AuthedRequest(HttpMethod.Post, "/ui/api/users", adminCookie, adminCsrf,
            new { username = "read-only", password = "V13wer-account-pw-77", role = "viewer", current_password = SeededPassword });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(create, TestContext.Current.CancellationToken)).StatusCode);

        var (viewerCookie, viewerCsrf) = await SignInAsync(client, "read-only", "V13wer-account-pw-77");

        // Reading the console stays open to a viewer
        var read = AuthedRequest(HttpMethod.Get, "/ui/api/settings", viewerCookie);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(read, TestContext.Current.CancellationToken)).StatusCode);

        // Writing settings is administrator-only, CSRF satisfied or not
        var write = AuthedRequest(HttpMethod.Put, "/ui/api/settings", viewerCookie, viewerCsrf,
            new Dictionary<string, object> { ["Caching:Enabled"] = false });
        var writeResp = await client.SendAsync(write, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, writeResp.StatusCode);

        // And it must not be able to hand itself an administrator account
        var escalate = AuthedRequest(HttpMethod.Post, "/ui/api/users", viewerCookie, viewerCsrf,
            new { username = "climber", password = "Esc4lation-pw-1234", role = "administrator" });
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(escalate, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task ClearMetrics_RequiresAdministrator_AndEmptiesHealth()
    {
        var client = CreateClient();
        var (adminCookie, adminCsrf) = await LoginAsync(client);
        var metrics = _factory.Services.GetRequiredService<PortwayApi.Services.MetricsService>();
        metrics.Record(500, "GET", "api", "Orders", "500", "", 12);

        var create = AuthedRequest(HttpMethod.Post, "/ui/api/users", adminCookie, adminCsrf,
            new { username = "metrics-viewer", password = "V13wer-metrics-pw-88", role = "viewer", current_password = SeededPassword });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(create, TestContext.Current.CancellationToken)).StatusCode);
        var (viewerCookie, viewerCsrf) = await SignInAsync(client, "metrics-viewer", "V13wer-metrics-pw-88");

        var viewerRead = await client.SendAsync(AuthedRequest(HttpMethod.Get, "/ui/api/metrics/health?period=1h", viewerCookie), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, viewerRead.StatusCode);
        var health = await viewerRead.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.True(health.GetProperty("summary").GetProperty("failures").GetInt64() >= 1);

        var viewerClear = await client.SendAsync(AuthedRequest(HttpMethod.Delete, "/ui/api/metrics", viewerCookie, viewerCsrf), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, viewerClear.StatusCode);

        var adminClear = await client.SendAsync(AuthedRequest(HttpMethod.Delete, "/ui/api/metrics", adminCookie, adminCsrf), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, adminClear.StatusCode);
        Assert.Equal(0, metrics.GetHealth(TimeSpan.FromDays(30), new PortwayApi.Services.HealthFilter()).Summary.Total);
    }

    [Fact]
    public async Task DeactivatedAccount_LosesAccessImmediately_EvenOnAPlainReadRequest()
    {
        // One client per account, the factory client tracks Set-Cookie
        var adminClient = CreateClient();
        var (adminCookie, adminCsrf) = await LoginAsync(adminClient);

        var create = AuthedRequest(HttpMethod.Post, "/ui/api/users", adminCookie, adminCsrf,
            new { username = "soon-deactivated", password = "D3act1vate-me-pw-42", role = "viewer", current_password = SeededPassword });
        var createResp = await adminClient.SendAsync(create, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var targetId = created.GetProperty("id").GetInt32();

        var targetClient = CreateClient();
        var (targetCookie, _) = await SignInAsync(targetClient, "soon-deactivated", "D3act1vate-me-pw-42");

        // The now-deactivated account's own cookie still authenticates a plain GET before the fix
        var deactivate = AuthedRequest(HttpMethod.Put, $"/ui/api/users/{targetId}", adminCookie, adminCsrf,
            new Dictionary<string, object> { ["is_active"] = false, ["current_password"] = SeededPassword });
        Assert.Equal(HttpStatusCode.OK, (await adminClient.SendAsync(deactivate, TestContext.Current.CancellationToken)).StatusCode);

        var readAfterDeactivation = AuthedRequest(HttpMethod.Get, "/ui/api/settings", targetCookie);
        var resp = await targetClient.SendAsync(readAfterDeactivation, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Contains("/ui/login", resp.Headers.Location!.ToString());
    }

    [Fact]
    public async Task SettingsWrite_RejectsKeysOutsideTheWhitelistAndOverlongText()
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        var secret = AuthedRequest(HttpMethod.Put, "/ui/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["WebUi:AdminApiKey"] = "stolen" });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(secret, TestContext.Current.CancellationToken)).StatusCode);

        var tooLong = AuthedRequest(HttpMethod.Put, "/ui/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["WebUi:Customization:PromoText"] = new string('x', 2_001) });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(tooLong, TestContext.Current.CancellationToken)).StatusCode);

        var ok = AuthedRequest(HttpMethod.Put, "/ui/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["WebUi:Customization:PromoText"] = "Hello **there**" });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(ok, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task SettingsWrite_RejectsUnsafeOpenApiValues()
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        foreach (var (key, value) in new (string, object)[]
        {
            ("OpenApi:Footer:Url", "javascript:alert(1)"),
            ("OpenApi:Footer:Target", "_top"),
            ("OpenApi:Version", "../v1"),
            ("OpenApi:SecurityDefinition:In", "Query"),
            ("OpenApi:SecurityDefinition:Type", "OAuth2"),
            ("OpenApi:DefaultGroup", "A/B"),
            ("OpenApi:ScalarTheme", "evil"),
            ("OpenApi:ExternalDocs:Url", "javascript:alert(1)"),
            ("OpenApi:ExternalDocs:Url", "mailto:a@example.com"),
            ("OpenApi:Contact:Email", "not an address"),
            ("OpenApi:Title", new string('x', 201))
        })
        {
            var req = AuthedRequest(HttpMethod.Put, "/ui/api/settings", authCookie, csrfCookie, new Dictionary<string, object> { [key] = value });
            Assert.True(HttpStatusCode.BadRequest == (await client.SendAsync(req, TestContext.Current.CancellationToken)).StatusCode, key);
        }

        var ok = AuthedRequest(HttpMethod.Put, "/ui/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["OpenApi:ShowNamespaces"] = true, ["OpenApi:Footer:Url"] = "https://example.com", ["OpenApi:ScalarTheme"] = "portway", ["OpenApi:ShowBadges"] = false, ["OpenApi:MarkdownEnabled"] = true, ["OpenApi:ExternalDocs:Url"] = "", ["OpenApi:ExternalDocs:Description"] = "Guide" });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(ok, TestContext.Current.CancellationToken)).StatusCode);
    }

    // console version management: next version, guarded delete and rename, scope field in the list
    [Fact]
    public async Task Console_ManagesEndpointVersions()
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Static", "UiVer", "Items");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "content.json"), "[]", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(dir, "entity.json"), """{ "ContentType": "application/json", "ContentFile": "content.json", "Deprecated": true, "DeprecatedSince": "2026-01-01T00:00:00Z" }""", TestContext.Current.CancellationToken);
        try
        {
            var client = CreateClient();
            var (authCookie, csrfCookie) = await LoginAsync(client);
            async Task<HttpResponseMessage> Send(HttpMethod method, string url, object? body = null) =>
                await client.SendAsync(AuthedRequest(method, url, authCookie, csrfCookie, body), TestContext.Current.CancellationToken);

            var created = await Send(HttpMethod.Post, "/ui/api/endpoints/static/versions/UiVer/Items");
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            var body = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.Equal("UiVer/Items/v2", body.GetProperty("name").GetString());
            Assert.True(File.Exists(Path.Combine(dir, "v2", "content.json")));
            Assert.DoesNotContain("Deprecated", await File.ReadAllTextAsync(Path.Combine(dir, "v2", "entity.json"), TestContext.Current.CancellationToken));

            var saved = await Send(HttpMethod.Put, "/ui/api/endpoints/static/UiVer/Items/v2",
                new { content = new { ContentType = "application/json", ContentFile = "content.json", Deprecated = true } });
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Contains("\"DeprecatedSince\"", await File.ReadAllTextAsync(Path.Combine(dir, "v2", "entity.json"), TestContext.Current.CancellationToken));

            var third = await (await Send(HttpMethod.Post, "/ui/api/endpoints/static/versions/UiVer/Items/v2")).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.Equal("v3", third.GetProperty("version").GetString());

            var overview = await (await Send(HttpMethod.Get, "/ui/api/overview")).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.True(int.Parse(overview.GetProperty("api_version").GetString()![1..]) >= 3);

            var list = await (await Send(HttpMethod.Get, "/ui/api/endpoints")).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            var v2 = list.GetProperty("static").EnumerateArray().Single(e => e.GetProperty("name").GetString() == "UiVer/Items/v2");
            Assert.Equal("v2", v2.GetProperty("version").GetString());
            Assert.Equal("UiVer/Items@v2", v2.GetProperty("scope").GetString());

            Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Delete, "/ui/api/endpoints/static/UiVer/Items")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(new HttpMethod("PATCH"), "/ui/api/endpoints/static/UiVer/Items/v2", new { new_name = "UiVer/Other" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Delete, "/ui/api/endpoints/static/UiVer/Items/v3")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Delete, "/ui/api/endpoints/static/UiVer/Items/v2")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Delete, "/ui/api/endpoints/static/UiVer/Items")).StatusCode);
        }
        finally
        {
            var root = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Static", "UiVer");
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            PortwayApi.Classes.EndpointHandler.ReloadAllEndpoints();
        }
    }

    // token scopes are validated on create and on update
    [Fact]
    public async Task TokenScopes_AreValidated()
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        var bad = await client.SendAsync(AuthedRequest(HttpMethod.Post, "/ui/api/tokens", authCookie, csrfCookie,
            new { username = "scope-bad", allowed_scopes = "CRM Accounts" }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var good = await client.SendAsync(AuthedRequest(HttpMethod.Post, "/ui/api/tokens", authCookie, csrfCookie,
            new { username = "scope-good", allowed_scopes = "CRM/Accounts@v2" }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);

        var tokens = await (await client.SendAsync(AuthedRequest(HttpMethod.Get, "/ui/api/tokens", authCookie), TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var id = tokens.EnumerateArray()
            .First(x => x.GetProperty("username").GetString() == "scope-good").GetProperty("id").GetInt32();

        var update = await client.SendAsync(AuthedRequest(HttpMethod.Put, $"/ui/api/tokens/{id}", authCookie, csrfCookie,
            new { allowed_scopes = "CRM/Accounts@v1" }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
    }

    /// <summary>
    /// Puts one enabled provider in the database so the kill switch has something to hide
    /// </summary>
    private async Task SeedProviderAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortwayApi.Auth.AuthDbContext>();
        await db.Database.EnsureCreatedAsync();
        if (!await db.OidcProviders.AnyAsync(p => p.Slug == "acme"))
        {
            db.OidcProviders.Add(new PortwayApi.Auth.OidcProvider
            {
                Slug = "acme",
                Name = "Acme SSO",
                Authority = "https://sso.invalid",
                ClientId = "portway",
                IsEnabled = true
            });
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task OidcDisabled_HidesEveryProviderAndRefusesTheStartRoute()
    {
        await SeedProviderAsync(_factory);

        // The provider is live while the switch is on, so the assertions below test the switch and not an empty table
        var on = CreateClient();
        var listed = await on.GetFromJsonAsync<JsonElement>("/ui/api/auth/providers", TestContext.Current.CancellationToken);
        Assert.Contains(listed.GetProperty("providers").EnumerateArray(),
            p => p.GetProperty("slug").GetString() == "acme");

        using var offFactory = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Oidc:Enabled"] = "false" })));
        var client = offFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var providers = await client.GetFromJsonAsync<JsonElement>("/ui/api/auth/providers", TestContext.Current.CancellationToken);
        Assert.Empty(providers.GetProperty("providers").EnumerateArray());

        var stillEnabled = await client.GetAsync("/ui/api/auth/oidc/acme/start", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, stillEnabled.StatusCode);

        // Disabled OIDC answers the start route with the unknown slug 404
        var unknown = await client.GetAsync("/ui/api/auth/oidc/anything/start", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Theory]
    // Refused: a network covering every address lets any client forge its own IP
    [InlineData("ForwardedHeaders:KnownNetworks", new[] { "0.0.0.0/0" }, false)]
    [InlineData("ForwardedHeaders:KnownNetworks", new[] { "::/0" }, false)]
    [InlineData("ForwardedHeaders:KnownNetworks", new[] { "172.18.0.0/16" }, true)]
    [InlineData("ForwardedHeaders:KnownProxies", new[] { "not-an-ip" }, false)]
    [InlineData("ForwardedHeaders:KnownProxies", new[] { "10.0.0.8", "10.0.0.9" }, true)]
    [InlineData("WebUi:PublicOrigins", new[] { "portway.example.com" }, false)]
    [InlineData("WebUi:PublicOrigins", new[] { "https://*" }, false)]
    public async Task DeploymentLists_AreValidatedEntryByEntry(string key, string[] values, bool expectOk)
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        var req = AuthedRequest(HttpMethod.Put, "/ui/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { [key] = values });
        var resp = await client.SendAsync(req, TestContext.Current.CancellationToken);

        Assert.Equal(expectOk ? HttpStatusCode.OK : HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PublicOrigins_RefusesAChangeThatWouldLockTheCallerOut()
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        // This session reaches /ui via PublicOrigins only, so replacing it is refused
        var evict = AuthedRequest(HttpMethod.Put, "/ui/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["WebUi:PublicOrigins"] = new[] { "https://elsewhere.example.com" } });
        var evictResp = await client.SendAsync(evict, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, evictResp.StatusCode);
        var problem = await evictResp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Contains("refuse your own requests", problem.GetProperty("error").GetString());

        // Keeping an entry that still covers the caller is allowed
        var keep = AuthedRequest(HttpMethod.Put, "/ui/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["WebUi:PublicOrigins"] = new[] { "http://localhost", "https://elsewhere.example.com" } });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(keep, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task SeedingKey_CanBeClearedButNeverSet()
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        var set = AuthedRequest(HttpMethod.Put, "/ui/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["WebUi:AdminApiKey"] = "a-brand-new-secret-value" });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(set, TestContext.Current.CancellationToken)).StatusCode);

        var clear = AuthedRequest(HttpMethod.Put, "/ui/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["WebUi:AdminApiKey"] = "" });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(clear, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task UntrustedForwardedFor_CannotChooseItsOwnClientIp()
    {
        // Untrusted X-Forwarded-For must not set RemoteIpAddress
        var client = CreateClient();
        var (authCookie, _) = await LoginAsync(client);

        var req = AuthedRequest(HttpMethod.Get, "/ui/api/settings", authCookie);
        req.Headers.Add("X-Forwarded-For", "203.0.113.9");
        var json = await (await client.SendAsync(req, TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        var security = json.GetProperty("security");
        Assert.False(security.GetProperty("trusted_proxies_configured").GetBoolean());
        Assert.NotEqual("203.0.113.9", security.GetProperty("client_ip").GetString());
    }

    [Theory]
    [InlineData("http://localhost/ui/api/settings", false)]
    [InlineData("https://localhost/ui/api/settings", true)]
    public async Task SettingsEndpoint_ReportsHttpsFromTheRequestScheme(string url, bool expectHttps)
    {
        var client = CreateClient();
        var (authCookie, _) = await LoginAsync(client);

        var req = AuthedRequest(HttpMethod.Get, url, authCookie);
        req.Headers.Add("Origin", "http://localhost");
        var resp = await client.SendAsync(req, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(expectHttps, json.GetProperty("security").GetProperty("https_enabled").GetBoolean());
    }

    [Fact]
    public async Task SettingsEndpoint_ReportsWhetherForwardedHeadersAreHonoured()
    {
        var client = CreateClient();
        var (authCookie, _) = await LoginAsync(client);

        var req = AuthedRequest(HttpMethod.Get, "/ui/api/settings", authCookie);
        req.Headers.Add("X-Forwarded-For", "203.0.113.9");
        var resp = await client.SendAsync(req, TestContext.Current.CancellationToken);

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var security = json.GetProperty("security");

        var behindProxy = security.GetProperty("behind_proxy").GetBoolean();
        var trusted = security.GetProperty("trusted_proxies_configured").GetBoolean();
        var ignored = security.GetProperty("forwarded_ignored").GetBoolean();

        Assert.True(behindProxy);   // the header was sent, so the deployment looks proxied
        // Asserted as a relationship, tests share appsettings.overrides.json
        Assert.Equal(behindProxy && !trusted, ignored);
    }

    [Fact]
    public async Task ConsoleResponses_IncludeTheHardenedSecurityHeaders()
    {
        var client = CreateClient();
        var resp = await client.GetAsync("/ui/login", TestContext.Current.CancellationToken);

        Assert.False(resp.Headers.Contains("X-Powered-By"));
        Assert.Equal("same-origin", resp.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
        Assert.Equal("same-origin", resp.Headers.GetValues("Cross-Origin-Resource-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", resp.Headers.GetValues("Content-Security-Policy").Single());
    }
}
