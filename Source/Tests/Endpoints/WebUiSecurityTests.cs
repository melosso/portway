using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PortwayApi.Classes;
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
                        ["Proxy:Username"] = "svc-proxy-account",
                        ["Telemetry:Otlp:Endpoint"] = "https://otel.example/?token=otlp-secret",
                        ["Console:AdminApiKey"] = AdminKey,
                        // TestServer connections have no remote IP, so allow via PublicOrigins instead of the local-network check
                        ["Console:PublicOrigins:0"] = "http://localhost"
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
        var csrfResp = await client.GetFromJsonAsync<JsonElement>("/_/api/auth/csrf");
        var oneTimeCsrf = csrfResp.GetProperty("csrf").GetString()!;

        var login = await client.PostAsJsonAsync("/_/api/auth",
            new { username, password, csrf = oneTimeCsrf });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        if (body.TryGetProperty("must_change_password", out var must) && must.GetBoolean())
        {
            Assert.NotNull(newPassword);
            var second = await client.GetFromJsonAsync<JsonElement>("/_/api/auth/csrf");
            login = await client.PostAsJsonAsync("/_/api/auth/password", new
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
    public async Task Logs_ReadOlderFile_WhenNewestFileIsLarge()
    {
        var ct = TestContext.Current.CancellationToken;
        var logDir = Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "log")).FullName;
        var newest = Path.Combine(logDir, "portwayapi-20991231.log");
        var older = Path.Combine(logDir, "portwayapi-20991230.log");
        try
        {
            await File.WriteAllLinesAsync(newest, Enumerable.Range(0, 1500)
                .Select(i => $"2099-12-31 00:{i / 60:00}:{i % 60:00}.000 +00:00 [INF] filler {i}"), ct);
            await File.WriteAllTextAsync(older, "2099-12-30 12:00:00.000 +00:00 [ERR] older-file-marker\n", ct);
            File.SetLastWriteTimeUtc(newest, DateTime.UtcNow.AddDays(2));
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(1));

            var client = CreateClient();
            var (authCookie, _) = await LoginAsync(client);
            var resp = await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/api/logs?level=ERR&limit=200", authCookie), ct);

            var body = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal("older-file-marker", body.GetProperty("lines")[0].GetProperty("message").GetString());
        }
        finally
        {
            File.Delete(newest);
            File.Delete(older);
        }
    }

    [Theory]
    [InlineData(true, 30 * 24)]
    [InlineData(false, 12)]
    public async Task SignIn_RememberSetsSessionLifetime(bool remember, int hours)
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClient();
        await LoginAsync(client);

        var csrf = (await client.GetFromJsonAsync<JsonElement>("/_/api/auth/csrf", ct)).GetProperty("csrf").GetString();
        var login = await client.PostAsJsonAsync("/_/api/auth", new { username = "admin", password = SeededPassword, csrf, remember }, ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var setCookie = login.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("portway_auth="));
        var signedExpiry = DateTimeOffset.FromUnixTimeSeconds(long.Parse(Uri.UnescapeDataString(setCookie.Split(';')[0]["portway_auth=".Length..]).Split('.')[1]));
        var lifetime = TimeSpan.FromHours(hours);
        Assert.InRange(signedExpiry - DateTimeOffset.UtcNow, lifetime - TimeSpan.FromMinutes(1), lifetime);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PasswordChange_EndsExistingSessions()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClient();
        var (auth, _) = await LoginAsync(client);

        using (var before = AuthedRequest(HttpMethod.Get, "/_/api/users/me", auth))
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(before, ct)).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortwayApi.Auth.AuthDbContext>();
            var admin = await db.AdminUsers.FirstAsync(u => u.Username == "admin", ct);
            admin.PasswordHash = PortwayApi.Auth.AdminUserService.HashPassword("An0ther-console-pw-1");
            await db.SaveChangesAsync(ct);
        }

        using var after = AuthedRequest(HttpMethod.Get, "/_/api/users/me", auth);
        Assert.Equal(HttpStatusCode.Found, (await client.SendAsync(after, ct)).StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedUiApiRequest_RedirectsToLogin()
    {
        var client = CreateClient();
        var resp = await client.GetAsync("/_/api/settings", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Contains("/_/login", resp.Headers.Location!.ToString());
    }

    [Fact]
    public async Task DisabledConsole_DeniesUiApiInsteadOfLeavingItUnguarded()
    {
        // Middleware must deny ui api routes itself now that it always runs
        using var offFactory = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Console:AdminApiKey"] = "", ["Console:Enabled"] = "false" })));
        var client = offFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var resp = await client.GetAsync("/_/api/environments/500", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task ExplicitWebUiEnabled_KeepsUiReachableAfterAdminKeyIsCleared()
    {
        // The settings page clears WebUi:AdminApiKey once real accounts exist; WebUi:Enabled must keep the console open
        using var onFactory = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Console:AdminApiKey"] = "", ["Console:Enabled"] = "true" })));
        var client = onFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var resp = await client.GetAsync("/_/api/environments/500", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Contains("/_/login", resp.Headers.Location!.ToString());
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
                    ["Console:AdminApiKey"] = AdminKey,
                    ["Console:PublicOrigins:0"] = "http://localhost"
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
                resp = await client.GetAsync("/_/api/settings", TestContext.Current.CancellationToken);
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

        var req = AuthedRequest(HttpMethod.Put, "/_/api/environments/settings", authCookie, csrfHeader: null, body: new { });
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

        var put = AuthedRequest(HttpMethod.Put, "/_/api/environments/settings", authCookie, csrfCookie,
            new { server_name = "localhost", allowed_environments = new[] { "500", "700" } });
        var resp = await client.SendAsync(put, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var auditReq = AuthedRequest(HttpMethod.Get, "/_/api/audit", authCookie);
        var auditResp = await client.SendAsync(auditReq, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, auditResp.StatusCode);
        var audit = await auditResp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var entries = audit.GetProperty("entries").EnumerateArray().ToList();
        Assert.Contains(entries, e =>
            e.GetProperty("action").GetString() == "update" &&
            e.GetProperty("target_type").GetString() == "environment-settings");
    }

    [Fact]
    public async Task ParallelEnvironmentCreates_AllKept()
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);
        var globalPath = Path.Combine(Directory.GetCurrentDirectory(), "environments", "settings.json");
        var before = File.ReadAllText(globalPath);
        var names = Enumerable.Range(0, 8).Select(i => $"race{i}{Guid.NewGuid():N}"[..12]).ToList();
        try
        {
            var responses = await Task.WhenAll(names.Select(name => client.SendAsync(
                AuthedRequest(HttpMethod.Post, "/_/api/environments", authCookie, csrfCookie, new { name }),
                TestContext.Current.CancellationToken)));
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

            var allowed = _factory.Services.GetRequiredService<PortwayApi.Classes.EnvironmentSettings>().AllowedEnvironments;
            Assert.All(names, name => Assert.Contains(name, allowed));
        }
        finally
        {
            File.WriteAllText(globalPath, before);
            _factory.Services.GetRequiredService<PortwayApi.Classes.EnvironmentSettings>().Reload();
            foreach (var name in names)
            {
                var dir = Path.Combine(Directory.GetCurrentDirectory(), "environments", name);
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("sql", """{"DatabaseObjectName":"Items","DatabaseSchema":"dbo"}""", true)]
    [InlineData("sql", """{"DatabaseSchema":"dbo"}""", false)]
    [InlineData("proxy", """{"Url":"http://localhost:8020/svc","Methods":["GET"]}""", true)]
    [InlineData("proxy", """{"Methods":["GET"]}""", false)]
    [InlineData("static", """{"ContentType":"text/csv","Namespace":"1bad"}""", false)]
    [InlineData("static", """{ "ContentType": "text/plain", "ContentFile": "a.txt", "Stability": "Experimental", "Badges": [{ "Name": "New", "Position": "before", "Color": "#fff" }] }""", true)]
    [InlineData("static", """{ "ContentType": "text/plain", "ContentFile": "a.txt", "Stability": "Deprecated" }""", false)]
    [InlineData("static", """{ "ContentType": "text/plain", "ContentFile": "a.txt", "Badges": [{ "Name": "New", "Color": "red;x:y" }] }""", false)]
    public async Task ValidateEndpoint_ChecksTypeRules(string type, string content, bool expectValid)
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        var req = AuthedRequest(HttpMethod.Post, $"/_/api/endpoints/{type}/validate", authCookie, csrfCookie,
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

        var req = AuthedRequest(HttpMethod.Post, "/_/api/endpoints/sql/validate", authCookie, csrfCookie,
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

        var req = AuthedRequest(HttpMethod.Get, "/_/settings", authCookie);
        var resp = await client.SendAsync(req, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var html = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("<title>Settings · Portway</title>", html);
        Assert.Contains("toastContainer", html);          // shell
        Assert.Contains("id=\"securityBody\"", html);     // view fragment
        Assert.EndsWith("</html>", html.TrimEnd());       // footer
    }

    [Fact]
    public async Task TracePage_IsComposedForASignedInAccountOnly()
    {
        var client = CreateClient();

        var anonymous = await client.GetAsync("/_/trace", TestContext.Current.CancellationToken);
        Assert.NotEqual(HttpStatusCode.OK, anonymous.StatusCode);

        var (authCookie, _) = await LoginAsync(client);
        var resp = await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/trace", authCookie), TestContext.Current.CancellationToken);
        var html = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("<title>Trace · Portway</title>", html);
        Assert.Contains("id=\"captureBody\"", html);
    }

    [Fact]
    public async Task McpEndpoint_RequiresABearerToken()
    {
        var client = CreateClient();

        var resp = await client.PostAsync("/mcp", new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", System.Text.Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task SettingsWrite_RefusesTheRemovedMcpAuthenticationSwitch()
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        var req = AuthedRequest(HttpMethod.Put, "/_/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["Mcp:RequireAuthentication"] = false });

        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(req, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task SettingsWrite_RefusesAllowedHostsThatExcludeTheCaller()
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        var req = AuthedRequest(HttpMethod.Put, "/_/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["AllowedHosts"] = new[] { "portway.example.com" } });
        var resp = await client.SendAsync(req, TestContext.Current.CancellationToken);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("AllowedHosts", body.GetProperty("field").GetString());
    }

    [Fact]
    public async Task SettingsRead_NeverReturnsSecretValues()
    {
        var client = CreateClient();
        var (authCookie, _) = await LoginAsync(client);

        var resp = await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/api/settings", authCookie), TestContext.Current.CancellationToken);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        foreach (var w in body.GetProperty("writable").EnumerateArray().Where(w => w.GetProperty("kind").GetString() is "secret" or "clear"))
            Assert.True(w.GetProperty("value").ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null, w.GetProperty("key").GetString());
    }

    [Fact]
    public async Task SettingsPage_SecuritySectionContainsOnlySecurityCards()
    {
        var client = CreateClient();
        var (authCookie, _) = await LoginAsync(client);

        var resp = await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/settings", authCookie), TestContext.Current.CancellationToken);
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
    public async Task TokensPage_RotateIsInEditSheetNotTableRow()
    {
        var client = CreateClient();
        var (authCookie, _) = await LoginAsync(client);

        var resp = await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/tokens", authCookie), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        var start = html.IndexOf("id=\"editDrawer\"", StringComparison.Ordinal);
        var end = html.IndexOf("id=\"editFooter\"", start, StringComparison.Ordinal);
        Assert.Contains("id=\"rotateRow\"", html[start..end]);
        Assert.Contains("onclick=\"confirmRotate()\"", html[start..end]);
        Assert.DoesNotContain("data-tooltip=\"Rotate token\"", html);
    }

    [Fact]
    public async Task UsersPage_ColumnsAreSortable()
    {
        var client = CreateClient();
        var (authCookie, _) = await LoginAsync(client);

        var resp = await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/users", authCookie), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        foreach (var col in new[] { "username", "role", "provider", "status", "last_login_at" })
        {
            Assert.Contains($"class=\"sortable\" data-col=\"{col}\" tabindex=\"0\" aria-sort=\"none\"", html);
            Assert.Contains($"  {col}: u => ", html);
        }
    }

    [Fact]
    public async Task RotateToken_IssuesReplacementAndArchivesOriginal()
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);
        var ct = TestContext.Current.CancellationToken;

        var create = AuthedRequest(HttpMethod.Post, "/_/api/tokens", authCookie, csrfCookie,
            new { username = "rotate-me", allowed_scopes = "CRM/Accounts", allowed_environments = "dev" });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(create, ct)).StatusCode);

        var list = await (await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/api/tokens", authCookie), ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = list.EnumerateArray().Single(t => t.GetProperty("username").GetString() == "rotate-me").GetProperty("id").GetInt32();

        var rotate = await client.SendAsync(AuthedRequest(HttpMethod.Post, $"/_/api/tokens/{id}/rotate", authCookie, csrfCookie), ct);
        Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);
        var rotated = await rotate.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.False(string.IsNullOrEmpty(rotated.GetProperty("token").GetString()));
        Assert.Equal("CRM/Accounts", rotated.GetProperty("allowed_scopes").GetString());

        var after = await (await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/api/tokens", authCookie), ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);
        var active = after.EnumerateArray().Where(t => t.GetProperty("username").GetString() == "rotate-me").ToList();
        Assert.Single(active);
        Assert.NotEqual(id, active[0].GetProperty("id").GetInt32());

        var again = await client.SendAsync(AuthedRequest(HttpMethod.Post, $"/_/api/tokens/{id}/rotate", authCookie, csrfCookie), ct);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task SettingsEndpoint_ReportsSecurityPosture()
    {
        var client = CreateClient();
        var (authCookie, _) = await LoginAsync(client);

        var req = AuthedRequest(HttpMethod.Get, "/_/api/settings", authCookie);
        var resp = await client.SendAsync(req, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var security = json.GetProperty("security");
        Assert.True(security.GetProperty("webui_auth_enabled").GetBoolean());
        Assert.True(security.GetProperty("admin_accounts").GetInt32() > 0);
        Assert.True(security.GetProperty("csrf_protection").GetBoolean());
    }

    [Theory]
    [InlineData("/_/api/auth", """{"username":1,"password":"x"}""", "application/json")]
    [InlineData("/_/api/auth", """{"username":"admin","password":{}}""", "application/json")]
    [InlineData("/_/api/auth", """{"username":"admin","csrf":[]}""", "application/json")]
    [InlineData("/_/api/auth", """{"username":true,"password":"x"}""", "application/json")]
    [InlineData("/_/api/auth", """{"username":"admin","password":"x","remember":"yes"}""", "application/json")]
    [InlineData("/_/api/auth", "[]", "application/json")]
    [InlineData("/_/api/auth", "not json", "application/json")]
    [InlineData("/_/api/auth/password", """{"username":1}""", "application/json")]
    [InlineData("/_/api/auth/password", """{"username":"admin","newPassword":5}""", "application/json")]
    [InlineData("/_/api/auth/password", "[]", "application/json")]
    [InlineData("/_/api/auth/password", "not json", "application/json")]
    public async Task MalformedSignInBody_Returns400(string path, string body, string contentType)
    {
        var client = CreateClient();

        var response = await client.PostAsync(path, new StringContent(body, System.Text.Encoding.UTF8, contentType), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SignInBodyOfAnotherMediaType_Returns415()
    {
        var response = await CreateClient().PostAsync("/_/api/auth", new StringContent("""{"username":"admin"}""", System.Text.Encoding.UTF8, "text/plain"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Theory]
    [InlineData("/_/api/auth")]
    [InlineData("/_/api/auth/password")]
    public async Task EmptySignInRequest_Returns400(string path)
    {
        var response = await CreateClient().PostAsync(path, null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MalformedSignInBody_DoesNotCountTowardTheLockout()
    {
        var client = CreateClient();

        for (var i = 0; i < 60; i++)
        {
            var response = await client.PostAsync("/_/api/auth", new StringContent("""{"username":1}""", System.Text.Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task Viewer_EnvironmentRaw_Forbidden_AndEnvironmentGet_OmitsValues()
    {
        var name = $"viewerenv{Guid.NewGuid():N}";
        var folder = Path.Combine("environments", name);
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "settings.json"),
                """{"ServerName":"srv","ConnectionString":"Server=x;Password=conn-secret","Headers":{"X-Api-Key":"header-secret"}}""");

            var client = CreateClient();
            var (adminCookie, adminCsrf) = await LoginAsync(client);
            var create = AuthedRequest(HttpMethod.Post, "/_/api/users", adminCookie, adminCsrf,
                new { username = "env-viewer", password = "V13wer-env-pw-99", role = "viewer", current_password = SeededPassword });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(create, TestContext.Current.CancellationToken)).StatusCode);
            var (viewerCookie, _) = await SignInAsync(client, "env-viewer", "V13wer-env-pw-99");

            var raw = await client.SendAsync(AuthedRequest(HttpMethod.Get, $"/_/api/environments/{name}/raw", viewerCookie), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, raw.StatusCode);

            var read = await client.SendAsync(AuthedRequest(HttpMethod.Get, $"/_/api/environments/{name}", viewerCookie), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var body = await read.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.DoesNotContain("conn-secret", body);
            Assert.DoesNotContain("header-secret", body);
            Assert.Contains("X-Api-Key", body);

            Assert.DoesNotContain("PWENC:", body);

            var adminRaw = await client.SendAsync(AuthedRequest(HttpMethod.Get, $"/_/api/environments/{name}/raw", adminCookie), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, adminRaw.StatusCode);
            Assert.Contains("ConnectionString", await adminRaw.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private async Task<(HttpClient Client, string Name, string Folder, string AdminCookie, string AdminCsrf)> SecretEnvironmentAsync(string settings)
    {
        var name = $"secretenv{Guid.NewGuid():N}";
        var folder = Path.Combine("environments", name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "settings.json"), settings);
        var client = CreateClient();
        var (cookie, csrf) = await LoginAsync(client);
        return (client, name, folder, cookie, csrf);
    }

    private const string PlainSecrets = """{"Encrypt":false,"ServerName":"srv","Headers":{},"Secrets":{"ERP-KEY":"plain-secret-value"},"FutureKey":{"x":1}}""";

    [Fact]
    public async Task Secrets_ValuesNeverReturned()
    {
        var (client, name, folder, adminCookie, adminCsrf) = await SecretEnvironmentAsync(PlainSecrets);
        try
        {
            var create = AuthedRequest(HttpMethod.Post, "/_/api/users", adminCookie, adminCsrf,
                new { username = "secret-viewer", password = "V13wer-secret-pw-99", role = "viewer", current_password = SeededPassword });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(create, TestContext.Current.CancellationToken)).StatusCode);
            var (viewerCookie, _) = await SignInAsync(client, "secret-viewer", "V13wer-secret-pw-99");

            foreach (var cookie in new[] { viewerCookie, adminCookie })
            {
                foreach (var url in new[] { "/_/api/environments", $"/_/api/environments/{name}" })
                {
                    var response = await client.SendAsync(AuthedRequest(HttpMethod.Get, url, cookie), TestContext.Current.CancellationToken);
                    var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                    Assert.DoesNotContain("plain-secret-value", body);
                }
            }

            var named = await (await client.SendAsync(AuthedRequest(HttpMethod.Get, $"/_/api/environments/{name}", viewerCookie), TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("ERP-KEY", named);

            var raw = await client.SendAsync(AuthedRequest(HttpMethod.Get, $"/_/api/environments/{name}/raw", viewerCookie), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, raw.StatusCode);
            Assert.DoesNotContain("plain-secret-value", await raw.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Secrets_ViewerCannotWrite()
    {
        var (client, name, folder, adminCookie, adminCsrf) = await SecretEnvironmentAsync(PlainSecrets);
        try
        {
            var create = AuthedRequest(HttpMethod.Post, "/_/api/users", adminCookie, adminCsrf,
                new { username = "secret-writer", password = "V13wer-writer-pw-99", role = "viewer", current_password = SeededPassword });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(create, TestContext.Current.CancellationToken)).StatusCode);
            var (viewerCookie, viewerCsrf) = await SignInAsync(client, "secret-writer", "V13wer-writer-pw-99");

            var put = await client.SendAsync(AuthedRequest(HttpMethod.Put, $"/_/api/environments/{name}/secrets/NEW-KEY", viewerCookie, viewerCsrf, new { value = "v" }), TestContext.Current.CancellationToken);
            var delete = await client.SendAsync(AuthedRequest(HttpMethod.Delete, $"/_/api/environments/{name}/secrets/ERP-KEY", viewerCookie, viewerCsrf), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
            Assert.Contains("plain-secret-value", File.ReadAllText(Path.Combine(folder, "settings.json")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Secrets_EmptyValue_Refused(string value)
    {
        var (client, name, folder, adminCookie, adminCsrf) = await SecretEnvironmentAsync(PlainSecrets);
        try
        {
            var put = await client.SendAsync(AuthedRequest(HttpMethod.Put, $"/_/api/environments/{name}/secrets/NEW-KEY", adminCookie, adminCsrf, new { value }), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("bad name")]
    [InlineData("under_score")]
    public async Task Secrets_InvalidName_Refused(string secret)
    {
        var (client, name, folder, adminCookie, adminCsrf) = await SecretEnvironmentAsync(PlainSecrets);
        try
        {
            var put = await client.SendAsync(AuthedRequest(HttpMethod.Put, $"/_/api/environments/{name}/secrets/{Uri.EscapeDataString(secret)}", adminCookie, adminCsrf, new { value = "v" }), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Secrets_PutReplacesCaseInsensitivelyAndDeleteRemoves()
    {
        var (client, name, folder, adminCookie, adminCsrf) = await SecretEnvironmentAsync("""{"ServerName":"srv","Headers":{},"Secrets":{"ERP-KEY":"old-value-000"}}""");
        try
        {
            var put = await client.SendAsync(AuthedRequest(HttpMethod.Put, $"/_/api/environments/{name}/secrets/erp-key", adminCookie, adminCsrf, new { value = "new-secret-value" }), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            Assert.DoesNotContain("new-secret-value", await put.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

            var saved = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "settings.json")))!["Secrets"]!.AsObject();
            var only = Assert.Single(saved);
            Assert.Equal("erp-key", only.Key);
            Assert.StartsWith("PWENC:", only.Value!.GetValue<string>());

            var delete = await client.SendAsync(AuthedRequest(HttpMethod.Delete, $"/_/api/environments/{name}/secrets/ERP-KEY", adminCookie, adminCsrf), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
            Assert.Null(System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "settings.json")))!["Secrets"]);

            var again = await client.SendAsync(AuthedRequest(HttpMethod.Delete, $"/_/api/environments/{name}/secrets/ERP-KEY", adminCookie, adminCsrf), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task FormSave_KeepsSecrets()
    {
        var (client, name, folder, adminCookie, adminCsrf) = await SecretEnvironmentAsync(PlainSecrets);
        try
        {
            var save = await client.SendAsync(AuthedRequest(HttpMethod.Put, $"/_/api/environments/{name}", adminCookie, adminCsrf,
                new { server_name = "srv2", headers = new Dictionary<string, string> { ["X-A"] = "1" } }), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, save.StatusCode);
            var saved = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "settings.json")))!;
            Assert.Equal("plain-secret-value", saved["Secrets"]!["ERP-KEY"]!.GetValue<string>());
            Assert.Equal(1, saved["FutureKey"]!["x"]!.GetValue<int>());
            Assert.Equal("srv2", saved["ServerName"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task EnvironmentSave_EndsWithNewline()
    {
        var (client, name, folder, adminCookie, adminCsrf) = await SecretEnvironmentAsync(PlainSecrets + "\n");
        try
        {
            var put = await client.SendAsync(AuthedRequest(HttpMethod.Put, $"/_/api/environments/{name}/secrets/NEW-KEY", adminCookie, adminCsrf, new { value = "v" }), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            Assert.EndsWith("}\n", File.ReadAllText(Path.Combine(folder, "settings.json")));

            var raw = await client.SendAsync(AuthedRequest(HttpMethod.Put, $"/_/api/environments/{name}/raw", adminCookie, adminCsrf,
                new { content = """{"ServerName":"srv"}""" }), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, raw.StatusCode);
            Assert.EndsWith("}\n", File.ReadAllText(Path.Combine(folder, "settings.json")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task RawSave_EncryptsSecrets()
    {
        var (client, name, folder, adminCookie, adminCsrf) = await SecretEnvironmentAsync("""{"ServerName":"srv"}""");
        try
        {
            var save = await client.SendAsync(AuthedRequest(HttpMethod.Put, $"/_/api/environments/{name}/raw", adminCookie, adminCsrf,
                new { content = """{"ServerName":"srv","Secrets":{"ERP-KEY":"raw-secret-value"}}""" }), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, save.StatusCode);
            var file = File.ReadAllText(Path.Combine(folder, "settings.json"));
            Assert.DoesNotContain("raw-secret-value", file);
            Assert.StartsWith("PWENC:", System.Text.Json.Nodes.JsonNode.Parse(file)!["Secrets"]!["ERP-KEY"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task EndpointSave_WarnsOnMissingSecret()
    {
        var (_, name, folder, _, _) = await SecretEnvironmentAsync(PlainSecrets);
        try
        {
            var warnings = await PortwayApi.Endpoints.WebUiEndpointExtensions.MissingSecretWarningsAsync(_factory.Services,
                $$"""{"AllowedEnvironments":["{{name}}"],"Url":"https://api.example/x?a=${secret:ERP-KEY}&b=${secret:NOPE}"}""");

            var warning = Assert.Single(warnings);
            Assert.Contains("NOPE", warning);
            Assert.Contains(name, warning);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task SecretsOverview_ListsReferencesAndMissing()
    {
        var (_, name, folder, _, _) = await SecretEnvironmentAsync("""{"Encrypt":false,"ConnectionString":"Password=${secret:DB-PW}","Secrets":{"ERP-KEY":"plain-secret-value","IDLE-KEY":"idle-secret-value"}}""");
        var endpointDir = Path.Combine("endpoints", "Proxy", "SecretsOverview", "Items");
        Directory.CreateDirectory(endpointDir);
        File.WriteAllText(Path.Combine(endpointDir, "entity.json"), $$"""{"Url":"https://api.example/x?a=${secret:ERP-KEY}&b=${secret:NOPE}","AllowedEnvironments":["{{name}}"]}""");
        try
        {
            var rows = await PortwayApi.Endpoints.WebUiEndpointExtensions.SecretsOverviewAsync(_factory.Services, [name], TestContext.Current.CancellationToken);
            var json = System.Text.Json.JsonSerializer.Serialize(rows);

            Assert.DoesNotContain("plain-secret-value", json);
            Assert.DoesNotContain("idle-secret-value", json);
            var erp = rows.Single(r => r.Name == "ERP-KEY");
            Assert.True(erp.HasValue);
            Assert.Contains("Proxy/SecretsOverview/Items", erp.UsedBy);
            var nope = rows.Single(r => r.Name == "NOPE");
            Assert.False(nope.HasValue);
            Assert.Contains("environment settings", rows.Single(r => r.Name == "DB-PW").UsedBy);
            Assert.Empty(rows.Single(r => r.Name == "IDLE-KEY").UsedBy);
            Assert.All(rows, r => Assert.Equal(name, r.Environment));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(Path.Combine("endpoints", "Proxy", "SecretsOverview"), recursive: true);
        }
    }

    [Fact]
    public async Task SecretsOverview_OnlyAllowedEnvironments()
    {
        var (client, name, folder, adminCookie, _) = await SecretEnvironmentAsync(PlainSecrets);
        try
        {
            var response = await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/api/secrets", adminCookie), TestContext.Current.CancellationToken);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain(name, body);
            Assert.DoesNotContain("plain-secret-value", body);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private async Task<(HttpClient Client, string Cookie, string Csrf, WebApplicationFactory<Program> Factory, string DbPath, string Dir)> DeliveriesAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"portway_deliveries_{Guid.NewGuid():N}.db");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE WebhookData (Id INTEGER PRIMARY KEY, WebhookId TEXT, Payload TEXT, ReceivedAt TEXT);"
                + string.Concat(Enumerable.Range(1, 60).Select(i => $"INSERT INTO WebhookData VALUES ({i}, 'evt', '{{\"n\":{i},\"html\":\"<b>x</b>\"}}', '2026-11-11T10:00:00Z');"));
            command.ExecuteNonQuery();
        }

        var dir = Path.Combine("endpoints", "Webhooks", "ConsoleView");
        Directory.CreateDirectory(Path.Combine(dir, "Hooks"));
        File.WriteAllText(Path.Combine(dir, "Hooks", "entity.json"), """{"DatabaseObjectName":"WebhookData","AllowedColumns":["evt"],"AllowedEnvironments":["HOOKENV"]}""");
        EndpointHandler.ReloadAllEndpoints();

        var settings = new PortwayApi.Tests.Base.TestEnvironmentSettings();
        settings.SetAllowedEnvironments(["HOOKENV"]);
        var provider = new Moq.Mock<PortwayApi.Interfaces.IEnvironmentSettingsProvider>();
        provider.Setup(p => p.LoadEnvironmentOrThrowAsync("HOOKENV")).ReturnsAsync(($"Data Source={dbPath}", "localhost", new Dictionary<string, string>()));
        var factory = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.AddSingleton<EnvironmentSettings>(settings);
            services.AddSingleton(provider.Object);
        }));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (cookie, csrf) = await LoginAsync(client);
        return (client, cookie, csrf, factory, dbPath, dir);
    }

    [Fact]
    public async Task Deliveries_ReturnsLatestFiftyNewestFirst()
    {
        var (client, cookie, _, factory, dbPath, dir) = await DeliveriesAsync();
        try
        {
            var response = await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/api/webhooks/ConsoleView/Hooks/deliveries?env=HOOKENV", cookie), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var deliveries = json.RootElement.GetProperty("deliveries");
            Assert.Equal(50, deliveries.GetArrayLength());
            Assert.Equal(60, deliveries[0].GetProperty("id").GetInt64());
            Assert.Contains("<b>x</b>", deliveries[0].GetProperty("payload").GetString());
        }
        finally
        {
            factory.Dispose();
            Directory.Delete(dir, recursive: true);
            EndpointHandler.ReloadAllEndpoints();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task Deliveries_ViewerDenied()
    {
        var (client, cookie, csrf, factory, dbPath, dir) = await DeliveriesAsync();
        try
        {
            var create = AuthedRequest(HttpMethod.Post, "/_/api/users", cookie, csrf,
                new { username = "hook-viewer", password = "V13wer-hook-pw-99", role = "viewer", current_password = SeededPassword });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(create, TestContext.Current.CancellationToken)).StatusCode);
            var (viewerCookie, _) = await SignInAsync(client, "hook-viewer", "V13wer-hook-pw-99");

            var response = await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/api/webhooks/ConsoleView/Hooks/deliveries?env=HOOKENV", viewerCookie), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.DoesNotContain("<b>x</b>", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            factory.Dispose();
            Directory.Delete(dir, recursive: true);
            EndpointHandler.ReloadAllEndpoints();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(dbPath);
        }
    }

    [Theory]
    [InlineData("/_/api/webhooks/ConsoleView/Hooks/deliveries?env=OTHER", HttpStatusCode.BadRequest)]
    [InlineData("/_/api/webhooks/ConsoleView/Hooks/deliveries", HttpStatusCode.BadRequest)]
    [InlineData("/_/api/webhooks/Nope/deliveries?env=HOOKENV", HttpStatusCode.NotFound)]
    public async Task Deliveries_RefusesUnknownEndpointOrEnvironment(string url, HttpStatusCode expected)
    {
        var (client, cookie, _, factory, dbPath, dir) = await DeliveriesAsync();
        try
        {
            Assert.Equal(expected, (await client.SendAsync(AuthedRequest(HttpMethod.Get, url, cookie), TestContext.Current.CancellationToken)).StatusCode);
        }
        finally
        {
            factory.Dispose();
            Directory.Delete(dir, recursive: true);
            EndpointHandler.ReloadAllEndpoints();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void RedactEndpointUrls_MasksUrlAndFallbackQueries()
    {
        var redacted = PortwayApi.Endpoints.WebUiEndpointExtensions.RedactEndpointUrls(
            """{"Url":"http://x/a?key=url-secret","FallbackUrls":["http://y/b?key=fallback-secret"],"Methods":["GET"]}""");

        Assert.DoesNotContain("url-secret", redacted);
        Assert.DoesNotContain("fallback-secret", redacted);
        Assert.Contains("Methods", redacted);
    }

    [Fact]
    public void RedactEndpointUrls_MasksLiteralHeaderValues()
    {
        var redacted = PortwayApi.Endpoints.WebUiEndpointExtensions.RedactEndpointUrls(
            """{"Url":"http://x/a","Headers":{"Authorization":"Bearer literal-header-secret","X-Api-Key":"${secret:ERP-KEY}"}}""");

        Assert.DoesNotContain("literal-header-secret", redacted);
        Assert.Contains("Authorization", redacted);
        Assert.Contains("${secret:ERP-KEY}", redacted);
    }

    [Fact]
    public async Task Viewer_EndpointGet_OmitsLiteralHeaderValues()
    {
        var dir = Path.Combine("endpoints", "Proxy", "ViewerHeaders", "Upstream");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "entity.json"),
            """{"Url":"http://localhost:9/x","Methods":["GET"],"Headers":{"Authorization":"Bearer literal-header-secret"}}""");
        try
        {
            var client = CreateClient();
            var (adminCookie, adminCsrf) = await LoginAsync(client);
            var create = AuthedRequest(HttpMethod.Post, "/_/api/users", adminCookie, adminCsrf,
                new { username = "header-viewer", password = "V13wer-header-pw-99", role = "viewer", current_password = SeededPassword });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(create, TestContext.Current.CancellationToken)).StatusCode);
            var (viewerCookie, _) = await SignInAsync(client, "header-viewer", "V13wer-header-pw-99");

            foreach (var url in new[] { "/_/api/endpoints/proxy/ViewerHeaders/Upstream", "/_/api/endpoints/proxy/ViewerHeaders/Upstream?raw=true" })
            {
                var response = await client.SendAsync(AuthedRequest(HttpMethod.Get, url, viewerCookie), TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.DoesNotContain("literal-header-secret", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }

            var admin = await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/api/endpoints/proxy/ViewerHeaders/Upstream", adminCookie), TestContext.Current.CancellationToken);
            Assert.Contains("literal-header-secret", await admin.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(Path.Combine("endpoints", "Proxy", "ViewerHeaders"), recursive: true);
        }
    }

    [Fact]
    public async Task ViewerAccount_CannotWriteSettings_ButCanRead()
    {
        var client = CreateClient();
        var (adminCookie, adminCsrf) = await LoginAsync(client);

        var create = AuthedRequest(HttpMethod.Post, "/_/api/users", adminCookie, adminCsrf,
            new { username = "read-only", password = "V13wer-account-pw-77", role = "viewer", current_password = SeededPassword });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(create, TestContext.Current.CancellationToken)).StatusCode);

        var (viewerCookie, viewerCsrf) = await SignInAsync(client, "read-only", "V13wer-account-pw-77");

        // Reading the console stays open to a viewer
        var read = await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/api/settings", viewerCookie), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var viewerBody = await read.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("svc-proxy-account", viewerBody);
        Assert.DoesNotContain("otlp-secret", viewerBody);

        var adminRead = await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/api/settings", adminCookie), TestContext.Current.CancellationToken);
        var adminBody = await adminRead.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("svc-proxy-account", adminBody);
        Assert.Contains("otlp-secret", adminBody);

        // Writing settings is administrator-only, CSRF satisfied or not
        var write = AuthedRequest(HttpMethod.Put, "/_/api/settings", viewerCookie, viewerCsrf,
            new Dictionary<string, object> { ["Caching:Enabled"] = false });
        var writeResp = await client.SendAsync(write, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, writeResp.StatusCode);

        // And it must not be able to hand itself an administrator account
        var escalate = AuthedRequest(HttpMethod.Post, "/_/api/users", viewerCookie, viewerCsrf,
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

        var create = AuthedRequest(HttpMethod.Post, "/_/api/users", adminCookie, adminCsrf,
            new { username = "metrics-viewer", password = "V13wer-metrics-pw-88", role = "viewer", current_password = SeededPassword });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(create, TestContext.Current.CancellationToken)).StatusCode);
        var (viewerCookie, viewerCsrf) = await SignInAsync(client, "metrics-viewer", "V13wer-metrics-pw-88");

        var viewerRead = await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/api/metrics/health?period=1h", viewerCookie), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, viewerRead.StatusCode);
        var health = await viewerRead.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.True(health.GetProperty("summary").GetProperty("failures").GetInt64() >= 1);

        var viewerClear = await client.SendAsync(AuthedRequest(HttpMethod.Delete, "/_/api/metrics", viewerCookie, viewerCsrf), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, viewerClear.StatusCode);

        var adminClear = await client.SendAsync(AuthedRequest(HttpMethod.Delete, "/_/api/metrics", adminCookie, adminCsrf), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, adminClear.StatusCode);
        Assert.Equal(0, metrics.GetHealth(TimeSpan.FromDays(30), new PortwayApi.Services.HealthFilter()).Summary.Total);
    }

    [Fact]
    public async Task DeactivatedAccount_LosesAccessImmediately_EvenOnAPlainReadRequest()
    {
        // One client per account, the factory client tracks Set-Cookie
        var adminClient = CreateClient();
        var (adminCookie, adminCsrf) = await LoginAsync(adminClient);

        var create = AuthedRequest(HttpMethod.Post, "/_/api/users", adminCookie, adminCsrf,
            new { username = "soon-deactivated", password = "D3act1vate-me-pw-42", role = "viewer", current_password = SeededPassword });
        var createResp = await adminClient.SendAsync(create, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var targetId = created.GetProperty("id").GetInt32();

        var targetClient = CreateClient();
        var (targetCookie, _) = await SignInAsync(targetClient, "soon-deactivated", "D3act1vate-me-pw-42");

        // The now-deactivated account's own cookie still authenticates a plain GET before the fix
        var deactivate = AuthedRequest(HttpMethod.Put, $"/_/api/users/{targetId}", adminCookie, adminCsrf,
            new Dictionary<string, object> { ["is_active"] = false, ["current_password"] = SeededPassword });
        Assert.Equal(HttpStatusCode.OK, (await adminClient.SendAsync(deactivate, TestContext.Current.CancellationToken)).StatusCode);

        var readAfterDeactivation = AuthedRequest(HttpMethod.Get, "/_/api/settings", targetCookie);
        var resp = await targetClient.SendAsync(readAfterDeactivation, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Contains("/_/login", resp.Headers.Location!.ToString());
    }

    [Fact]
    public async Task SettingsWrite_RejectsKeysOutsideTheWhitelistAndOverlongText()
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        var secret = AuthedRequest(HttpMethod.Put, "/_/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["Console:AdminApiKey"] = "stolen" });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(secret, TestContext.Current.CancellationToken)).StatusCode);

        var former = AuthedRequest(HttpMethod.Put, "/_/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["WebUi:Customization:PromoText"] = "former" });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(former, TestContext.Current.CancellationToken)).StatusCode);

        var tooLong = AuthedRequest(HttpMethod.Put, "/_/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["Console:Customization:PromoText"] = new string('x', 2_001) });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(tooLong, TestContext.Current.CancellationToken)).StatusCode);

        var ok = AuthedRequest(HttpMethod.Put, "/_/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["Console:Customization:PromoText"] = "Hello **there**" });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(ok, TestContext.Current.CancellationToken)).StatusCode);
        var written = JsonNode.Parse(await File.ReadAllTextAsync(_overridesPath, TestContext.Current.CancellationToken))!;
        Assert.Equal("Hello **there**", written["Console"]!["Customization"]!["PromoText"]!.GetValue<string>());
        Assert.Null(written["WebUi"]);
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
            ("OpenApi:Scalar:Theme", "evil"),
            ("OpenApi:ExternalDocs:Url", "javascript:alert(1)"),
            ("OpenApi:ExternalDocs:Url", "mailto:a@example.com"),
            ("OpenApi:Contact:Email", "not an address"),
            ("OpenApi:Title", new string('x', 201))
        })
        {
            var req = AuthedRequest(HttpMethod.Put, "/_/api/settings", authCookie, csrfCookie, new Dictionary<string, object> { [key] = value });
            Assert.True(HttpStatusCode.BadRequest == (await client.SendAsync(req, TestContext.Current.CancellationToken)).StatusCode, key);
        }

        var ok = AuthedRequest(HttpMethod.Put, "/_/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["OpenApi:ShowNamespaces"] = true, ["OpenApi:Footer:Url"] = "https://example.com", ["OpenApi:Scalar:Theme"] = "portway", ["OpenApi:ShowBadges"] = false, ["OpenApi:ServeMarkdown"] = true, ["OpenApi:ExternalDocs:Url"] = "", ["OpenApi:ExternalDocs:Description"] = "Guide" });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(ok, TestContext.Current.CancellationToken)).StatusCode);
    }

    // console version management: next version, guarded delete and rename, scope field in the list
    [Fact]
    public async Task Console_ManagesEndpointVersions()
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Static", "UiVer", "Items");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "content.json"), "[]", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(dir, "entity.json"), """{ "ContentType": "application/json", "ContentFile": "content.json", "Deprecated": true, "DeprecatedSince": "2026-01-01T00:00:00Z", "Stability": "Stable" }""", TestContext.Current.CancellationToken);
        try
        {
            var client = CreateClient();
            var (authCookie, csrfCookie) = await LoginAsync(client);
            async Task<HttpResponseMessage> Send(HttpMethod method, string url, object? body = null) =>
                await client.SendAsync(AuthedRequest(method, url, authCookie, csrfCookie, body), TestContext.Current.CancellationToken);

            var created = await Send(HttpMethod.Post, "/_/api/endpoints/static/versions/UiVer/Items");
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            var body = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.Equal("UiVer/Items/v2", body.GetProperty("name").GetString());
            Assert.True(File.Exists(Path.Combine(dir, "v2", "content.json")));
            var copied = await File.ReadAllTextAsync(Path.Combine(dir, "v2", "entity.json"), TestContext.Current.CancellationToken);
            Assert.DoesNotContain("Deprecated", copied);
            Assert.DoesNotContain("Stability", copied);

            var rejected = await Send(HttpMethod.Put, "/_/api/endpoints/static/UiVer/Items/v2",
                new { content = new { ContentType = "application/json", ContentFile = "content.json", Stability = "Deprecated" } });
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

            var saved = await Send(HttpMethod.Put, "/_/api/endpoints/static/UiVer/Items/v2",
                new { content = new { ContentType = "application/json", ContentFile = "content.json", Deprecated = true } });
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Contains("\"DeprecatedSince\"", await File.ReadAllTextAsync(Path.Combine(dir, "v2", "entity.json"), TestContext.Current.CancellationToken));

            var third = await (await Send(HttpMethod.Post, "/_/api/endpoints/static/versions/UiVer/Items/v2")).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.Equal("v3", third.GetProperty("version").GetString());

            var overview = await (await Send(HttpMethod.Get, "/_/api/overview")).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.True(int.Parse(overview.GetProperty("api_version").GetString()![1..]) >= 3);

            var list = await (await Send(HttpMethod.Get, "/_/api/endpoints")).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            var v2 = list.GetProperty("static").EnumerateArray().Single(e => e.GetProperty("name").GetString() == "UiVer/Items/v2");
            Assert.Equal("v2", v2.GetProperty("version").GetString());
            Assert.Equal("UiVer/Items@v2", v2.GetProperty("scope").GetString());

            Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Delete, "/_/api/endpoints/static/UiVer/Items")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(new HttpMethod("PATCH"), "/_/api/endpoints/static/UiVer/Items/v2", new { new_name = "UiVer/Other" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Delete, "/_/api/endpoints/static/UiVer/Items/v3")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Delete, "/_/api/endpoints/static/UiVer/Items/v2")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Delete, "/_/api/endpoints/static/UiVer/Items")).StatusCode);
        }
        finally
        {
            var root = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Static", "UiVer");
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            PortwayApi.Classes.EndpointHandler.ReloadAllEndpoints();
        }
    }

    [Fact]
    public async Task StampDeprecatedSince_KeepsExistingDeprecation()
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Static", "UiStamp", "Items");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "content.json"), "[]", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(dir, "entity.json"), "{ \n    \"ContentType\": \"application/json\",\n    \"ContentFile\": \"content.json\",\n    \"Deprecated\": true\n}\n", TestContext.Current.CancellationToken);
        try
        {
            var client = CreateClient();
            var (authCookie, csrfCookie) = await LoginAsync(client);
            var saved = await client.SendAsync(AuthedRequest(HttpMethod.Put, "/_/api/endpoints/static/UiStamp/Items", authCookie, csrfCookie,
                new { content = new { ContentType = "application/json", ContentFile = "content.json", Deprecated = true } }), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var file = await File.ReadAllTextAsync(Path.Combine(dir, "entity.json"), TestContext.Current.CancellationToken);
            Assert.DoesNotContain("DeprecatedSince", file);
            Assert.Matches("\n {4}\"contentType\"", file);
            Assert.EndsWith("}\n", file);
        }
        finally
        {
            Directory.Delete(Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Static", "UiStamp"), recursive: true);
            PortwayApi.Classes.EndpointHandler.ReloadAllEndpoints();
        }
    }

    [Fact]
    public async Task EndpointSave_KeepsInlineArrays()
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Static", "UiInline", "Items");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "content.json"), "[]", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(dir, "entity.json"), "{\n    \"ContentType\": \"application/json\",\n    \"ContentFile\": \"content.json\",\n    \"AllowedEnvironments\": [\"500\",\"700\"],\n    \"Notes\": [\"a\", \"b\"]\n}\n", TestContext.Current.CancellationToken);
        try
        {
            var client = CreateClient();
            var (authCookie, csrfCookie) = await LoginAsync(client);
            var content = """{"ContentType":"application/json","ContentFile":"content.json","AllowedEnvironments":["500","700"],"Notes":["a","c"]}""";
            var saved = await client.SendAsync(AuthedRequest(HttpMethod.Put, "/_/api/endpoints/static/UiInline/Items?raw=true", authCookie, csrfCookie,
                new { content }), TestContext.Current.CancellationToken);

            Assert.True(saved.StatusCode == HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var file = await File.ReadAllTextAsync(Path.Combine(dir, "entity.json"), TestContext.Current.CancellationToken);
            Assert.Contains("\n    \"AllowedEnvironments\": [\"500\",\"700\"],\n", file);
            Assert.Contains("\n    \"Notes\": [\n        \"a\",\n        \"c\"\n    ]", file);
            Assert.Equal(System.Text.Json.Nodes.JsonNode.Parse(content)!.ToJsonString(), System.Text.Json.Nodes.JsonNode.Parse(file)!.ToJsonString());
        }
        finally
        {
            Directory.Delete(Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Static", "UiInline"), recursive: true);
            PortwayApi.Classes.EndpointHandler.ReloadAllEndpoints();
        }
    }

    // token scopes are validated on create and on update
    [Fact]
    public async Task TokenScopes_AreValidated()
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        var bad = await client.SendAsync(AuthedRequest(HttpMethod.Post, "/_/api/tokens", authCookie, csrfCookie,
            new { username = "scope-bad", allowed_scopes = "CRM Accounts" }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var good = await client.SendAsync(AuthedRequest(HttpMethod.Post, "/_/api/tokens", authCookie, csrfCookie,
            new { username = "scope-good", allowed_scopes = "CRM/Accounts@v2" }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);

        var tokens = await (await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/api/tokens", authCookie), TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var id = tokens.EnumerateArray()
            .First(x => x.GetProperty("username").GetString() == "scope-good").GetProperty("id").GetInt32();

        var update = await client.SendAsync(AuthedRequest(HttpMethod.Put, $"/_/api/tokens/{id}", authCookie, csrfCookie,
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
        var listed = await on.GetFromJsonAsync<JsonElement>("/_/api/auth/providers", TestContext.Current.CancellationToken);
        Assert.Contains(listed.GetProperty("providers").EnumerateArray(),
            p => p.GetProperty("slug").GetString() == "acme");

        using var offFactory = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Oidc:Enabled"] = "false" })));
        var client = offFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var providers = await client.GetFromJsonAsync<JsonElement>("/_/api/auth/providers", TestContext.Current.CancellationToken);
        Assert.Empty(providers.GetProperty("providers").EnumerateArray());

        var stillEnabled = await client.GetAsync("/_/api/auth/oidc/acme/start", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, stillEnabled.StatusCode);

        // Disabled OIDC answers the start route with the unknown slug 404
        var unknown = await client.GetAsync("/_/api/auth/oidc/anything/start", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Theory]
    // Refused: a network covering every address lets any client forge its own IP
    [InlineData("ForwardedHeaders:KnownNetworks", new[] { "0.0.0.0/0" }, false)]
    [InlineData("ForwardedHeaders:KnownNetworks", new[] { "::/0" }, false)]
    [InlineData("ForwardedHeaders:KnownNetworks", new[] { "172.18.0.0/16" }, true)]
    [InlineData("ForwardedHeaders:KnownProxies", new[] { "not-an-ip" }, false)]
    [InlineData("ForwardedHeaders:KnownProxies", new[] { "10.0.0.8", "10.0.0.9" }, true)]
    [InlineData("Console:PublicOrigins", new[] { "portway.example.com" }, false)]
    [InlineData("Console:PublicOrigins", new[] { "https://*" }, false)]
    public async Task DeploymentLists_AreValidatedEntryByEntry(string key, string[] values, bool expectOk)
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        var req = AuthedRequest(HttpMethod.Put, "/_/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { [key] = values });
        var resp = await client.SendAsync(req, TestContext.Current.CancellationToken);

        Assert.Equal(expectOk ? HttpStatusCode.OK : HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PublicOrigins_RefusesAChangeThatWouldLockTheCallerOut()
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        // This session reaches /_ via PublicOrigins only, so replacing it is refused
        var evict = AuthedRequest(HttpMethod.Put, "/_/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["Console:PublicOrigins"] = new[] { "https://elsewhere.example.com" } });
        var evictResp = await client.SendAsync(evict, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, evictResp.StatusCode);
        var problem = await evictResp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Contains("refuse your own requests", problem.GetProperty("error").GetString());

        // Keeping an entry that still covers the caller is allowed
        var keep = AuthedRequest(HttpMethod.Put, "/_/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["Console:PublicOrigins"] = new[] { "http://localhost", "https://elsewhere.example.com" } });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(keep, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task EnvLockedSetting_IsReportedAndRefused()
    {
        using var lockedFactory = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.AddSingleton(new PortwayApi.Services.Configuration.SettingsWriteService(["PORTWAY_MCP_ENABLED"]))));
        var client = lockedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (authCookie, csrfCookie) = await LoginAsync(client);

        var read = await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/api/settings", authCookie), TestContext.Current.CancellationToken);
        var writable = (await read.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("writable").EnumerateArray().ToDictionary(w => w.GetProperty("key").GetString()!);
        Assert.Equal("PORTWAY_MCP_ENABLED", writable["Mcp:Enabled"].GetProperty("env").GetString());
        Assert.Equal(JsonValueKind.Null, writable["Caching:Enabled"].GetProperty("env").ValueKind);

        var write = AuthedRequest(HttpMethod.Put, "/_/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["Mcp:Enabled"] = true });
        var resp = await client.SendAsync(write, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Mcp:Enabled", problem.GetProperty("field").GetString());
    }

    [Fact]
    public async Task TelemetrySettings_AreValidatedAndRetireTheLegacySwitch()
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        foreach (var (key, value) in new (string, object)[]
        {
            ("Telemetry:Provider", "Zipkin"),
            ("Telemetry:Otlp:Endpoint", "ftp://collector:4317"),
            ("Telemetry:ServiceName", "gateway one")
        })
        {
            var bad = AuthedRequest(HttpMethod.Put, "/_/api/settings", authCookie, csrfCookie, new Dictionary<string, object> { [key] = value });
            Assert.True(HttpStatusCode.BadRequest == (await client.SendAsync(bad, TestContext.Current.CancellationToken)).StatusCode, key);
        }

        var ok = AuthedRequest(HttpMethod.Put, "/_/api/settings", authCookie, csrfCookie, new Dictionary<string, object>
        {
            ["Telemetry:Provider"] = "None",
            ["Telemetry:Otlp:Endpoint"] = "http://otel-collector:4317",
            ["Telemetry:ServiceName"] = "portway-gw01"
        });
        var resp = await client.SendAsync(ok, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True((await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("restart_required").GetBoolean());

        // the legacy Enabled switch selects Otlp when no provider is set
        var written = JsonNode.Parse(await File.ReadAllTextAsync(_overridesPath, TestContext.Current.CancellationToken))!;
        Assert.False(written["Telemetry"]!["Enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task DefaultConfig_EnablesConsoleWithRandomAccountName()
    {
        var authDb = Path.Combine(Path.GetTempPath(), $"portway_default_{Guid.NewGuid():N}_auth.db");
        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Mcp:Enabled"] = "false",
                    ["Console:PublicOrigins:0"] = "http://localhost"
                }));
                b.ConfigureTestServices(services => services.AddDbContext<PortwayApi.Auth.AuthDbContext>(opts =>
                    opts.UseSqlite($"Data Source={authDb}"), ServiceLifetime.Scoped, ServiceLifetime.Scoped));
            });
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var resp = await client.GetAsync("/_/api/environments/500", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Found, resp.StatusCode);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PortwayApi.Auth.AuthDbContext>();
            var account = await db.AdminUsers.SingleAsync(TestContext.Current.CancellationToken);
            Assert.StartsWith("admin-", account.Username);
            Assert.True(account.MustChangePassword);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(authDb)) File.Delete(authDb);
        }
    }

    [Fact]
    public async Task LoginPage_ReportsPendingSetupUntilThePasswordIsChanged()
    {
        var client = CreateClient();

        var before = await client.GetFromJsonAsync<JsonElement>("/_/api/auth/csrf", TestContext.Current.CancellationToken);
        Assert.True(before.GetProperty("setup_pending").GetBoolean());

        await LoginAsync(client);

        var after = await client.GetFromJsonAsync<JsonElement>("/_/api/auth/csrf", TestContext.Current.CancellationToken);
        Assert.False(after.GetProperty("setup_pending").GetBoolean());
    }

    [Fact]
    public async Task Backups_AreCreatedAndDownloadedByAdministratorsOnly()
    {
        var client = CreateClient();
        var (adminCookie, adminCsrf) = await LoginAsync(client);
        string? name = null;
        try
        {
            var create = await client.SendAsync(AuthedRequest(HttpMethod.Post, "/_/api/backups", adminCookie, adminCsrf), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, create.StatusCode);
            name = (await create.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("created").GetString();

            var list = await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/api/backups", adminCookie), TestContext.Current.CancellationToken);
            var names = (await list.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
                .GetProperty("backups").EnumerateArray().Select(b => b.GetProperty("name").GetString());
            Assert.Contains(name, names);

            var download = await client.SendAsync(AuthedRequest(HttpMethod.Get, $"/_/api/backups/{name}", adminCookie), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal("application/zip", download.Content.Headers.ContentType?.MediaType);

            var traversal = await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/api/backups/..%2Fappsettings.json", adminCookie), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, traversal.StatusCode);

            var addViewer = AuthedRequest(HttpMethod.Post, "/_/api/users", adminCookie, adminCsrf,
                new { username = "backup-viewer", password = "V13wer-backup-pw-99", role = "viewer", current_password = SeededPassword });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(addViewer, TestContext.Current.CancellationToken)).StatusCode);
            var (viewerCookie, viewerCsrf) = await SignInAsync(client, "backup-viewer", "V13wer-backup-pw-99");

            var viewerDownload = await client.SendAsync(AuthedRequest(HttpMethod.Get, $"/_/api/backups/{name}", viewerCookie), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, viewerDownload.StatusCode);
            var viewerCreate = await client.SendAsync(AuthedRequest(HttpMethod.Post, "/_/api/backups", viewerCookie, viewerCsrf), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, viewerCreate.StatusCode);
            var viewerRun = await client.SendAsync(AuthedRequest(HttpMethod.Post, "/_/api/maintenance/run", viewerCookie, viewerCsrf), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, viewerRun.StatusCode);
            var viewerRestore = await client.SendAsync(AuthedRequest(HttpMethod.Post, $"/_/api/backups/{name}/restore", viewerCookie, viewerCsrf), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, viewerRestore.StatusCode);

            var restore = await client.SendAsync(AuthedRequest(HttpMethod.Post, $"/_/api/backups/{name}/restore", adminCookie, adminCsrf), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
            var pending = await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/api/backups", adminCookie), TestContext.Current.CancellationToken);
            Assert.Equal(name, (await pending.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("pending_restore").GetString());

            var cancel = await client.SendAsync(AuthedRequest(HttpMethod.Delete, "/_/api/backups/restore", adminCookie, adminCsrf), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
            Assert.Null(PortwayApi.Services.Database.BackupArchive.PendingRestore(Directory.GetCurrentDirectory()));
        }
        finally
        {
            PortwayApi.Services.Database.BackupArchive.CancelRestore(Directory.GetCurrentDirectory());
            if (name is not null && PortwayApi.Services.Database.BackupArchive.Resolve(Directory.GetCurrentDirectory(), name) is { } path)
                File.Delete(path);
        }
    }

    [Fact]
    public async Task MaintenanceRun_ReturnsIntegrityPerDatabase()
    {
        var client = CreateClient();
        var (adminCookie, adminCsrf) = await LoginAsync(client);

        var resp = await client.SendAsync(AuthedRequest(HttpMethod.Post, "/_/api/maintenance/run", adminCookie, adminCsrf), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var results = (await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("results");
        Assert.Equal(JsonValueKind.Array, results.ValueKind);
    }

    [Fact]
    public async Task SeedingKey_CanBeClearedButNeverSet()
    {
        var client = CreateClient();
        var (authCookie, csrfCookie) = await LoginAsync(client);

        var set = AuthedRequest(HttpMethod.Put, "/_/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["Console:AdminApiKey"] = "a-brand-new-secret-value" });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(set, TestContext.Current.CancellationToken)).StatusCode);

        var clear = AuthedRequest(HttpMethod.Put, "/_/api/settings", authCookie, csrfCookie,
            new Dictionary<string, object> { ["Console:AdminApiKey"] = "" });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(clear, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task UntrustedForwardedFor_CannotChooseItsOwnClientIp()
    {
        // Untrusted X-Forwarded-For must not set RemoteIpAddress
        var client = CreateClient();
        var (authCookie, _) = await LoginAsync(client);

        var req = AuthedRequest(HttpMethod.Get, "/_/api/settings", authCookie);
        req.Headers.Add("X-Forwarded-For", "203.0.113.9");
        var json = await (await client.SendAsync(req, TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        var security = json.GetProperty("security");
        Assert.False(security.GetProperty("trusted_proxies_configured").GetBoolean());
        Assert.NotEqual("203.0.113.9", security.GetProperty("client_ip").GetString());
    }

    [Theory]
    [InlineData("http://localhost/_/api/settings", false)]
    [InlineData("https://localhost/_/api/settings", true)]
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

        var req = AuthedRequest(HttpMethod.Get, "/_/api/settings", authCookie);
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
        var resp = await client.GetAsync("/_/login", TestContext.Current.CancellationToken);

        Assert.False(resp.Headers.Contains("X-Powered-By"));
        Assert.Equal("same-origin", resp.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
        Assert.Equal("same-origin", resp.Headers.GetValues("Cross-Origin-Resource-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", resp.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task HealthChecks_RequireConsoleSession()
    {
        var client = CreateClient();
        var resp = await client.GetAsync("/_/api/health/checks", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
    }

    [Fact]
    public async Task HealthChecks_ListEachCheckWithStatusAndDuration()
    {
        var client = CreateClient();
        var (authCookie, _) = await LoginAsync(client);

        var resp = await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/api/health/checks", authCookie), TestContext.Current.CancellationToken);

        // the console reads the status from the body, so an unhealthy gateway still answers 200
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrEmpty(json.GetProperty("status").GetString()));
        Assert.True(json.GetProperty("uptime_seconds").GetInt64() >= 0);
        var checks = json.GetProperty("checks").EnumerateArray().ToList();
        Assert.NotEmpty(checks);
        Assert.All(checks, c =>
        {
            Assert.False(string.IsNullOrEmpty(c.GetProperty("name").GetString()));
            Assert.False(string.IsNullOrEmpty(c.GetProperty("status").GetString()));
            Assert.True(c.GetProperty("duration_ms").GetDouble() >= 0);
            Assert.False(c.TryGetProperty("data", out _));
        });
    }

    [Fact]
    public async Task CreateUser_AssignsARandomAvatarAndStoresTheDisplayName()
    {
        var client = CreateClient();
        var (cookie, csrf) = await LoginAsync(client);

        var create = AuthedRequest(HttpMethod.Post, "/_/api/users", cookie, csrf,
            new { username = "avatar-auto", password = "Av4tar-auto-pw-123", role = "viewer", display_name = "  Jules Verne ", current_password = SeededPassword });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(create, TestContext.Current.CancellationToken)).StatusCode);

        var user = await FindListedUserAsync(client, cookie, "avatar-auto");
        Assert.Equal("Jules Verne", user.GetProperty("display_name").GetString());
        Assert.Contains(user.GetProperty("avatar_style").GetString(), PortwayApi.Services.Avatars.AvatarService.Styles);
        Assert.StartsWith("data:image/svg+xml", user.GetProperty("avatar").GetString());
    }

    [Fact]
    public async Task CreateUser_WithRemovedAvatar_HasNone()
    {
        var client = CreateClient();
        var (cookie, csrf) = await LoginAsync(client);

        var create = AuthedRequest(HttpMethod.Post, "/_/api/users", cookie, csrf,
            new { username = "avatar-none", password = "Av4tar-none-pw-123", role = "viewer", avatar_style = "", current_password = SeededPassword });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(create, TestContext.Current.CancellationToken)).StatusCode);

        var user = await FindListedUserAsync(client, cookie, "avatar-none");
        Assert.Equal(JsonValueKind.Null, user.GetProperty("avatar").ValueKind);
    }

    [Theory]
    [InlineData("adventurer", "abc123", "avatar")]
    [InlineData("rings", "../../etc", "avatar")]
    public async Task CreateUser_RejectsAvatarOutsideTheAllowlist(string style, string seed, string field)
    {
        var client = CreateClient();
        var (cookie, csrf) = await LoginAsync(client);

        var create = AuthedRequest(HttpMethod.Post, "/_/api/users", cookie, csrf,
            new { username = "avatar-bad", password = "Av4tar-bad-pw-1234", role = "viewer", avatar_style = style, avatar_seed = seed, current_password = SeededPassword });
        var resp = await client.SendAsync(create, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(field, json.GetProperty("field").GetString());
    }

    [Fact]
    public async Task CreateUser_RejectsAnOverlongDisplayName()
    {
        var client = CreateClient();
        var (cookie, csrf) = await LoginAsync(client);

        var create = AuthedRequest(HttpMethod.Post, "/_/api/users", cookie, csrf,
            new { username = "avatar-long", password = "Av4tar-long-pw-123", role = "viewer", display_name = new string('x', 81), current_password = SeededPassword });
        var resp = await client.SendAsync(create, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_ChangesDisplayNameAndAvatar()
    {
        var client = CreateClient();
        var (cookie, csrf) = await LoginAsync(client);
        var create = AuthedRequest(HttpMethod.Post, "/_/api/users", cookie, csrf,
            new { username = "avatar-edit", password = "Av4tar-edit-pw-123", role = "viewer", current_password = SeededPassword });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(create, TestContext.Current.CancellationToken)).StatusCode);
        var id = (await FindListedUserAsync(client, cookie, "avatar-edit")).GetProperty("id").GetInt32();

        var update = AuthedRequest(HttpMethod.Put, $"/_/api/users/{id}", cookie, csrf,
            new { display_name = "Edited", avatar_style = "rings", avatar_seed = "k3x9", current_password = SeededPassword });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(update, TestContext.Current.CancellationToken)).StatusCode);

        var user = await FindListedUserAsync(client, cookie, "avatar-edit");
        Assert.Equal("Edited", user.GetProperty("display_name").GetString());
        Assert.Equal("rings", user.GetProperty("avatar_style").GetString());
        Assert.Equal("k3x9", user.GetProperty("avatar_seed").GetString());
    }

    [Fact]
    public async Task AvatarCandidates_OfferRenderedChoices()
    {
        var client = CreateClient();
        var (cookie, _) = await LoginAsync(client);

        var resp = await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/api/avatars/candidates?count=500", cookie), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var candidates = (await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("candidates").EnumerateArray().ToList();

        Assert.InRange(candidates.Count, 1, 24);
        Assert.All(candidates, c =>
        {
            Assert.Contains(c.GetProperty("style").GetString(), PortwayApi.Services.Avatars.AvatarService.Styles);
            Assert.StartsWith("data:image/svg+xml", c.GetProperty("avatar").GetString());
        });
    }

    private static async Task<JsonElement> FindListedUserAsync(HttpClient client, string cookie, string username)
    {
        var resp = await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/api/users", cookie), TestContext.Current.CancellationToken);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return json.GetProperty("users").EnumerateArray().Single(u => u.GetProperty("username").GetString() == username);
    }

    [Fact]
    public async Task AuditEntries_RecordTheSignedInAccount()
    {
        var client = CreateClient();
        var (cookie, csrf) = await LoginAsync(client);

        var put = AuthedRequest(HttpMethod.Put, "/_/api/environments/settings", cookie, csrf,
            new { server_name = "localhost", allowed_environments = new[] { "500", "700" } });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(put, TestContext.Current.CancellationToken)).StatusCode);

        var audit = await (await client.SendAsync(AuthedRequest(HttpMethod.Get, "/_/api/audit?limit=1", cookie), TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var entry = audit.GetProperty("entries").EnumerateArray().Single();
        Assert.Equal("environment-settings", entry.GetProperty("target_type").GetString());
        Assert.Equal("admin", entry.GetProperty("actor").GetString());
    }
}
