using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using PortwayApi.Classes;
using PortwayApi.Interfaces;
using PortwayApi.Middleware;
using Xunit;

namespace PortwayApi.Tests.Middleware;

/// <summary>
/// The traffic logger must redact a custom auth header or query param name, and the query string itself
/// </summary>
public class ProxyTrafficLoggerMiddlewareRedactionTests
{
    private static (ProxyTrafficLoggerMiddleware Middleware, ChannelReader<ProxyTrafficLogEntry> Reader) BuildMiddleware(
        AuthenticationSettings? auth, RequestDelegate? next = null)
    {
        var channel = Channel.CreateUnbounded<ProxyTrafficLogEntry>();

        var envProvider = new Mock<IEnvironmentSettingsProvider>();
        envProvider.Setup(p => p.GetEnvironmentConfigAsync(It.IsAny<string>()))
            .ReturnsAsync(new EnvironmentConfig { Authentication = auth });

        var services = new ServiceCollection();
        services.AddSingleton(envProvider.Object);
        var provider = services.BuildServiceProvider();

        var options = Options.Create(new RequestTrafficLoggingOptions { Enabled = true, CaptureHeaders = true });
        var middleware = new ProxyTrafficLoggerMiddleware(next ?? (_ => Task.CompletedTask), options, channel, provider);

        return (middleware, channel.Reader);
    }

    private static DefaultHttpContext BuildContext(string path, string? queryString = null, string? headerName = null, string? headerValue = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Method = "GET";
        if (queryString != null) ctx.Request.QueryString = new QueryString(queryString);
        if (headerName != null) ctx.Request.Headers[headerName] = headerValue;
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    [Fact]
    public async Task CustomQueryParamAuthName_IsRedactedInLoggedQueryString()
    {
        var auth = new AuthenticationSettings
        {
            Enabled = true,
            Methods = [new AuthenticationMethod { Type = "apikey", Name = "apikey", In = "Query", Value = "sk_live_abc123" }]
        };
        var (middleware, reader) = BuildMiddleware(auth);
        var ctx = BuildContext("/api/prod/orders", "?apikey=sk_live_abc123");

        await middleware.InvokeAsync(ctx);

        Assert.True(reader.TryRead(out var entry));
        Assert.Equal("?apikey=***", entry!.QueryString);
    }

    [Fact]
    public async Task CustomHeaderAuthName_IsRedactedInLoggedHeaders()
    {
        var auth = new AuthenticationSettings
        {
            Enabled = true,
            Methods = [new AuthenticationMethod { Type = "apikey", Name = "X-Env-Auth", In = "Header", Value = "sk_live_abc123" }]
        };
        var (middleware, reader) = BuildMiddleware(auth);
        var ctx = BuildContext("/api/prod/orders", headerName: "X-Env-Auth", headerValue: "sk_live_abc123");

        await middleware.InvokeAsync(ctx);

        Assert.True(reader.TryRead(out var entry));
        Assert.Equal("[REDACTED]", entry!.RequestHeaders["X-Env-Auth"]);
    }

    [Fact]
    public async Task EveryQueryValue_IsRedactedNamesKept()
    {
        var (middleware, reader) = BuildMiddleware(auth: null);
        var ctx = BuildContext("/api/prod/orders", "?$filter=code%20eq%20%27SECRETCODE%27&apikey=querysecret123&$top=10");

        await middleware.InvokeAsync(ctx);

        Assert.True(reader.TryRead(out var entry));
        Assert.Equal("?$filter=***&apikey=***&$top=***", entry!.QueryString);
    }

    [Fact]
    public async Task TargetUrl_SetByHandler_IsLogged()
    {
        var (middleware, reader) = BuildMiddleware(null, next: ctx =>
        {
            ctx.Items["TargetUrl"] = "https://upstream.example/accounts?id=***";
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(BuildContext("/api/prod/orders", "?id=7"));

        Assert.True(reader.TryRead(out var entry));
        Assert.Equal("https://upstream.example/accounts?id=***", entry!.TargetUrl);
    }

    [Fact]
    public async Task Username_ComesFromVerifiedToken()
    {
        var (middleware, reader) = BuildMiddleware(null);
        var ctx = BuildContext("/api/prod/orders", headerName: "Authorization", headerValue: "Bearer raw-token");
        ctx.Features.Set(new PortwayApi.Auth.AuthToken { Username = "alice", TokenHash = "h", TokenSalt = "s" });

        await middleware.InvokeAsync(ctx);

        Assert.True(reader.TryRead(out var entry));
        Assert.Equal("alice", entry!.Username);
    }
}
