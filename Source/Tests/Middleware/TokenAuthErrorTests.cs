using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Moq;
using PortwayApi.Auth;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Middleware;

/// <summary>
/// Auth failures answer in the shared error envelope, carry a Bearer challenge on 401 and do not reveal which endpoints exist
/// </summary>
public class TokenAuthErrorTests : ApiTestBase
{
    private const string ScopedToken = "scoped-token";
    private const string EnvScopedToken = "env-scoped-token";

    public TokenAuthErrorTests()
    {
        _mockTokenService.Setup(s => s.GetTokenDetailsByTokenAsync(ScopedToken))
            .ReturnsAsync(new AuthToken
            {
                Username = "scoped-user",
                TokenHash = "hash",
                TokenSalt = "salt",
                AllowedEnvironments = "*",
                AllowedScopes = "Inventory/Products"
            });

        _mockTokenService.Setup(s => s.GetTokenDetailsByTokenAsync(EnvScopedToken))
            .ReturnsAsync(new AuthToken
            {
                Username = "env-scoped-user",
                TokenHash = "hash",
                TokenSalt = "salt",
                AllowedEnvironments = "700",
                AllowedScopes = "*"
            });
    }

    [Fact]
    public async Task OutOfScope_ExistingAndMissingEndpoint_AnswerIdentically()
    {
        AddAuthorizationHeader(ScopedToken);

        var existing = await _client.GetAsync("/api/500/CRM/Accounts", TestContext.Current.CancellationToken);
        var missing = await _client.GetAsync("/api/500/CRM/Accountz", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, existing.StatusCode);
        Assert.Equal(existing.StatusCode, missing.StatusCode);
        Assert.Equal(
            await existing.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            await missing.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null, "/api/500/CRM/Accounts", HttpStatusCode.Unauthorized)]
    [InlineData("not-a-token", "/api/500/CRM/Accounts", HttpStatusCode.Unauthorized)]
    [InlineData(EnvScopedToken, "/api/500/CRM/Accounts", HttpStatusCode.Forbidden)]
    [InlineData(ScopedToken, "/api/500/CRM/Accounts", HttpStatusCode.Forbidden)]
    public async Task AuthFailure_UsesSharedErrorEnvelope(string? token, string path, HttpStatusCode expected)
    {
        _client.DefaultRequestHeaders.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var names = body.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(["error", "success"], names);
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task MissingToken_ChallengesWithBearer()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/api/500/CRM/Accounts", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.DoesNotContain("error=", challenge.Parameter ?? string.Empty);
    }

    [Theory]
    [InlineData(new string[0], "Bearer realm=\"portway\"")]
    [InlineData(new[] { "ApiKey", "HMAC" }, "Bearer realm=\"portway\"")]
    [InlineData(new[] { "Basic" }, "Basic realm=\"portway\"")]
    [InlineData(new[] { "basic", "JWT" }, "Basic realm=\"portway\",Bearer realm=\"portway\"")]
    public void EnvironmentChallenges_OfferBasicOnlyWhenConfigured(string[] types, string expected)
    {
        var challenges = PortwayApi.Middleware.TokenAuthMiddleware.EnvironmentChallenges(
            types.Select(t => new PortwayApi.Classes.AuthenticationMethod { Type = t }));

        Assert.Equal(expected, challenges.ToString());
    }

    [Fact]
    public async Task InvalidToken_ChallengesWithInvalidTokenError()
    {
        AddAuthorizationHeader("not-a-token");

        var response = await _client.GetAsync("/api/500/CRM/Accounts", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Contains("error=\"invalid_token\"", challenge.Parameter);
    }
}
