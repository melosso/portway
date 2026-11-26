using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

/// <summary>
/// Guards the shared error envelope for handlers returning minimal-API results
/// </summary>
public class PortwayResultsTests
{
    // An unhandled error must never return exception text to the caller
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

    [Fact]
    public async Task MinimalError_UsesSharedEnvelope_AndOmitsFieldWhenNull()
    {
        var context = BuildContext();

        await PortwayResults.MinimalError("Token not found", StatusCodes.Status404NotFound).ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        using var body = ReadBody(context);
        var keys = body.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(["success", "error"], keys);
        Assert.Equal("Token not found", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task MinimalError_IncludesFieldWhenSet()
    {
        var context = BuildContext();

        await PortwayResults.MinimalError("That username is already taken", StatusCodes.Status409Conflict, "username").ExecuteAsync(context);

        using var body = ReadBody(context);
        Assert.Equal(JsonValueKind.False, body.RootElement.GetProperty("success").ValueKind);
        Assert.Equal("username", body.RootElement.GetProperty("field").GetString());
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
