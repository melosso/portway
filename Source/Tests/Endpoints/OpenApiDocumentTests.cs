using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text.Json;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// Verifies OpenAPI document generation end to end; guards the Microsoft.OpenApi upgrade path since Scalar only renders what this endpoint produces
/// </summary>
public class OpenApiDocumentTests : ApiTestBase
{
    [Fact]
    public async Task OpenApiDocument_Generates_And_Parses()
    {
        var response = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("openapi", out var version));
        Assert.StartsWith("3.2", version.GetString());
        Assert.True(root.TryGetProperty("paths", out var paths));
        Assert.True(paths.ValueKind == JsonValueKind.Object);
        Assert.True(root.TryGetProperty("info", out _));
    }

    // An empty requirement object would declare anonymous access
    [Fact]
    public async Task Security_IsRequiredOnceAtRoot_NeverPerOperation()
    {
        var response = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = doc.RootElement;
        var scheme = Assert.Single(root.GetProperty("components").GetProperty("securitySchemes").EnumerateObject()).Name;

        Assert.True(root.TryGetProperty("security", out var security), "root security requirement missing");
        var requirement = Assert.Single(security.EnumerateArray());
        var entry = Assert.Single(requirement.EnumerateObject());
        Assert.Equal(scheme, entry.Name);
        Assert.Equal(0, entry.Value.GetArrayLength());

        var offenders = new List<string>();
        foreach (var path in root.GetProperty("paths").EnumerateObject())
        {
            foreach (var op in path.Value.EnumerateObject())
            {
                var operations = op.Name == "additionalOperations" ? op.Value.EnumerateObject().ToList() : [op];
                offenders.AddRange(operations
                    .Where(o => o.Value.ValueKind == JsonValueKind.Object && o.Value.TryGetProperty("security", out _))
                    .Select(o => $"{o.Name} {path.Name}"));
            }
        }

        Assert.True(offenders.Count == 0, "Operations declaring their own security:\n" + string.Join("\n", offenders));
    }

    // The shared error envelope is registered once as a reusable component schema
    [Fact]
    public async Task SharedErrorResponse_ComponentSchema_IsRegistered()
    {
        var response = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
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

        var response = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var paths = doc.RootElement.GetProperty("paths");

        var query = paths.GetProperty("/api/{env}/Inventory/StockLevels").GetProperty("query");
        var responses = query.GetProperty("responses");

        Assert.Equal("#/components/responses/BadRequest", responses.GetProperty("400").GetProperty("$ref").GetString());
        var badRequest = doc.RootElement.GetProperty("components").GetProperty("responses").GetProperty("BadRequest");
        Assert.Equal("#/components/mediaTypes/ErrorJson",
            badRequest.GetProperty("content").GetProperty("application/json").GetProperty("$ref").GetString());

        // Response summaries are the standard HTTP reason phrase; descriptions explain what it means here
        Assert.Equal("Bad Request", badRequest.GetProperty("summary").GetString());
        Assert.Equal("OK", responses.GetProperty("200").GetProperty("summary").GetString());
        Assert.Contains("validation", badRequest.GetProperty("description").GetString());
    }

    // The shared error envelope is registered once as a reusable media type, not repeated per response
    [Fact]
    public async Task SharedErrorResponse_MediaTypeComponent_IsRegistered()
    {
        var response = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

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

        var response = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
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

            Assert.True(example.TryGetProperty("serializedValue", out var serialized), $"{path} should have serializedValue");
            Assert.False(example.TryGetProperty("value", out _), $"{path} should not also have a JSON value");
            Assert.False(string.IsNullOrWhiteSpace(serialized.GetString()));
        }
    }

    // Static endpoints serve QUERY through the same read path, so filterable ones document it
    [Fact]
    public async Task FilterableStaticEndpoint_DocumentsQueryOperation()
    {
        SetAllowedEnvironments("500", "700", "Synergy");

        var response = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

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
        var response = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var bearer = doc.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");

        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());

        // name and in belong to apiKey schemes; emitting them here would describe a scheme Portway does not use
        Assert.False(bearer.TryGetProperty("name", out _));
        Assert.False(bearer.TryGetProperty("in", out _));
    }

    // Audit: an operation whose summary is just "{METHOD} {name}" means its endpoint never wrote MethodDescriptions
    [Fact]
    public async Task EveryOperation_HasAuthoredSummary()
    {
        SetAllowedEnvironments("500", "700", "Synergy", "WMS");

        var response = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var verbs = new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "MERGE", "QUERY" };
        var offenders = new List<string>();

        foreach (var path in doc.RootElement.GetProperty("paths").EnumerateObject())
            foreach (var op in path.Value.EnumerateObject())
            {
                // Non-standard verbs sit under additionalOperations rather than directly on the path item
                var operations = op.Name == "additionalOperations"
                    ? op.Value.EnumerateObject().Select(o => o.Value)
                    : [op.Value];

                foreach (var operation in operations)
                {
                    if (!operation.TryGetProperty("summary", out var summary)) continue;

                    var text = summary.GetString() ?? "";
                    if (verbs.Any(v => text.StartsWith(v + " ", StringComparison.Ordinal)))
                    {
                        offenders.Add($"{path.Name} {op.Name}: {text}");
                    }
                }
            }

        Assert.True(offenders.Count == 0,
            "Operations falling back to a generated summary; give their endpoint a Documentation.MethodDescriptions entry:\n"
            + string.Join("\n", offenders));
    }

    // Bearer is the only scheme Portway publishes, so the reference UI opens with it selected
    [Fact]
    public async Task ScalarPage_PreselectsBearerScheme()
    {
        var response = await _client.GetAsync("/docs", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"\"preferredSecurityScheme\"\": \"\"Bearer\"\"".Replace("\"\"", "\""), html);
    }

    // the reference page must not reach scalar hosts so a pasted token never shares a page with third party calls
    [Fact]
    public async Task ScalarPage_MakesNoThirdPartyCalls()
    {
        var response = await _client.GetAsync("/docs", TestContext.Current.CancellationToken);
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("connect-src 'self';", csp);
        Assert.Contains("font-src 'self';", csp);

        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var match = System.Text.RegularExpressions.Regex.Match(html, "data-configuration='([^']*)'");
        Assert.True(match.Success);

        using var config = JsonDocument.Parse(match.Groups[1].Value);
        var root = config.RootElement;
        Assert.False(root.GetProperty("withDefaultFonts").GetBoolean());
        Assert.False(root.GetProperty("telemetry").GetBoolean());
        Assert.Equal("never", root.GetProperty("showDeveloperTools").GetString());
        Assert.True(root.GetProperty("agent").GetProperty("disabled").GetBoolean());
        Assert.True(root.GetProperty("mcp").GetProperty("disabled").GetBoolean());
    }

    // the footer script rewrites the powered by link, the vendored bundle must still emit that url
    [Fact]
    public async Task ScalarPage_FooterSelectorMatchesVendoredBundle()
    {
        const string poweredBy = "https://scalar.com/?utm_source=powered-by";
        var html = await _client.GetStringAsync("/docs", TestContext.Current.CancellationToken);
        Assert.Contains($"a[href^=\"{poweredBy}\"]", html);

        var bundle = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "wwwroot", "js", "vendor", "scalar-api-reference.js"), TestContext.Current.CancellationToken);
        Assert.Contains("`https://scalar.com/`", bundle);
        Assert.Contains("`utm_source`,`powered-by`", bundle);
    }

    // scalar draws an empty operations card for namespace groups, the page css hides it
    [Fact]
    public async Task ScalarPage_HidesEmptyOperationsCard()
    {
        var html = await _client.GetStringAsync("/docs", TestContext.Current.CancellationToken);
        var match = System.Text.RegularExpressions.Regex.Match(html, "data-configuration='([^']*)'");
        Assert.True(match.Success);

        using var config = JsonDocument.Parse(match.Groups[1].Value);
        Assert.Contains(".scalar-app .endpoints-card:not(:has(li)) { display: none; }", config.RootElement.GetProperty("customCss").GetString());
    }

    // footer and title come from settings the console can write, so the docs page must encode them
    [Fact]
    public async Task ScalarPage_EncodesTitleAndFooter()
    {
        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenApi:Title"] = "</title><script>alert(1)</script>",
            ["OpenApi:Footer:Text"] = "x'; alert(2); '",
            ["OpenApi:Footer:Url"] = "https://example.com/'+alert(3)+'",
            ["OpenApi:Footer:Target"] = "_blank'</script><script>alert(4)</script>"
        })));
        var html = await factory.CreateClient().GetStringAsync("/docs", TestContext.Current.CancellationToken);

        Assert.DoesNotContain("<script>alert(", html);
        Assert.DoesNotContain("'; alert(2); '", html);
        Assert.DoesNotContain("'+alert(3)+'", html);
    }

    private static JsonDocument ScalarConfig(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html, "data-configuration='([^']*)'");
        Assert.True(match.Success);
        return JsonDocument.Parse(match.Groups[1].Value);
    }

    // code samples are limited to the clients gateway consumers use, curl first
    [Fact]
    public async Task ScalarPage_LimitsCodeSampleClients()
    {
        using var config = ScalarConfig(await _client.GetStringAsync("/docs", TestContext.Current.CancellationToken));
        var root = config.RootElement;

        var hidden = root.GetProperty("hiddenClients").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("ruby", hidden);
        foreach (var shown in new[] { "shell", "csharp", "js", "python", "powershell" })
            Assert.DoesNotContain(shown, hidden);

        Assert.Equal("shell", root.GetProperty("defaultHttpClient").GetProperty("targetKey").GetString());
        Assert.Equal("curl", root.GetProperty("defaultHttpClient").GetProperty("clientKey").GetString());
    }

    // the portway theme is scalar without a theme plus the console colours
    [Fact]
    public async Task ScalarPage_PortwayTheme_UsesConsoleColours()
    {
        using var defaultConfig = ScalarConfig(await _client.GetStringAsync("/docs", TestContext.Current.CancellationToken));
        Assert.NotEqual("none", defaultConfig.RootElement.GetProperty("theme").GetString());
        Assert.DoesNotContain("--scalar-color-accent", defaultConfig.RootElement.GetProperty("customCss").GetString());

        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenApi:ScalarTheme"] = "portway"
        })));
        using var config = ScalarConfig(await factory.CreateClient().GetStringAsync("/docs", TestContext.Current.CancellationToken));
        Assert.Equal("none", config.RootElement.GetProperty("theme").GetString());
        var css = config.RootElement.GetProperty("customCss").GetString();
        Assert.Contains("--scalar-color-accent", css);
        Assert.Contains(".dark-mode", css);
    }

    // every operation has its own url under /docs, the document route stays json
    [Fact]
    public async Task ScalarPage_ServesDeepLinks()
    {
        var response = await _client.GetAsync("/docs/tag/product-stock", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("href=\"/favicon.ico\"", html);

        using var config = ScalarConfig(html);
        Assert.Equal("/docs", config.RootElement.GetProperty("pathRouting").GetProperty("basePath").GetString());

        var spec = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        Assert.Equal("application/json", spec.Content.Headers.ContentType?.MediaType);
    }

    // badges come from the endpoint file and the operation itself, and the setting turns them off
    [Fact]
    public async Task Operations_CarryBadges_UnlessDisabled()
    {
        SetAllowedEnvironments("500", "700");

        static JsonElement Get(JsonDocument doc, string path) => doc.RootElement.GetProperty("paths").GetProperty(path).GetProperty("get");
        static List<string> Badges(JsonElement op) => op.TryGetProperty("x-badges", out var b)
            ? b.EnumerateArray().Select(x => x.GetProperty("name").GetString()!).ToList()
            : [];

        using (var doc = JsonDocument.Parse(await _client.GetStringAsync("/docs/openapi.json", TestContext.Current.CancellationToken)))
        {
            Assert.Equal(["MCP", "OData"], Badges(Get(doc, "/api/{env}/Inventory/ProductStock")));
            Assert.Equal(["OData"], Badges(Get(doc, "/api/{env}/Inventory/Products")));
        }

        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenApi:ShowBadges"] = "false"
        })));
        var json = await factory.CreateClient().GetStringAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        Assert.DoesNotContain("x-badges", json);
    }

    // the operator's own documentation link, only when it is an absolute web url
    [Fact]
    public async Task Document_DeclaresExternalDocs_WhenConfigured()
    {
        using (var doc = JsonDocument.Parse(await _client.GetStringAsync("/docs/openapi.json", TestContext.Current.CancellationToken)))
        {
            Assert.False(doc.RootElement.TryGetProperty("externalDocs", out _));
        }

        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenApi:ExternalDocs:Url"] = "https://docs.example.com/api",
            ["OpenApi:ExternalDocs:Description"] = "Integration guide"
        })));
        using var configured = JsonDocument.Parse(await factory.CreateClient().GetStringAsync("/docs/openapi.json", TestContext.Current.CancellationToken));
        var external = configured.RootElement.GetProperty("externalDocs");
        Assert.Equal("https://docs.example.com/api", external.GetProperty("url").GetString());
        Assert.Equal("Integration guide", external.GetProperty("description").GetString());
    }

    // v1 is the unversioned endpoint document; other pre 0.8 document names redirect to the full document
    [Fact]
    public async Task LegacyVersionedDocumentUrl_KeepsWorking()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var v1 = await client.GetAsync("/docs/openapi/v1/openapi.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, v1.StatusCode);
        Assert.StartsWith("3.2", JsonDocument.Parse(await v1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.GetProperty("openapi").GetString());

        var named = await client.GetAsync("/docs/openapi/2.0/openapi.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.PermanentRedirect, named.StatusCode);
        Assert.Equal("/docs/openapi.json", named.Headers.Location?.OriginalString);
    }

    // markdown for llms is opt in, and off it answers like any missing document
    [Fact]
    public async Task MarkdownDocument_IsOptIn()
    {
        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenApi:MarkdownEnabled"] = "false"
        })));
        var client = factory.CreateClient();

        var off = await client.GetAsync("/docs/openapi.md", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, off.StatusCode);
        Assert.Equal("application/json", off.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("openapi.md", await client.GetStringAsync("/docs/openapi.json", TestContext.Current.CancellationToken));
    }

    // the markdown is rendered from the same document, grouped by namespace
    [Fact]
    public async Task MarkdownDocument_RendersTheDocument()
    {
        SetAllowedEnvironments("500", "700");
        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenApi:MarkdownEnabled"] = "true",
            ["OpenApi:Title"] = "Gateway Reference"
        })));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/docs/openapi.md", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/markdown", response.Content.Headers.ContentType?.MediaType);

        var markdown = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.StartsWith("# Gateway Reference", markdown);
        Assert.Contains("\n## Inventory\n", markdown);
        Assert.Contains("`GET /api/{env}/Inventory/Products`", markdown);
        Assert.Contains("| `$filter` | query |", markdown);
        Assert.Contains("\n## Errors\n", markdown);
        Assert.Contains("- `401` Unauthorized: The bearer token is missing", markdown);

        var json = await client.GetStringAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        Assert.Contains("[View as Markdown](/docs/openapi.md)", json);
    }

    // The document names its own URI so other descriptions can reference it
    [Fact]
    public async Task Document_DeclaresSelfUri()
    {
        var response = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var self = doc.RootElement.GetProperty("$self").GetString();
        Assert.NotNull(self);
        Assert.EndsWith("/docs/openapi.json", self);
        Assert.True(Uri.IsWellFormedUriString(self, UriKind.Absolute));
    }

    // Only UseForwardedHeaders may change the scheme, and it trusts no proxy here
    [Fact]
    public async Task UntrustedForwardedProto_DoesNotChangeTheServerUrl()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/docs/openapi.json");
        request.Headers.Add("X-Forwarded-Proto", "https");

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.StartsWith("http://", doc.RootElement.GetProperty("servers")[0].GetProperty("url").GetString());
        Assert.StartsWith("http://", doc.RootElement.GetProperty("$self").GetString());
    }

    // Audit: no operation may inline its own error schema, whatever endpoint type produced it
    [Fact]
    public async Task EveryErrorResponse_UsesTheSharedEnvelope()
    {
        SetAllowedEnvironments("500", "700", "Synergy", "WMS");

        var response = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var shared = doc.RootElement.GetProperty("components").GetProperty("responses");

        var offenders = new List<string>();
        foreach (var (path, method, operation) in Operations(doc.RootElement))
            foreach (var r in operation.GetProperty("responses").EnumerateObject())
            {
                if (!int.TryParse(r.Name, out var code) || code < 400) continue;

                var reference = r.Value.TryGetProperty("$ref", out var refValue) ? refValue.GetString() : null;
                var media = reference is not null && reference.StartsWith("#/components/responses/") &&
                            shared.TryGetProperty(reference["#/components/responses/".Length..], out var component)
                    ? component.GetProperty("content").GetProperty("application/json").GetProperty("$ref").GetString()
                    : null;

                if (media != "#/components/mediaTypes/ErrorJson" && media != "#/components/mediaTypes/ValidationErrorJson")
                {
                    offenders.Add($"{path} {method} {r.Name}: {reference ?? "inline"}");
                }
            }

        Assert.True(offenders.Count == 0, "Error responses not using a shared response:\n" + string.Join("\n", offenders));

        var challenge = shared.GetProperty("Unauthorized").GetProperty("headers");
        Assert.True(challenge.TryGetProperty("WWW-Authenticate", out _), "401 declares its Bearer challenge");
    }

    // Multipart uploads describe the part encoding, narrowed to the extensions the endpoint allows
    [Fact]
    public async Task FileUpload_DocumentsMultipartEncoding_FromAllowedExtensions()
    {
        SetAllowedEnvironments("500", "700");

        var response = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

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

        var response = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var paths = doc.RootElement.GetProperty("paths");

        // Inventory/Products declares an Assortment navigation; Inventory/ProductStock declares none
        var expand = GetQueryParameter(paths, "/api/{env}/Inventory/Products", "$expand");
        Assert.NotNull(expand);
        // Optional parameters are serialized without a "required" key, so it stays unchecked in the UI
        Assert.False(expand.Value.TryGetProperty("required", out var required) && required.GetBoolean());
        Assert.Equal("string", expand.Value.GetProperty("schema").GetProperty("type").GetString());
        Assert.Contains("Assortment", expand.Value.GetProperty("description").GetString());

        Assert.Null(GetQueryParameter(paths, "/api/{env}/Inventory/ProductStock", "$expand"));
        Assert.NotNull(GetQueryParameter(paths, "/api/{env}/Inventory/ProductStock", "$select"));
    }

    // Ids derive from the route, so adding an endpoint never renames the operations of another
    [Fact]
    public async Task OperationIds_AreStable_UniqueAndDerivedFromTheRoute()
    {
        SetAllowedEnvironments("500", "700", "Synergy", "WMS");

        var response = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var ids = Operations(doc.RootElement)
            .ToDictionary(o => $"{o.Method} {o.Path}", o => o.Operation.GetProperty("operationId").GetString()!);

        Assert.All(ids.Values, id => Assert.Matches("^[a-zA-Z]+_[A-Za-z0-9_]+$", id));
        Assert.Equal(ids.Count, ids.Values.Distinct().Count());

        Assert.Equal("get_WMS_Bins", ids["get /api/{env}/WMS/Bins"]);
        Assert.Equal("merge_WMS_Bins", ids["MERGE /api/{env}/WMS/Bins"]);
        Assert.Equal("delete_WMS_Bins", ids["delete /api/{env}/WMS/Bins({id})"]);
        Assert.Equal("query_Inventory_StockLevels", ids["query /api/{env}/Inventory/StockLevels"]);
        Assert.Equal("get_Masterdata_CostCenters", ids["get /api/{env}/Masterdata/CostCenters"]);
        Assert.Equal("query_Masterdata_CostCenters", ids["query /api/{env}/Masterdata/CostCenters"]);
        Assert.Equal("post_Webhooks_Incoming", ids["post /api/{env}/Webhooks/Incoming/{webhookId}"]);

        // ids that were already stable keep their value
        Assert.Equal("get_CRM_Accounts", ids["get /api/{env}/CRM/Accounts"]);
        Assert.Equal("delete_CRM_Accounts", ids["delete /api/{env}/CRM/Accounts/{id}"]);
        Assert.Equal("composite_Sales_Orders", ids["post /api/{env}/Sales/Orders"]);
        Assert.Equal("uploadFile_Images", ids["post /api/{env}/files/Images"]);
    }

    // SqlRequestHandler serves Bins(1) as one record, so the document offers it where GET is allowed
    [Fact]
    public async Task SqlGetById_IsDocumented_ForTablesAndViews()
    {
        SetAllowedEnvironments("500", "700", "Synergy", "WMS");

        var response = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var paths = doc.RootElement.GetProperty("paths");

        var byId = paths.GetProperty("/api/{env}/WMS/Bins({id})").GetProperty("get");
        Assert.Equal("get_WMS_Bins_byId", byId.GetProperty("operationId").GetString());
        var parameters = byId.GetProperty("parameters").EnumerateArray().ToList();
        Assert.Contains(parameters, p => p.GetProperty("name").GetString() == "id" && p.GetProperty("in").GetString() == "path");
        Assert.DoesNotContain(parameters, p => p.GetProperty("name").GetString()!.StartsWith('$'));

        var responses = byId.GetProperty("responses");
        Assert.True(responses.TryGetProperty("404", out _));
        var schema = responses.GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.False(schema.TryGetProperty("properties", out var props) && props.TryGetProperty("value", out _),
            "a single record is returned without the collection envelope");

        // GET-only endpoints get the id path too; table valued functions have no key
        Assert.True(paths.GetProperty("/api/{env}/WMS/Warehouses({id})").TryGetProperty("get", out _));
        Assert.False(paths.TryGetProperty("/api/{env}/Company/Departments({id})", out _));
    }

    // $count adds totalCount to the body; the pagination headers are the ones SqlRequestHandler sends
    [Fact]
    public async Task SqlGet_DocumentsCount_AndTheHeadersItSends()
    {
        SetAllowedEnvironments("500", "700", "Synergy", "WMS");

        var response = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var paths = doc.RootElement.GetProperty("paths");

        var count = GetQueryParameter(paths, "/api/{env}/WMS/Bins", "$count");
        Assert.NotNull(count);
        Assert.Equal("boolean", count.Value.GetProperty("schema").GetProperty("type").GetString());
        Assert.Null(GetQueryParameter(paths, "/api/{env}/Company/Departments", "$count"));

        var ok = paths.GetProperty("/api/{env}/WMS/Bins").GetProperty("get").GetProperty("responses").GetProperty("200");
        var headers = ok.GetProperty("headers").EnumerateObject().Select(h => h.Name).ToList();
        Assert.Contains("X-Has-More", headers);
        Assert.Contains("X-Returned-Count", headers);
        Assert.DoesNotContain("X-Has-More-Results", headers);
        Assert.DoesNotContain("X-Total-Count", headers);
        Assert.Contains("totalCount", ok.GetProperty("content").GetProperty("application/json").GetRawText());

        var list = paths.GetProperty("/api/{env}/files/Images/list").GetProperty("get").GetProperty("responses").GetProperty("200");
        var listHeaders = list.GetProperty("headers").EnumerateObject().Select(h => h.Name).ToList();
        Assert.Contains("X-Has-More", listHeaders);
        Assert.DoesNotContain("X-Has-More-Results", listHeaders);
    }

    private static IEnumerable<(string Path, string Method, JsonElement Operation)> Operations(JsonElement root)
    {
        foreach (var path in root.GetProperty("paths").EnumerateObject())
        {
            foreach (var entry in path.Value.EnumerateObject())
            {
                if (entry.Name == "additionalOperations")
                {
                    foreach (var extra in entry.Value.EnumerateObject())
                        yield return (path.Name, extra.Name, extra.Value);
                }
                else if (entry.Value.ValueKind == JsonValueKind.Object && entry.Value.TryGetProperty("responses", out _))
                {
                    yield return (path.Name, entry.Name, entry.Value);
                }
            }
        }
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

    // Audit: every documented status code's description equals its canonical HTTP reason phrase
    [Fact]
    public async Task AllResponseDescriptions_AreStandardized()
    {
        SetAllowedEnvironments("500", "700", "Synergy", "WMS");

        var response = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var shared = doc.RootElement.GetProperty("components").GetProperty("responses");

        var offenders = new List<string>();
        foreach (var (path, method, operation) in Operations(doc.RootElement))
            foreach (var r in operation.GetProperty("responses").EnumerateObject())
            {
                if (!int.TryParse(r.Name, out var code)) continue;
                var expected = PortwayApi.Classes.OpenApi.StandardResponses.DescriptionFor(code);
                if (expected == null) continue;
                var resolved = r.Value.TryGetProperty("$ref", out var reference)
                    ? shared.GetProperty(reference.GetString()!["#/components/responses/".Length..])
                    : r.Value;
                if (!resolved.TryGetProperty("description", out var d) || d.GetString() != expected)
                {
                    offenders.Add($"{path} {method} {r.Name}: '{d.GetString()}' (expected '{expected}')");
                }
            }

        Assert.True(offenders.Count == 0, "Non-standardized response descriptions:\n" + string.Join("\n", offenders));
    }

    // Regression: QUERY-only endpoints are not documented as GET or mutated
    [Fact]
    public async Task QueryOnlyEndpoint_NotRenderedAsGet_AndGetStays405()
    {
        SetAllowedEnvironments("500", "700");

        // Generate the OpenAPI document (runs the document filter over the live endpoint definitions)
        var docResponse = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, docResponse.StatusCode);

        using var doc = JsonDocument.Parse(await docResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var paths = doc.RootElement.GetProperty("paths");

        // The QUERY-only endpoint is documented as a native OpenAPI 3.2 query operation, never as GET
        Assert.True(paths.TryGetProperty("/api/{env}/Inventory/StockLevels", out var stockPath),
            "QUERY-only endpoint should be present in the 3.2 document");
        Assert.True(stockPath.TryGetProperty("query", out var queryOp),
            "A QUERY-only endpoint must be documented as a query operation");
        Assert.True(queryOp.TryGetProperty("requestBody", out _),
            "The query operation should document its JSON request body");
        // The author-provided example from the endpoint's Documentation block is copied into the success response
        Assert.Contains("SKU-1001", queryOp.GetRawText());
        Assert.False(stockPath.TryGetProperty("get", out _),
            "A QUERY-only endpoint must not be documented as GET");

        // Generating the document must not have enabled GET at runtime
        var getResponse = await _client.GetAsync("/api/500/Inventory/StockLevels", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, getResponse.StatusCode);
    }

    [Fact]
    public async Task TvfQueryParameterDocumented()
    {
        var response = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var parameters = json.RootElement.GetProperty("paths").GetProperty("/api/{env}/Company/Departments").GetProperty("get").GetProperty("parameters").EnumerateArray().ToList();

        Assert.Single(parameters, p => p.GetProperty("name").GetString() == "BaseDate" && p.GetProperty("in").GetString() == "query");
    }
}
