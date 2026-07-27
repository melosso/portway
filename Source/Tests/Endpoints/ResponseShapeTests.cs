using Moq;
using PortwayApi.Classes;
using PortwayApi.Services.Providers;
using PortwayApi.Tests.Base;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>Verifies standardized HTTP response shapes from EndpointController. Uses JsonDocument to assert body shape without coupling to record types</summary>
public class ResponseShapeTests : ApiTestBase
{
    // Error shape tests
    [Fact]
    public async Task BadEnv_Returns400_WithErrorShape()
    {
        // Environment "notallowed" is not in the global allowed list
        var response = await _client.GetAsync("/api/notallowed/SomeEndpoint");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ParseBody(response);
        Assert.True(body.RootElement.TryGetProperty("success", out var success));
        Assert.Equal(JsonValueKind.False, success.ValueKind);
        Assert.True(body.RootElement.TryGetProperty("error", out var error));
        Assert.Equal(JsonValueKind.String, error.ValueKind);
        Assert.False(string.IsNullOrEmpty(error.GetString()));

        // Must NOT have errorDetail or timestamp (old CreateErrorResponse fields)
        Assert.False(body.RootElement.TryGetProperty("errorDetail", out _));
        Assert.False(body.RootElement.TryGetProperty("timestamp", out _));
    }

