using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

/// <summary>Guards the shared error envelope for handlers returning minimal-API results</summary>
public class PortwayResultsTests
{
    // An unhandled error must never carry exception text back to the caller
    [Fact]
    public async Task MinimalServerError_MasksDetail_AndUsesSharedEnvelope()
    {
        var context = BuildContext();

        var result = PortwayResults.MinimalServerError(context);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);

        using var body = ReadBody(context);
        var root = body.RootElement;

        Assert.Equal(JsonValueKind.False, root.GetProperty("success").ValueKind);
        Assert.Equal("An unexpected error occurred.", root.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));

        // The ProblemDetails shape must not leak back in
        Assert.False(root.TryGetProperty("type", out _));
        Assert.False(root.TryGetProperty("title", out _));
        Assert.False(root.TryGetProperty("status", out _));
        Assert.False(root.TryGetProperty("detail", out _));
    }

    // The caller may still supply its own masked wording
    [Fact]
    public async Task MinimalServerError_HonoursSuppliedDetail()
    {
        var context = BuildContext();

        var result = PortwayResults.MinimalServerError(context, "Error processing endpoint Orders");
        await result.ExecuteAsync(context);

        using var body = ReadBody(context);
        Assert.Equal("Error processing endpoint Orders", body.RootElement.GetProperty("error").GetString());
    }

    private static DefaultHttpContext BuildContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.AspNetCore.Http.Json.JsonOptions>();

        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            TraceIdentifier = "test-trace-id"
        };
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static JsonDocument ReadBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return JsonDocument.Parse(reader.ReadToEnd());
    }
}
