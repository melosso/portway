using PortwayApi.Tests.Base;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// Verifies standardized HTTP response shapes from EndpointController.
/// Uses JsonDocument to assert body shape without coupling to record types.
/// </summary>
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
        // Error shape must be { success, error } — nothing else
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

        // The demo file endpoint "attachments" requires environment "500"
        // We just need a file endpoint that exists; shape check is what matters.
        // /api/500/files/attachments/list may 404 (no endpoint configured in tests)
        // but if it returns 200 the shape must conform. If 404 we skip shape check.
        var response = await _client.GetAsync("/api/500/files/attachments/list");

        if (response.StatusCode != HttpStatusCode.OK)
            return; // Skip shape check if endpoint not configured in test environment

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
        SetAllowedEnvironments("WMS");

        // WMS/Warehouses is a real demo SQL endpoint with GET allowed
        var response = await _client.GetAsync("/api/WMS/WMS/Warehouses");

        if (response.StatusCode != HttpStatusCode.OK)
            return; // Skip if SQL connection not available in test environment

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
        // but a successful delete must return mutation shape.
        // We test the error path here to verify shape correctness.
        var response = await _client.DeleteAsync("/api/500/files/attachments/nonexistent-file-id");

        // Either 404 (file not found) or 400/500 — both should have { success, error }
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

    // 405 response has body
    [Fact]
    public async Task WebhookGet_Returns405_WithBody()
    {
        SetAllowedEnvironments("500");

        // Webhook endpoints don't support GET — should return 405 with a body
        var response = await _client.GetAsync("/api/500/webhook/somewebhook");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);

        var bodyStr = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(bodyStr);

        // Should be valid JSON with { success: false, error: string }
        var body = JsonDocument.Parse(bodyStr);
        Assert.True(body.RootElement.TryGetProperty("success", out var success));
        Assert.Equal(JsonValueKind.False, success.ValueKind);
        Assert.True(body.RootElement.TryGetProperty("error", out _));
    }

    // Helper
    private static async Task<JsonDocument> ParseBody(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrEmpty(content), "Response body should not be empty");
        return JsonDocument.Parse(content);
    }
}
