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

/// <summary>Integration tests for Web UI CSRF enforcement, audit trail and security posture endpoint</summary>
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
        _mcpDbPath  = Path.Combine(Path.GetTempPath(), $"portway_uisec_{id}_mcp.db");

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
        if (File.Exists(_mcpDbPath))  File.Delete(_mcpDbPath);

        if (_overridesBefore is not null) File.WriteAllText(_overridesPath, _overridesBefore);
        else if (File.Exists(_overridesPath)) File.Delete(_overridesPath);
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    // WebUi:AdminApiKey seeds the account "admin" with the key as its password and MustChangePassword set,
    // so the first sign-in is a two-step: authenticate, then choose a password to get the session.
    private const string SeededPassword = "T3st-console-pw-9f2b";

    /// <summary>Signs in as the seeded administrator and returns the auth and csrf cookie values</summary>
    private Task<(string AuthCookie, string CsrfCookie)> LoginAsync(HttpClient client) =>
        SignInAsync(client, "admin", AdminKey, SeededPassword);

    /// <summary>Signs in, completing a first-sign-in password change when the account still owes one</summary>
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
        var resp = await client.GetAsync("/ui/api/settings");
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Contains("/ui/login", resp.Headers.Location!.ToString());
    }

    [Fact]
    public async Task MutationWithoutCsrfHeader_Returns403()
    {
        var client = CreateClient();
        var (authCookie, _) = await LoginAsync(client);

        var req = AuthedRequest(HttpMethod.Put, "/ui/api/environments/settings", authCookie, csrfHeader: null, body: new { });
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("CSRF", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task MutationWithCsrfHeader_SucceedsAndIsAudited()
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        var put = AuthedRequest(HttpMethod.Put, "/ui/api/environments/settings", authCookie, csrfCookie,
            new { server_name = "localhost", allowed_environments = new[] { "500", "700" } });
        var resp = await client.SendAsync(put);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var auditReq = AuthedRequest(HttpMethod.Get, "/ui/api/audit", authCookie);
        var auditResp = await client.SendAsync(auditReq);
        Assert.Equal(HttpStatusCode.OK, auditResp.StatusCode);
        var audit = await auditResp.Content.ReadFromJsonAsync<JsonElement>();
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
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectValid, json.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task ValidateEndpoint_InvalidJson_ReturnsInvalidWithError()
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        var req = AuthedRequest(HttpMethod.Post, "/ui/api/endpoints/sql/validate", authCookie, csrfCookie,
            new { content = "{ not json" });
        var resp = await client.SendAsync(req);

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("valid").GetBoolean());
        Assert.Contains("Invalid JSON", json.GetProperty("errors")[0].GetString());
    }

    [Fact]
    public async Task ComposedPage_ContainsShellViewAndTitle()
    {
        var client = CreateClient();
        var (authCookie, _) = await LoginAsync(client);

        var req = AuthedRequest(HttpMethod.Get, "/ui/settings", authCookie);
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("<title>Settings · Portway</title>", html);
        Assert.Contains("toastContainer", html);          // shell
        Assert.Contains("id=\"securityBody\"", html);     // view fragment
        Assert.EndsWith("</html>", html.TrimEnd());       // footer
    }

    [Fact]
    public async Task SettingsEndpoint_ReportsSecurityPosture()
    {
        var client = CreateClient();
        var (authCookie, _) = await LoginAsync(client);

        var req = AuthedRequest(HttpMethod.Get, "/ui/api/settings", authCookie);
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
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
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(create)).StatusCode);

        var (viewerCookie, viewerCsrf) = await SignInAsync(client, "read-only", "V13wer-account-pw-77");

        // Reading the console stays open to a viewer
        var read = AuthedRequest(HttpMethod.Get, "/ui/api/settings", viewerCookie);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(read)).StatusCode);

        // Writing settings is administrator-only, CSRF satisfied or not
        var write = AuthedRequest(HttpMethod.Put, "/ui/api/settings", viewerCookie, viewerCsrf,
            new Dictionary<string, object> { ["Caching:Enabled"] = false });
        var writeResp = await client.SendAsync(write);
        Assert.Equal(HttpStatusCode.Forbidden, writeResp.StatusCode);

        // And it must not be able to hand itself an administrator account
        var escalate = AuthedRequest(HttpMethod.Post, "/ui/api/users", viewerCookie, viewerCsrf,
            new { username = "climber", password = "Esc4lation-pw-1234", role = "administrator" });
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(escalate)).StatusCode);
    }

    [Fact]
    public async Task SettingsWrite_RejectsKeysOutsideTheWhitelistAndOverlongText()
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        var secret = AuthedRequest(HttpMethod.Put, "/ui/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["WebUi:AdminApiKey"] = "stolen" });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(secret)).StatusCode);

        var tooLong = AuthedRequest(HttpMethod.Put, "/ui/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["WebUi:Customization:PromoText"] = new string('x', 2_001) });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(tooLong)).StatusCode);

        var ok = AuthedRequest(HttpMethod.Put, "/ui/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["WebUi:Customization:PromoText"] = "Hello **there**" });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(ok)).StatusCode);
    }

    /// <summary>Puts one enabled provider in the database so the kill switch has something to hide</summary>
    private async Task SeedProviderAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortwayApi.Auth.AuthDbContext>();
        await db.Database.EnsureCreatedAsync();
        if (!await db.OidcProviders.AnyAsync(p => p.Slug == "acme"))
        {
            db.OidcProviders.Add(new PortwayApi.Auth.OidcProvider
            {
                Slug = "acme", Name = "Acme SSO", Authority = "https://sso.invalid",
                ClientId = "portway", IsEnabled = true
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
        var listed = await on.GetFromJsonAsync<JsonElement>("/ui/api/auth/providers");
        Assert.Contains(listed.GetProperty("providers").EnumerateArray(),
            p => p.GetProperty("slug").GetString() == "acme");

        using var offFactory = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Oidc:Enabled"] = "false" })));
        var client = offFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var providers = await client.GetFromJsonAsync<JsonElement>("/ui/api/auth/providers");
        Assert.Empty(providers.GetProperty("providers").EnumerateArray());

        var stillEnabled = await client.GetAsync("/ui/api/auth/oidc/acme/start");
        Assert.Equal(HttpStatusCode.NotFound, stillEnabled.StatusCode);

        // The switch has to hold at the start route too, not just hide the buttons:
        // with it off every slug is unknown, which is the same 404 an unknown slug already gets
        var unknown = await client.GetAsync("/ui/api/auth/oidc/anything/start");
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
        var resp = await client.SendAsync(req);

        Assert.Equal(expectOk ? HttpStatusCode.OK : HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PublicOrigins_RefusesAChangeThatWouldLockTheCallerOut()
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        // TestServer connections carry no remote IP, so this session reaches /ui only through PublicOrigins.
        // Replacing the entry that admits it must be refused rather than applied and discovered on restart.
        var evict = AuthedRequest(HttpMethod.Put, "/ui/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["WebUi:PublicOrigins"] = new[] { "https://elsewhere.example.com" } });
        var evictResp = await client.SendAsync(evict);

        Assert.Equal(HttpStatusCode.BadRequest, evictResp.StatusCode);
        var problem = await evictResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("refuse your own requests", problem.GetProperty("error").GetString());

        // Keeping an entry that still covers the caller is allowed
        var keep = AuthedRequest(HttpMethod.Put, "/ui/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["WebUi:PublicOrigins"] = new[] { "http://localhost", "https://elsewhere.example.com" } });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(keep)).StatusCode);
    }

    [Fact]
    public async Task SeedingKey_CanBeClearedButNeverSet()
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        var set = AuthedRequest(HttpMethod.Put, "/ui/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["WebUi:AdminApiKey"] = "a-brand-new-secret-value" });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(set)).StatusCode);

        var clear = AuthedRequest(HttpMethod.Put, "/ui/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["WebUi:AdminApiKey"] = "" });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(clear)).StatusCode);
    }

    [Fact]
    public async Task UntrustedForwardedFor_CannotChooseItsOwnClientIp()
    {
        // ForwardedHeadersMiddleware skips its trust check when both lists are empty, so registering it
        // with nothing configured would let any caller set RemoteIpAddress through X-Forwarded-For and
        // walk past the console's local-network gate, the per-IP rate limiter and the login lockout.
        var client = CreateClient();
        var (authCookie, _) = await LoginAsync(client);

        var req = AuthedRequest(HttpMethod.Get, "/ui/api/settings", authCookie);
        req.Headers.Add("X-Forwarded-For", "203.0.113.9");
        var json = await (await client.SendAsync(req)).Content.ReadFromJsonAsync<JsonElement>();

        var security = json.GetProperty("security");
        Assert.False(security.GetProperty("trusted_proxies_configured").GetBoolean());
        Assert.NotEqual("203.0.113.9", security.GetProperty("client_ip").GetString());
    }

    [Fact]
    public async Task SettingsEndpoint_ReportsWhetherForwardedHeadersAreHonoured()
    {
        var client = CreateClient();
        var (authCookie, _) = await LoginAsync(client);

        var req = AuthedRequest(HttpMethod.Get, "/ui/api/settings", authCookie);
        req.Headers.Add("X-Forwarded-For", "203.0.113.9");
        var resp = await client.SendAsync(req);

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var security = json.GetProperty("security");

        var behindProxy = security.GetProperty("behind_proxy").GetBoolean();
        var trusted     = security.GetProperty("trusted_proxies_configured").GetBoolean();
        var ignored     = security.GetProperty("forwarded_ignored").GetBoolean();

        Assert.True(behindProxy);   // the header was sent, so the deployment looks proxied
        // The warning fires exactly when a forwarded address arrives with no proxy trusted to send it.
        // Asserted as the relationship because sibling tests share one appsettings.overrides.json.
        Assert.Equal(behindProxy && !trusted, ignored);
    }

    [Fact]
    public async Task ConsoleResponses_CarryTheHardenedSecurityHeaders()
    {
        var client = CreateClient();
        var resp = await client.GetAsync("/ui/login");

        Assert.False(resp.Headers.Contains("X-Powered-By"));
        Assert.Equal("same-origin", resp.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
        Assert.Equal("same-origin", resp.Headers.GetValues("Cross-Origin-Resource-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", resp.Headers.GetValues("Content-Security-Policy").Single());
    }
}
