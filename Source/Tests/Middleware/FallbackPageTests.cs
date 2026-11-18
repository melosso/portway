using Microsoft.AspNetCore.Http;
using PortwayApi.Middleware;
using Xunit;

namespace PortwayApi.Tests.Middleware;

/// <summary>
/// Browser 404 page: links follow the route prefix, styles come from site.css
/// </summary>
public sealed class FallbackPageTests
{
    private static async Task<(int Status, string ContentType, string Body)> RenderAsync(string accept, string pathBase = "")
    {
        var context = new DefaultHttpContext();
        context.Request.PathBase = pathBase;
        context.Request.Path = "/missing";
        context.Request.Headers.Accept = accept;
        context.Response.Body = new MemoryStream();

        await FallbackEndpointExtensions.WriteNotFoundAsync(context);

        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, context.Response.ContentType ?? "", await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("/portway")]
    public async Task BrowserRequest_LinksFollowPathBase(string pathBase)
    {
        var (status, _, body) = await RenderAsync("text/html", pathBase);

        Assert.Equal(404, status);
        Assert.Contains($"href=\"{pathBase}/css/site.css\"", body);
        Assert.Contains($"src=\"{pathBase}/js/theme-toggle.js\"", body);
        Assert.Contains($"href=\"{pathBase}/_/dashboard\"", body);
    }

    [Fact]
    public async Task BrowserRequest_HasHeadingAndNoInlineStyles()
    {
        var (_, _, body) = await RenderAsync("text/html");

        Assert.Contains("<h1>Page not found</h1>", body);
        Assert.Contains("<main class=\"status-page\">", body);
        Assert.DoesNotContain("style=", body);
    }

    [Fact]
    public async Task ApiRequest_ReturnsJson()
    {
        var (status, contentType, body) = await RenderAsync("application/json");

        Assert.Equal(404, status);
        Assert.StartsWith("application/json", contentType);
        Assert.Contains("Route not found", body);
    }
}