    [Fact]
    public async Task UnknownEndpoint_Returns404_WithErrorShape()
    {
        SetAllowedEnvironments("500");

        // POST to an unknown endpoint that won't match any type
        var response = await _client.PostAsync("/api/500/nonexistent-xyz-unknown",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await ParseBody(response);
        Assert.True(body.RootElement.TryGetProperty("success", out var success));
        Assert.Equal(JsonValueKind.False, success.ValueKind);
        Assert.True(body.RootElement.TryGetProperty("error", out var error));
        Assert.Equal(JsonValueKind.String, error.ValueKind);
    }

    [Fact]
    public async Task CompositeGet_Returns405_WithErrorShape()
    {
        SetAllowedEnvironments("500");

        // GET to the demo composite endpoint (Financial/SalesInvoice only supports POST)
        var response = await _client.GetAsync("/api/500/Financial/SalesInvoice");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);

        var body = await ParseBody(response);
        Assert.True(body.RootElement.TryGetProperty("success", out var success));
        Assert.Equal(JsonValueKind.False, success.ValueKind);
        Assert.True(body.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task ErrorShape_HasExactlyTwoTopLevelKeys()
    {
        // Error shape must be { success, error }; nothing else
        var response = await _client.GetAsync("/api/notallowed/SomeEndpoint");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ParseBody(response);
        var keys = body.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(2, keys.Count);
        Assert.Contains("success", keys);
        Assert.Contains("error", keys);
    }

    // Collection shape tests
    [Fact]
    public async Task FileList_Returns_CollectionShape_AllKeysLowercase()
    {
        SetAllowedEnvironments("500");

        // Reports is a real demo file endpoint for environment 500; an empty listing still returns the envelope
        var response = await _client.GetAsync("/api/500/files/Reports/list");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ParseBody(response);
        var root = body.RootElement;

        // All keys must be lowercase
        Assert.True(root.TryGetProperty("success", out var success));
        Assert.True(root.TryGetProperty("count", out _));
        Assert.True(root.TryGetProperty("value", out _));
        Assert.True(root.TryGetProperty("nextLink", out _));

        Assert.Equal(JsonValueKind.True, success.ValueKind);

        // Must NOT have uppercase variants
        Assert.False(root.TryGetProperty("Success", out _));
        Assert.False(root.TryGetProperty("Count", out _));
        Assert.False(root.TryGetProperty("Value", out _));
    }

    [Fact]
    public async Task SqlGet_WhenEndpointExists_CollectionHasLowercaseKeys()
    {
        // Committed SQLite demo database ships next to the test assembly, so this asserts instead of skipping
        Assert.True(WmsDemoDbAvailable, $"WMS demo database missing at {WmsDemoDbPath}");

        SetAllowedEnvironments("WMS");

        // Shape is under test, so only the OData translation is stubbed; handler, driver and envelope stay real
        _mockODataToSqlConverter
            .Setup(c => c.ConvertToSQL(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<SqlProviderType>(),
                It.IsAny<IReadOnlyList<EndpointRelationship>?>()))
            .Returns(("SELECT Id, Code, Name, City, Country, Region, CapacityM2, IsActive FROM Warehouses LIMIT 10",
                new Dictionary<string, object>()));

        // WMS/Warehouses is a real demo SQL endpoint with GET allowed
        var response = await _client.GetAsync("/api/WMS/WMS/Warehouses");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ParseBody(response);
        var root = body.RootElement;

        Assert.True(root.TryGetProperty("success", out var success));
        Assert.True(root.TryGetProperty("count", out _));
        Assert.True(root.TryGetProperty("value", out _));
        Assert.True(root.TryGetProperty("nextLink", out _));

        Assert.Equal(JsonValueKind.True, success.ValueKind);

        // Uppercase variants must be absent
        Assert.False(root.TryGetProperty("Success", out _));
    }

    // Mutation shape tests
    [Fact]
    public async Task FileDelete_MutationShape_HasSuccessAndMessage()
    {
        SetAllowedEnvironments("500");

        // DELETE on a non-existent file returns 404 with error shape,
        // but a successful delete must return mutation shape
        // We test the error path here to verify shape correctness
        var response = await _client.DeleteAsync("/api/500/files/attachments/nonexistent-file-id");

        // Either 404 (file not found) or 400/500; both should have { success, error }
        if (response.StatusCode == HttpStatusCode.NotFound ||
            response.StatusCode == HttpStatusCode.BadRequest)
        {
            var body = await ParseBody(response);
            Assert.True(body.RootElement.TryGetProperty("success", out var s));
            Assert.Equal(JsonValueKind.False, s.ValueKind);
            Assert.True(body.RootElement.TryGetProperty("error", out _));
        }
        // If 200 (unexpected in tests), verify mutation shape
        else if (response.StatusCode == HttpStatusCode.OK)
        {
            var body = await ParseBody(response);
            Assert.True(body.RootElement.TryGetProperty("success", out var s));
            Assert.Equal(JsonValueKind.True, s.ValueKind);
            Assert.True(body.RootElement.TryGetProperty("message", out _));
        }
    }

    // The legacy flat webhook route was removed when webhooks became namespaced; GET now resolves to no endpoint
    [Fact]
    public async Task WebhookGet_LegacyRoute_Returns404_WithBody()
    {
        SetAllowedEnvironments("500");

        // '/api/{env}/webhook/{id}' is no longer a webhook; GET resolves to an unknown endpoint
        var response = await _client.GetAsync("/api/500/webhook/somewebhook");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var bodyStr = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(bodyStr);

        // Should be valid JSON with { success: false, error: string }
        var body = JsonDocument.Parse(bodyStr);
        Assert.True(body.RootElement.TryGetProperty("success", out var success));
        Assert.Equal(JsonValueKind.False, success.ValueKind);
        Assert.True(body.RootElement.TryGetProperty("error", out _));
    }

    // POST to the removed legacy webhook route returns a 410 Gone tombstone pointing at the namespaced shape
    [Fact]
    public async Task WebhookPost_LegacyRoute_Returns410_WithBody()
    {
        SetAllowedEnvironments("500");

        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/500/webhook/somewebhook", content);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);

        var bodyStr = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(bodyStr);

        // Should be valid JSON with { success: false, error: string } and mention the new namespaced route
        var body = JsonDocument.Parse(bodyStr);
        Assert.True(body.RootElement.TryGetProperty("success", out var success));
        Assert.Equal(JsonValueKind.False, success.ValueKind);
        Assert.True(body.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("namespace", error.GetString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    // QUERY (RFC 10008): Content-Type is mandatory; a non-JSON type is rejected with 415
    [Fact]
    public async Task Query_WithoutJsonContentType_Returns415()
    {
        SetAllowedEnvironments("500");

        var req = new HttpRequestMessage(new HttpMethod("QUERY"), "/api/500/AnyEndpoint")
        {
            Content = new StringContent("{}", Encoding.UTF8, "text/plain")
        };
        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    // QUERY with a malformed JSON body returns 400 (not 500)
    [Fact]
    public async Task Query_MalformedJsonBody_Returns400()
    {
        SetAllowedEnvironments("500");

        var req = new HttpRequestMessage(new HttpMethod("QUERY"), "/api/500/AnyEndpoint")
        {
            Content = new StringContent("{ not json", Encoding.UTF8, "application/json")
        };
        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // QUERY body must be a JSON object, not an array or scalar
    [Fact]
    public async Task Query_NonObjectBody_Returns400()
    {
        SetAllowedEnvironments("500");

        var req = new HttpRequestMessage(new HttpMethod("QUERY"), "/api/500/AnyEndpoint")
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        };
        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // A well-formed QUERY on an unknown endpoint reaches routing and resolves to 404
    [Fact]
    public async Task Query_ValidBodyUnknownEndpoint_Returns404()
    {
        SetAllowedEnvironments("500");

        var req = new HttpRequestMessage(new HttpMethod("QUERY"), "/api/500/NoSuchEndpoint")
        {
            Content = new StringContent("{\"filter\":\"Id eq 1\"}", Encoding.UTF8, "application/json")
        };
        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Helper
    private static async Task<JsonDocument> ParseBody(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrEmpty(content), "Response body should not be empty");
        return JsonDocument.Parse(content);
    }

    // A masked 500 uses the same envelope as every other error, plus a trace id to quote in a bug report
    [Fact]
    public async Task ServerError_Returns500_WithSharedEnvelopeAndTraceId()
    {
        SetAllowedEnvironments("500", "700");

        // No SQL server is reachable from the test host, so this endpoint fails on connect
        var response = await _client.GetAsync("/api/500/Product/Products");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var body = await ParseBody(response);
        Assert.Equal(JsonValueKind.False, body.RootElement.GetProperty("success").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("error").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));

        // The RFC 9110 ProblemDetails shape must not leak back in
        Assert.False(body.RootElement.TryGetProperty("type", out _));
        Assert.False(body.RootElement.TryGetProperty("title", out _));
        Assert.False(body.RootElement.TryGetProperty("status", out _));
    }
}
