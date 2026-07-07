using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using PortwayApi.Middleware;
using Xunit;

namespace PortwayApi.Tests.Middleware;

public class ETagMiddlewareTests
{
    private static DefaultHttpContext CreateContext(string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static ETagMiddleware CreateMiddleware(string body, int statusCode = StatusCodes.Status200OK)
        => new(async ctx =>
        {
            ctx.Response.StatusCode = statusCode;
            await ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(body));
        });

    [Fact]
    public async Task ApiGet_Returns_ETag_And_Body()
    {
        var context = CreateContext("GET", "/api/500/Products");
        await CreateMiddleware("""{"value":[1,2]}""").InvokeAsync(context);

        var etag = context.Response.Headers.ETag.ToString();
        Assert.StartsWith("\"", etag);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        Assert.Equal("""{"value":[1,2]}""", await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public async Task MatchingIfNoneMatch_Returns304_WithoutBody()
    {
        // First request to learn the ETag
        var first = CreateContext("GET", "/api/500/Products");
        await CreateMiddleware("""{"value":[1,2]}""").InvokeAsync(first);
        var etag = first.Response.Headers.ETag.ToString();

        var second = CreateContext("GET", "/api/500/Products");
        second.Request.Headers.IfNoneMatch = etag;
        await CreateMiddleware("""{"value":[1,2]}""").InvokeAsync(second);

        Assert.Equal(StatusCodes.Status304NotModified, second.Response.StatusCode);
        Assert.Equal(0, second.Response.Body.Length);
        Assert.Equal(etag, second.Response.Headers.ETag.ToString());
    }

    [Fact]
    public async Task ChangedBody_Produces_DifferentETag_AndFullResponse()
    {
        var first = CreateContext("GET", "/api/500/Products");
        await CreateMiddleware("""{"value":[1]}""").InvokeAsync(first);
        var staleTag = first.Response.Headers.ETag.ToString();

        var second = CreateContext("GET", "/api/500/Products");
        second.Request.Headers.IfNoneMatch = staleTag;
        await CreateMiddleware("""{"value":[1,2,3]}""").InvokeAsync(second);

        Assert.Equal(StatusCodes.Status200OK, second.Response.StatusCode);
        Assert.NotEqual(staleTag, second.Response.Headers.ETag.ToString());
        Assert.True(second.Response.Body.Length > 0);
    }

    [Theory]
    [InlineData("POST", "/api/500/Products")]
    [InlineData("GET", "/ui/api/settings")]
    [InlineData("GET", "/health")]
    public async Task NonApiGet_IsUntouched(string method, string path)
    {
        var context = CreateContext(method, path);
        await CreateMiddleware("payload").InvokeAsync(context);

        Assert.True(string.IsNullOrEmpty(context.Response.Headers.ETag.ToString()));
        context.Response.Body.Position = 0;
        Assert.Equal("payload", await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public async Task ErrorResponse_GetsNoETag()
    {
        var context = CreateContext("GET", "/api/500/Products");
        await CreateMiddleware("""{"error":"boom"}""", StatusCodes.Status500InternalServerError).InvokeAsync(context);

        Assert.True(string.IsNullOrEmpty(context.Response.Headers.ETag.ToString()));
        context.Response.Body.Position = 0;
        Assert.Equal("""{"error":"boom"}""", await new StreamReader(context.Response.Body).ReadToEndAsync());
    }
}
