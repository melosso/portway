using System.Text.Json;
using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public sealed class ErrorRecordShapeTests
{
    [Fact]
    public void CompositeStepFailure_KeepsTheDocumentedKeys()
    {
        var json = JsonSerializer.Serialize(CompositeErrorResponse.Of("Error executing step 'Lines'", "trace", "Lines", ["Header"], "Upstream returned HTTP 400", 400));

        Assert.Equal(["success", "error", "details", "traceId", "step", "statusCode", "completedSteps"], Keys(json));
    }

    [Fact]
    public void CompositeUnexpectedFailure_OmitsDetailsAndStatusCode()
    {
        var json = JsonSerializer.Serialize(CompositeErrorResponse.Of("Error executing step 'Lines'", "trace", "Lines", []));

        Assert.Equal(["success", "error", "traceId", "step", "completedSteps"], Keys(json));
    }

    [Fact]
    public void RateLimit_KeepsTheDocumentedRetrytime()
    {
        var json = JsonSerializer.Serialize(RateLimitErrorResponse.Of("Too many requests", "2026-01-01T00:00:00Z"));

        Assert.Equal(["success", "error", "retrytime"], Keys(json));
    }

    private static string[] Keys(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return [.. doc.RootElement.EnumerateObject().Select(p => p.Name)];
    }
}
