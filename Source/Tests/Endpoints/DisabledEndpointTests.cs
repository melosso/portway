using System.Net;
using System.Text.Json;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>Endpoints switched off through Enabled report a deliberate outage instead of serving</summary>
/// <remarks>
/// endpoints/Static/Production/Machines ships with Enabled false as the worked example.
/// Every controller-served type (SQL, Proxy, Static, Webhook, Files) passes the same gate in
/// EndpointController.TryResolveEndpoint, so one disabled sample exercises all of them.
/// </remarks>
public class DisabledEndpointTests : ApiTestBase
{
    private const string DisabledPath = "/api/500/Production/Machines";

    public DisabledEndpointTests()
    {
        SetAllowedEnvironments("500", "700", "Synergy", "WMS");
    }

    [Fact]
    public async Task DisabledEndpoint_Returns503_WithSharedEnvelopeAndRetryAfter()
    {
        var response = await _client.GetAsync(DisabledPath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("3600", Assert.Single(response.Headers.GetValues("Retry-After")));

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task DisabledEndpoint_RejectsFilteredReads_Too()
    {
        var response = await _client.GetAsync($"{DisabledPath}?$filter=Id eq 1");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task EnabledEndpoint_IsUnaffected()
    {
        var response = await _client.GetAsync("/api/500/Masterdata/CostCenters");

        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task DisabledEndpoint_StaysInTheDocument_MarkedUnavailable()
    {
        var response = await _client.GetAsync("/docs/openapi/v1/openapi.json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var operation = doc.RootElement.GetProperty("paths")
            .GetProperty("/api/{env}/Production/Machines")
            .GetProperty("get");

        Assert.True(operation.GetProperty("deprecated").GetBoolean());
        Assert.StartsWith("[Disabled] ", operation.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task EveryOperation_Documents503()
    {
        var response = await _client.GetAsync("/docs/openapi/v1/openapi.json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var offenders = new List<string>();
        foreach (var path in doc.RootElement.GetProperty("paths").EnumerateObject())
        foreach (var op in path.Value.EnumerateObject())
        {
            if (!op.Value.TryGetProperty("responses", out var responses)) continue;
            if (!responses.TryGetProperty("503", out var unavailable))
            {
                offenders.Add($"{path.Name} {op.Name}");
                continue;
            }

            var reference = unavailable.GetProperty("content").GetProperty("application/json")
                .GetProperty("schema").GetProperty("$ref").GetString();
            Assert.Equal("#/components/schemas/ErrorResponse", reference);
        }

        Assert.True(offenders.Count == 0, "Operations missing a 503 response:\n" + string.Join("\n", offenders));
    }
}
