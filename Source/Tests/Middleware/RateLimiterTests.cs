using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PortwayApi.Middleware;
using System.Net;
using Xunit;

namespace PortwayApi.Tests.Middleware;

public class RateLimiterTests
{
    private const string TestAdminKey = "unit-test-admin-key-0123456789-0123456789";

    private static RateLimiter CreateRateLimiter(
        RequestDelegate next,
        int ipLimit = 10000,
        int tokenLimit = 10000,
        bool enabled = true,
        string adminApiKey = "")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Enabled"] = enabled.ToString().ToLowerInvariant(),
                ["RateLimiting:IpLimit"] = ipLimit.ToString(),
                ["RateLimiting:IpWindow"] = "60",
                ["RateLimiting:TokenLimit"] = tokenLimit.ToString(),
                ["RateLimiting:TokenWindow"] = "60",
            })
            .Build();

        var logger = new Mock<ILogger<RateLimiter>>().Object;

        return new RateLimiter(next, config, logger, adminApiKey);
    }

    // Mints a session cookie in the same format WebUiEndpoints.GenerateToken produces
    private static string MintValidSessionCookie(string adminApiKey)
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString();
        var signingKey = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(adminApiKey));
        using var hmac = new System.Security.Cryptography.HMACSHA256(signingKey);
        var sig = Convert.ToBase64String(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(expiry)));
        return $"{expiry}.{sig}";
    }

    private static DefaultHttpContext BuildContext(string path = "/api/test", string? bearerToken = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();

        if (bearerToken != null)
            ctx.Request.Headers.Authorization = $"Bearer {bearerToken}";

        return ctx;
    }

    [Fact]
    public async Task InvokeAsync_BelowLimit_PassesThrough()
    {
        var nextCalled = false;
        var limiter = CreateRateLimiter(_ => { nextCalled = true; return Task.CompletedTask; });

        await limiter.InvokeAsync(BuildContext(), null!, null!);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_DisabledRateLimiting_AlwaysPassesThrough()
    {
        var callCount = 0;
        var limiter = CreateRateLimiter(_ => { callCount++; return Task.CompletedTask; }, ipLimit: 1, enabled: false);

        for (var i = 0; i < 5; i++)
            await limiter.InvokeAsync(BuildContext(), null!, null!);

        Assert.Equal(5, callCount);
    }

    [Fact]
    public async Task InvokeAsync_BearerTokenPresent_RequestPassesThroughWithinLimit()
    {
        var nextCalled = false;
        var limiter = CreateRateLimiter(_ => { nextCalled = true; return Task.CompletedTask; }, tokenLimit: 10000);
        var ctx = BuildContext(bearerToken: "abc.def.ghi");

        await limiter.InvokeAsync(ctx, null!, null!);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_ExceedsIpLimit_Returns429()
    {
        var passed = 0;
        var limiter = CreateRateLimiter(_ => { passed++; return Task.CompletedTask; }, ipLimit: 3, tokenLimit: 10000);

        for (var i = 0; i < 5; i++)
        {
            var ctx = BuildContext();
            await limiter.InvokeAsync(ctx, null!, null!);
        }

        Assert.Equal(3, passed);
    }

    [Fact]
    public async Task InvokeAsync_ExceedsTokenLimit_Returns429()
    {
        var passed = 0;
        var limiter = CreateRateLimiter(_ => { passed++; return Task.CompletedTask; }, ipLimit: 10000, tokenLimit: 2);

        for (var i = 0; i < 5; i++)
        {
            var ctx = BuildContext(bearerToken: "my-rate-limited-token");
            await limiter.InvokeAsync(ctx, null!, null!);
        }

        Assert.Equal(2, passed);
    }

    [Fact]
    public async Task InvokeAsync_TwoDistinctTokens_LimitedIndependently()
    {
        var passedA = 0;
        var passedB = 0;
        var limiter = CreateRateLimiter(_ => Task.CompletedTask, ipLimit: 10000, tokenLimit: 2);

        for (var i = 0; i < 4; i++)
        {
            var ctxA = BuildContext(bearerToken: "token-aaa");
            ctxA.Response.Body = new MemoryStream();
            await limiter.InvokeAsync(ctxA, null!, null!);
            if (ctxA.Response.StatusCode != 429) passedA++;

            var ctxB = BuildContext(bearerToken: "token-bbb");
            ctxB.Response.Body = new MemoryStream();
            await limiter.InvokeAsync(ctxB, null!, null!);
            if (ctxB.Response.StatusCode != 429) passedB++;
        }

        Assert.Equal(2, passedA);
        Assert.Equal(2, passedB);
    }

    [Fact]
    public async Task InvokeAsync_UiWithValidSessionCookie_IsExempt()
    {
        var passed = 0;
        var limiter = CreateRateLimiter(_ => { passed++; return Task.CompletedTask; }, ipLimit: 2, adminApiKey: TestAdminKey);
        var cookie = MintValidSessionCookie(TestAdminKey);

        for (var i = 0; i < 5; i++)
        {
            var ctx = BuildContext(path: "/ui/api/settings");
            ctx.Request.Headers.Cookie = $"portway_auth={cookie}";
            await limiter.InvokeAsync(ctx, null!, null!);
        }

        // A valid session bypasses rate limiting; all 5 pass despite the low IP limit
        Assert.Equal(5, passed);
    }

    [Fact]
    public async Task InvokeAsync_UiWithBogusCookie_IsRateLimited()
    {
        var passed = 0;
        var limiter = CreateRateLimiter(_ => { passed++; return Task.CompletedTask; }, ipLimit: 3, adminApiKey: TestAdminKey);

        for (var i = 0; i < 6; i++)
        {
            var ctx = BuildContext(path: "/ui/api/settings");
            ctx.Request.Headers.Cookie = "portway_auth=forged-value";
            await limiter.InvokeAsync(ctx, null!, null!);
        }

        // A merely-present cookie no longer exempts; the IP limit binds
        Assert.Equal(3, passed);
    }

    [Fact]
    public async Task InvokeAsync_AuthEndpoint_IsRateLimited_EvenWithValidCookie()
    {
        var passed = 0;
        var limiter = CreateRateLimiter(_ => { passed++; return Task.CompletedTask; }, ipLimit: 3, adminApiKey: TestAdminKey);
        var cookie = MintValidSessionCookie(TestAdminKey);

        for (var i = 0; i < 6; i++)
        {
            var ctx = BuildContext(path: "/ui/api/auth/csrf");
            ctx.Request.Headers.Cookie = $"portway_auth={cookie}";
            await limiter.InvokeAsync(ctx, null!, null!);
        }

        // The credential endpoints are never exempt, so the CSRF-token route stays throttled
        Assert.Equal(3, passed);
    }

    [Fact]
    public async Task InvokeAsync_BearerTokenWithExactPrefix_ExtractedCorrectly()
    {
        // Ensures "Bearer " (7 chars including space) is stripped, not "Bearer" (6 chars)
        var captured = string.Empty;
        var limiter = CreateRateLimiter(async ctx =>
        {
            captured = ctx.Request.Headers.Authorization.ToString();
            await Task.CompletedTask;
        }, tokenLimit: 10000);

        var ctx = BuildContext(bearerToken: "exact-token-value");
        await limiter.InvokeAsync(ctx, null!, null!);

        Assert.Equal("Bearer exact-token-value", captured);
    }
}
