using System.Net;
using Moq;
using PortwayApi.Classes;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Middleware;

/// <summary>
/// Environment authentication fails closed when its configuration or a referenced secret cannot be loaded
/// </summary>
public sealed class EnvironmentSecretAuthTests : ApiTestBase
{
    private const string Env = "700";

    private void EnvironmentConfig(EnvironmentConfig config) =>
        _mockEnvironmentSettingsProvider.Setup(p => p.GetEnvironmentConfigAsync(Env)).ReturnsAsync(config);

    private static AuthenticationSettings ApiKey(string value, bool overrideGlobalToken = true) => new()
    {
        Enabled = true,
        OverrideGlobalToken = overrideGlobalToken,
        Methods = [new AuthenticationMethod { Type = "ApiKey", Name = "X-Env-Key", Value = value, In = "Header" }]
    };

    [Theory]
    [InlineData("/api/700/Static/Countries")]
    [InlineData("/api/700/files/documents/report.pdf")]
    [InlineData("/api/700/composite/Orders")]
    [InlineData("/api/700/CRM/Accounts")]
    [InlineData("/api/700/Proxy/Divisions")]
    public async Task MissingSecret_OverrideGlobalToken_RejectsGlobalToken(string path)
    {
        EnvironmentConfig(new EnvironmentConfig { Authentication = ApiKey("${secret:MISSING}") });
        AddAuthorizationHeader();

        var response = await _client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("MISSING", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MissingSecret_WithoutOverride_StillRejectsGlobalToken()
    {
        EnvironmentConfig(new EnvironmentConfig { Authentication = ApiKey("${secret:MISSING}", overrideGlobalToken: false) });
        AddAuthorizationHeader();

        var response = await _client.GetAsync("/api/700/CRM/Accounts", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task ResolvedSecret_AuthenticatesRequest()
    {
        EnvironmentConfig(new EnvironmentConfig { Authentication = ApiKey("${secret:ENV-KEY}") });
        _mockEnvironmentSettingsProvider.Setup(p => p.GetSecretAsync(Env, "ENV-KEY", It.IsAny<CancellationToken>())).ReturnsAsync("env-key-value");
        _client.DefaultRequestHeaders.Add("X-Env-Key", "env-key-value");

        var response = await _client.GetAsync("/api/700/Nope/Nothing", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MissingHeaderSecret_LeavesAuthenticationWorking()
    {
        EnvironmentConfig(new EnvironmentConfig
        {
            Headers = new Dictionary<string, string> { ["X-Upstream-Key"] = "${secret:MISSING}" },
            Authentication = ApiKey("literal-key")
        });
        _client.DefaultRequestHeaders.Add("X-Env-Key", "literal-key");

        var response = await _client.GetAsync("/api/700/Nope/Nothing", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MalformedSettings_NoLastGood_Returns500()
    {
        _mockEnvironmentSettingsProvider.Setup(p => p.GetEnvironmentConfigAsync(Env))
            .ThrowsAsync(new InvalidOperationException("could not be loaded"));
        AddAuthorizationHeader();

        var response = await _client.GetAsync("/api/700/CRM/Accounts", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task AbsentSettings_KeepsTokenPath()
    {
        _mockEnvironmentSettingsProvider.Setup(p => p.GetEnvironmentConfigAsync(Env)).ReturnsAsync((EnvironmentConfig?)null);

        _client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/700/Nope/Nothing", TestContext.Current.CancellationToken)).StatusCode);

        AddAuthorizationHeader();
        var response = await _client.GetAsync("/api/700/Nope/Nothing", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private void ScopedApiKey(params string[]? allowedEndpoints)
    {
        var auth = ApiKey("scoped-key");
        auth.Methods![0].AllowedEndpoints = allowedEndpoints?.ToList();
        EnvironmentConfig(new EnvironmentConfig { Authentication = auth });
        _client.DefaultRequestHeaders.Add("X-Env-Key", "scoped-key");
    }

    [Fact]
    public async Task EnvironmentAuth_OutOfScope_MatchesMissingEndpoint()
    {
        ScopedApiKey("Nope");

        var existing = await _client.GetAsync("/api/700/CRM/Accounts", TestContext.Current.CancellationToken);
        var missing = await _client.GetAsync("/api/700/Other/Thing", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, existing.StatusCode);
        Assert.Equal(existing.StatusCode, missing.StatusCode);
        Assert.Equal(
            await existing.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            await missing.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EnvironmentAuth_InScope_Allowed()
    {
        ScopedApiKey("No*");

        var response = await _client.GetAsync("/api/700/Nope/Nothing", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EnvironmentAuth_Unset_KeepsAccessAndWarns()
    {
        ScopedApiKey(null);

        var response = await _client.GetAsync("/api/700/Nope/Nothing", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(PortwayApi.Classes.EnvironmentSettingsProvider.HasUnscopedMethods(new AuthenticationSettings
        {
            Enabled = true,
            Methods = [new AuthenticationMethod { Type = "ApiKey" }, new AuthenticationMethod { Type = "Bearer", AllowedEndpoints = ["CRM/*"] }]
        }));
        Assert.False(PortwayApi.Classes.EnvironmentSettingsProvider.HasUnscopedMethods(new AuthenticationSettings
        {
            Enabled = false,
            Methods = [new AuthenticationMethod { Type = "ApiKey" }]
        }));
    }
}
