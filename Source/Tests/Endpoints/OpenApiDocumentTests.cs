using System.Net;
using System.Text.Json;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>Verifies OpenAPI document generation end to end; guards the Microsoft.OpenApi upgrade path since Scalar only renders what this endpoint produces</summary>
public class OpenApiDocumentTests : ApiTestBase
{
    [Fact]
    public async Task OpenApiDocument_Generates_And_Parses()
    {
        var response = await _client.GetAsync("/docs/openapi/v1/openapi.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("openapi", out var version));
        Assert.StartsWith("3.2", version.GetString());
        Assert.True(root.TryGetProperty("paths", out var paths));
        Assert.True(paths.ValueKind == JsonValueKind.Object);
        Assert.True(root.TryGetProperty("info", out _));
    }

    // The shared error envelope is registered once as a reusable component schema
    [Fact]
    public async Task SharedErrorResponse_ComponentSchema_IsRegistered()
    {
        var response = await _client.GetAsync("/docs/openapi/v1/openapi.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");

        Assert.True(schemas.TryGetProperty("ErrorResponse", out var err), "ErrorResponse component should exist");
        var props = err.GetProperty("properties");
        Assert.True(props.TryGetProperty("success", out _));
        Assert.True(props.TryGetProperty("error", out _));
        Assert.True(schemas.TryGetProperty("ValidationErrorResponse", out _), "ValidationErrorResponse component should exist");
    }

    // Operation error responses reference the shared error component instead of inlining a schema
    [Fact]
    public async Task OperationErrors_ReferenceSharedErrorSchema()
    {
        SetAllowedEnvironments("500", "700");

        var response = await _client.GetAsync("/docs/openapi/v1/openapi.json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = doc.RootElement.GetProperty("paths");

        var query = paths.GetProperty("/api/{env}/Inventory/StockLevels").GetProperty("query");
        var responses = query.GetProperty("responses");

        var badRequestRef = responses.GetProperty("400")
            .GetProperty("content").GetProperty("application/json")
            .GetProperty("$ref").GetString();
        Assert.Equal("#/components/mediaTypes/ErrorJson", badRequestRef);

        // Response summaries are the standard HTTP reason phrase; descriptions explain what it means here
        Assert.Equal("Bad Request", responses.GetProperty("400").GetProperty("summary").GetString());
        Assert.Equal("OK", responses.GetProperty("200").GetProperty("summary").GetString());
        Assert.Contains("validation", responses.GetProperty("400").GetProperty("description").GetString());
    }

    // The shared error envelope is registered once as a reusable media type, not repeated per response
    [Fact]
    public async Task SharedErrorResponse_MediaTypeComponent_IsRegistered()
    {
        var response = await _client.GetAsync("/docs/openapi/v1/openapi.json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var mediaTypes = doc.RootElement.GetProperty("components").GetProperty("mediaTypes");

        Assert.Equal("#/components/schemas/ErrorResponse",
            mediaTypes.GetProperty("ErrorJson").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.Equal("#/components/schemas/ValidationErrorResponse",
            mediaTypes.GetProperty("ValidationErrorJson").GetProperty("schema").GetProperty("$ref").GetString());
    }

    // CSV and XML bodies are not JSON, so their examples travel as serializedValue rather than a quoted JSON string
    [Fact]
    public async Task NonJsonStaticEndpoints_UseSerializedValueExamples()
    {
        SetAllowedEnvironments("500", "700", "Synergy");

        var response = await _client.GetAsync("/docs/openapi/v1/openapi.json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = doc.RootElement.GetProperty("paths");

        foreach (var (path, mediaType) in new[]
                 {
                     ("/api/{env}/Production/Lines", "text/csv"),
                     ("/api/{env}/Production/Machines", "application/xml")
                 })
        {
            var content = paths.GetProperty(path).GetProperty("get")
                .GetProperty("responses").GetProperty("200")
                .GetProperty("content").GetProperty(mediaType);

            var example = content.GetProperty("examples").EnumerateObject().First().Value;

            Assert.True(example.TryGetProperty("serializedValue", out var serialized), $"{path} should carry serializedValue");
            Assert.False(example.TryGetProperty("value", out _), $"{path} should not also carry a JSON value");
            Assert.False(string.IsNullOrWhiteSpace(serialized.GetString()));
        }
    }

    // Static endpoints serve QUERY through the same read path, so filterable ones document it
    [Fact]
    public async Task FilterableStaticEndpoint_DocumentsQueryOperation()
    {
        SetAllowedEnvironments("500", "700", "Synergy");

        var response = await _client.GetAsync("/docs/openapi/v1/openapi.json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var pathItem = doc.RootElement.GetProperty("paths").GetProperty("/api/{env}/Masterdata/Countries");
        var query = pathItem.GetProperty("query");

        var bodyProps = query.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("properties");
        Assert.True(bodyProps.TryGetProperty("filter", out _));

        // The criteria move into the body, so no OData query parameters remain alongside
        var locations = query.GetProperty("parameters").EnumerateArray()
            .Select(p => p.GetProperty("in").GetString());
        Assert.All(locations, l => Assert.Equal("path", l));
    }

    // Portway authenticates with Authorization: Bearer, which is an http scheme and not an apiKey
    [Fact]
    public async Task SecurityScheme_IsHttpBearer()
    {
        var response = await _client.GetAsync("/docs/openapi/v1/openapi.json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var bearer = doc.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");

        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());

        // name and in belong to apiKey schemes; emitting them here would describe a scheme Portway does not use
        Assert.False(bearer.TryGetProperty("name", out _));
        Assert.False(bearer.TryGetProperty("in", out _));
    }

    // Bearer is the only scheme Portway publishes, so the reference UI opens with it selected
    [Fact]
    public async Task ScalarPage_PreselectsBearerScheme()
    {
        var response = await _client.GetAsync("/docs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"\"preferredSecurityScheme\"\": \"\"Bearer\"\"".Replace("\"\"", "\""), html);
    }

    // The document names its own URI so other descriptions can reference it
    [Fact]
    public async Task Document_DeclaresSelfUri()
    {
        var response = await _client.GetAsync("/docs/openapi/v1/openapi.json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var self = doc.RootElement.GetProperty("$self").GetString();
        Assert.NotNull(self);
        Assert.EndsWith("/docs/openapi/v1/openapi.json", self);
        Assert.True(Uri.IsWellFormedUriString(self, UriKind.Absolute));
    }

    // Audit: no operation may inline its own error schema, whatever endpoint type produced it
    [Fact]
    public async Task EveryErrorResponse_UsesTheSharedEnvelope()
    {
        SetAllowedEnvironments("500", "700", "Synergy", "WMS");

        var response = await _client.GetAsync("/docs/openapi/v1/openapi.json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = doc.RootElement.GetProperty("paths");

        var offenders = new List<string>();
        foreach (var path in paths.EnumerateObject())
        foreach (var op in path.Value.EnumerateObject())
        {
            if (!op.Value.TryGetProperty("responses", out var responses) || responses.ValueKind != JsonValueKind.Object) continue;
            foreach (var r in responses.EnumerateObject())
            {
                if (!int.TryParse(r.Name, out var code) || code < 400) continue;

                var reference = r.Value.TryGetProperty("content", out var content) &&
                                content.TryGetProperty("application/json", out var media) &&
                                media.TryGetProperty("$ref", out var refValue)
                    ? refValue.GetString()
                    : null;

                if (reference != "#/components/mediaTypes/ErrorJson" &&
                    reference != "#/components/mediaTypes/ValidationErrorJson")
                {
                    offenders.Add($"{path.Name} {op.Name} {r.Name}: {reference ?? "no $ref"}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Error responses not using the shared envelope:\n" + string.Join("\n", offenders));
    }

    // Multipart uploads describe the part encoding, narrowed to the extensions the endpoint allows
    [Fact]
    public async Task FileUpload_DocumentsMultipartEncoding_FromAllowedExtensions()
    {
        SetAllowedEnvironments("500", "700");

        var response = await _client.GetAsync("/docs/openapi/v1/openapi.json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var upload = doc.RootElement.GetProperty("paths").GetProperty("/api/{env}/files/Images").GetProperty("post");
        var multipart = upload.GetProperty("requestBody").GetProperty("content").GetProperty("multipart/form-data");

        var contentType = multipart.GetProperty("encoding").GetProperty("file").GetProperty("contentType").GetString();
        Assert.NotNull(contentType);
        Assert.Contains("image/png", contentType);
        Assert.DoesNotContain("application/octet-stream", contentType);
    }

    // $expand is offered only where the endpoint declares navigations, and names the ones it has
    [Fact]
    public async Task ExpandParameter_IsDocumented_OnlyForEndpointsWithRelationships()
    {
        SetAllowedEnvironments("500", "700");

        var response = await _client.GetAsync("/docs/openapi/v1/openapi.json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = doc.RootElement.GetProperty("paths");

        // Product/Products declares an Assortment navigation; Product/Stock declares none
        var expand = GetQueryParameter(paths, "/api/{env}/Product/Products", "$expand");
        Assert.NotNull(expand);
        // Optional parameters are serialized without a "required" key, so it stays unchecked in the UI
        Assert.False(expand.Value.TryGetProperty("required", out var required) && required.GetBoolean());
        Assert.Equal("string", expand.Value.GetProperty("schema").GetProperty("type").GetString());
        Assert.Contains("Assortment", expand.Value.GetProperty("description").GetString());

        Assert.Null(GetQueryParameter(paths, "/api/{env}/Product/Stock", "$expand"));
        Assert.NotNull(GetQueryParameter(paths, "/api/{env}/Product/Stock", "$select"));
    }

    private static JsonElement? GetQueryParameter(JsonElement paths, string path, string name)
    {
        if (!paths.TryGetProperty(path, out var operations)) return null;
        if (!operations.TryGetProperty("get", out var get)) return null;
        if (!get.TryGetProperty("parameters", out var parameters)) return null;

        foreach (var parameter in parameters.EnumerateArray())
        {
            if (parameter.GetProperty("name").GetString() == name)
                return parameter;
        }

        return null;
    }

    // Audit: every documented status code carries its canonical HTTP reason phrase
    [Fact]
    public async Task AllResponseDescriptions_AreStandardized()
    {
        SetAllowedEnvironments("500", "700", "Synergy", "WMS");

        var response = await _client.GetAsync("/docs/openapi/v1/openapi.json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = doc.RootElement.GetProperty("paths");

        var offenders = new List<string>();
        foreach (var path in paths.EnumerateObject())
        foreach (var op in path.Value.EnumerateObject())
        {
            if (!op.Value.TryGetProperty("responses", out var resp) || resp.ValueKind != JsonValueKind.Object) continue;
            foreach (var r in resp.EnumerateObject())
            {
                if (!int.TryParse(r.Name, out var code)) continue;
                var expected = PortwayApi.Classes.OpenApi.StandardResponses.DescriptionFor(code);
                if (expected == null) continue;
                if (!r.Value.TryGetProperty("description", out var d) || d.GetString() != expected)
                {
                    offenders.Add($"{path.Name} {op.Name} {r.Name}: '{d.GetString()}' (expected '{expected}')");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Non-standardized response descriptions:\n" + string.Join("\n", offenders));
    }

    // Regression: a QUERY-only endpoint must not be advertised as GET, and generating the document
    // must not mutate the endpoint's methods (which previously injected GET and broke the 405 gate).
    [Fact]
    public async Task QueryOnlyEndpoint_NotRenderedAsGet_AndGetStays405()
    {
        SetAllowedEnvironments("500", "700");

        // Generate the OpenAPI document (runs the document filter over the live endpoint definitions)
        var docResponse = await _client.GetAsync("/docs/openapi/v1/openapi.json");
        Assert.Equal(HttpStatusCode.OK, docResponse.StatusCode);

        using var doc = JsonDocument.Parse(await docResponse.Content.ReadAsStringAsync());
        var paths = doc.RootElement.GetProperty("paths");

        // The QUERY-only endpoint is documented as a native OpenAPI 3.2 query operation, never as GET
        Assert.True(paths.TryGetProperty("/api/{env}/Inventory/StockLevels", out var stockPath),
            "QUERY-only endpoint should be present in the 3.2 document");
        Assert.True(stockPath.TryGetProperty("query", out var queryOp),
            "A QUERY-only endpoint must be documented as a query operation");
        Assert.True(queryOp.TryGetProperty("requestBody", out _),
            "The query operation should document its JSON request body");
        // The author-provided example from the endpoint's Documentation block flows into the success response
        Assert.Contains("SKU-1001", queryOp.GetRawText());
        Assert.False(stockPath.TryGetProperty("get", out _),
            "A QUERY-only endpoint must not be documented as GET");

        // Generating the document must not have enabled GET at runtime
        var getResponse = await _client.GetAsync("/api/500/Inventory/StockLevels");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, getResponse.StatusCode);
    }
}
