using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using PortwayApi.Classes;
using PortwayApi.Interfaces;
using PortwayApi.Middleware;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Middleware;

public sealed class TrafficLogEndpointNameTests : ApiTestBase
{
    [Theory]
    [InlineData("/api/500/Inventory/Products", "Inventory/Products")]
    [InlineData("/api/500/Sales/Invoices", "Sales/Invoices")]
    [InlineData("/api/500/Nonexistent/Probe", "")]
    public async Task TrafficLog_RecordsTheResolvedEndpointName(string path, string expected)
    {
        var channel = Channel.CreateUnbounded<ProxyTrafficLogEntry>();
        var envProvider = new Mock<IEnvironmentSettingsProvider>();
        envProvider.Setup(p => p.GetEnvironmentConfigAsync(It.IsAny<string>())).ReturnsAsync(new EnvironmentConfig());
        var provider = new ServiceCollection().AddSingleton(envProvider.Object).BuildServiceProvider();
        var middleware = new ProxyTrafficLoggerMiddleware(_ => Task.CompletedTask,
            Options.Create(new RequestTrafficLoggingOptions { Enabled = true }), channel, provider);
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.True(channel.Reader.TryRead(out var entry));
        Assert.Equal(("500", expected), (entry!.Environment, entry.EndpointName));
    }
}
