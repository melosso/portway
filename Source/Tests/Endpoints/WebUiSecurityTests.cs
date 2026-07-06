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

    public WebUiSecurityTests()
    {
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
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    /// <summary>Logs in and returns the auth and csrf cookie values</summary>
    private async Task<(string AuthCookie, string CsrfCookie)> LoginAsync(HttpClient client)
    {
        var csrfResp = await client.GetFromJsonAsync<JsonElement>("/ui/api/auth/csrf");
        var oneTimeCsrf = csrfResp.GetProperty("csrf").GetString()!;

        var login = await client.PostAsJsonAsync("/ui/api/auth", new { apiKey = AdminKey, csrf = oneTimeCsrf });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

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
        Assert.Equal("strong", security.GetProperty("admin_key_strength").GetString());
        Assert.True(security.GetProperty("csrf_protection").GetBoolean());
    }
}
