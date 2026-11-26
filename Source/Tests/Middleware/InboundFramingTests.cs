using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PortwayApi.Middleware;
using Xunit;

namespace PortwayApi.Tests.Middleware;

public sealed class InboundFramingTests
{
    [Theory]
    [InlineData("10", "chunked")]
    [InlineData(null, "gzip")]
    public async Task ConflictingFraming_Returns400Envelope_AndClosesTheConnection(string? contentLength, string transferEncoding)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        context.Request.Method = "POST";
        context.Request.Path = "/api/500/Inventory/Products";
        if (contentLength is not null) context.Request.Headers.ContentLength = long.Parse(contentLength);
        context.Request.Headers.TransferEncoding = transferEncoding;
        context.Response.Body = new MemoryStream();
        var reachedNext = false;

        await new SecurityHeadersMiddleware(_ => { reachedNext = true; return Task.CompletedTask; }).InvokeAsync(context);

        Assert.False(reachedNext);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("close", context.Response.Headers.Connection.ToString());
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(["success", "error"], body.RootElement.EnumerateObject().Select(p => p.Name));
    }
}
