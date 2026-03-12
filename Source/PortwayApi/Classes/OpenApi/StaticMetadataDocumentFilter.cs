using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Serilog;

namespace PortwayApi.Classes.OpenApi;

/// <summary>
/// Document transformer that enriches static JSON, CSV, and XML endpoint documentation by analyzing actual content files
/// while generating randomized mock data for examples to prevent data leakage.
/// </summary>
public class StaticMetadataDocumentFilter : IOpenApiDocumentTransformer
{
    private const long MaxFileSizeToAnalyze = 1 * 1024 * 1024; // 1 MB limit for analysis

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        try
        {
            EnrichStaticEndpointsWithMetadata(document);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error applying static metadata to OpenAPI documentation");
        }

        return Task.CompletedTask;
    }

    private void EnrichStaticEndpointsWithMetadata(OpenApiDocument document)
    {
        var staticEndpoints = EndpointHandler.GetStaticEndpoints();

        foreach (var endpoint in staticEndpoints)
        {
            var endpointName = endpoint.Key;
            var definition = endpoint.Value;

            if (definition.IsPrivate)
                continue;

            // Resolve the physical path to the content file
            var contentFilePath = ResolveContentFilePath(definition, endpointName);
            if (string.IsNullOrEmpty(contentFilePath) || !File.Exists(contentFilePath))
            {
                Log.Debug("Content file not found for static endpoint {EndpointName}: {FilePath}", endpointName, contentFilePath);
                continue;
            }

            // Check file size
            var fileInfo = new FileInfo(contentFilePath);
            if (fileInfo.Length > MaxFileSizeToAnalyze)
            {
                Log.Debug("Content file too large for static analysis: {FilePath} ({Size} bytes)", contentFilePath, fileInfo.Length);
                continue;
            }

            var contentType = definition.Properties?.GetValueOrDefault("ContentType", "text/plain")?.ToString() ?? "text/plain";

            try
            {
                if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
                {
                    EnrichJsonEndpoint(document, definition, contentType, contentFilePath, endpointName);
                }
                else if (contentType.Contains("csv", StringComparison.OrdinalIgnoreCase))
                {
                    EnrichCsvEndpoint(document, definition, contentType, contentFilePath, endpointName);
                }
                else if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
                {
                    EnrichXmlEndpoint(document, definition, contentType, contentFilePath, endpointName);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to parse static {Format} content for endpoint {EndpointName}: {Message}", contentType, endpointName, ex.Message);
            }
        }
    }

    private void EnrichJsonEndpoint(OpenApiDocument document, EndpointDefinition definition, string contentType, string filePath, string endpointName)
    {
        var jsonContent = File.ReadAllText(filePath);
        var jsonNode = JsonNode.Parse(jsonContent);
        if (jsonNode == null) return;

        var schema = CreateSchemaFromJsonNode(jsonNode);
        var example = CreateMockFromJsonNode(jsonNode);

        UpdateEndpointDocumentation(document, definition, contentType, schema, example);
        Log.Debug("Enriched documentation for static JSON endpoint {EndpointName} with randomized mock data", endpointName);
    }

    private void EnrichCsvEndpoint(OpenApiDocument document, EndpointDefinition definition, string contentType, string filePath, string endpointName)
    {
        var header = File.ReadLines(filePath).FirstOrDefault();
        if (string.IsNullOrEmpty(header)) return;

        // Auto-detect delimiter (comma or semicolon)
        char delimiter = header.Contains(';') ? ';' : ',';
        var columns = header.Split(delimiter).Select(c => c.Trim().Replace("\"", "")).ToList();

        // Create a schema representing an array of objects with these columns
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Array,
            Description = "A list of records from the CSV file",
            Items = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Properties = columns.ToDictionary(
                    col => col,
                    _ => (IOpenApiSchema)new OpenApiSchema { Type = JsonSchemaType.String }
                )
            }
        };

        // Create randomized mock CSV example
        var headerLine = string.Join(delimiter.ToString(), columns);
        var mockRows = Enumerable.Range(0, 3).Select(_ => MockDataGenerator.GenerateCsvRow(columns, delimiter));
        var exampleContent = $"{headerLine}\n{string.Join("\n", mockRows)}";
        var example = JsonValue.Create(exampleContent);

        UpdateEndpointDocumentation(document, definition, contentType, schema, example);
        Log.Debug("Enriched documentation for static CSV endpoint {EndpointName} with randomized mock data", endpointName);
    }

    private void EnrichXmlEndpoint(OpenApiDocument document, EndpointDefinition definition, string contentType, string filePath, string endpointName)
    {
        var xmlContent = File.ReadAllText(filePath);
        var xdoc = XDocument.Parse(xmlContent);
        if (xdoc.Root == null) return;

        var schema = CreateSchemaFromXElement(xdoc.Root);
        var mockXDoc = CreateMockFromXElement(xdoc.Root);
        var example = JsonValue.Create(mockXDoc.ToString());

        UpdateEndpointDocumentation(document, definition, contentType, schema, example);
        Log.Debug("Enriched documentation for static XML endpoint {EndpointName} with randomized mock data", endpointName);
    }

    private XElement CreateMockFromXElement(XElement element)
    {
        var mockElement = new XElement(element.Name);

        // Copy attributes with randomized mock values
        foreach (var attr in element.Attributes())
        {
            var mockVal = MockDataGenerator.GenerateValue(attr.Name.LocalName, JsonValueKind.String);
            mockElement.SetAttributeValue(attr.Name, mockVal?.ToString() ?? "example");
        }

        // Handle simple text content
        if (!element.HasElements && !string.IsNullOrWhiteSpace(element.Value))
        {
            var mockVal = MockDataGenerator.GenerateValue(element.Name.LocalName, JsonValueKind.String);
            mockElement.Value = mockVal?.ToString() ?? "example_text";
            return mockElement;
        }

        // Recursively add mock children
        var childGroups = element.Elements().GroupBy(e => e.Name.LocalName);
        foreach (var group in childGroups)
        {
            var first = group.First();
            mockElement.Add(CreateMockFromXElement(first));
            
            if (group.Count() > 1)
            {
                // Add one more to represent a collection with unique data
                mockElement.Add(CreateMockFromXElement(first));
            }
        }

        return mockElement;
    }

    private JsonNode? CreateMockFromJsonNode(JsonNode node, string? propertyName = null)
    {
        if (node is JsonArray array)
        {
            var mockArray = new JsonArray();
            if (array.Count > 0 && array[0] != null)
            {
                // Create two items with unique randomized data
                mockArray.Add(CreateMockFromJsonNode(array[0]!, propertyName));
                mockArray.Add(CreateMockFromJsonNode(array[0]!, propertyName));
            }
            return mockArray;
        }

        if (node is JsonObject obj)
        {
            var mockObj = new JsonObject();
            foreach (var property in obj)
            {
                if (property.Value != null)
                {
                    mockObj[property.Key] = CreateMockFromJsonNode(property.Value, property.Key);
                }
            }
            return mockObj;
        }

        if (node is JsonValue value)
        {
            var element = value.GetValue<JsonElement>();
            return MockDataGenerator.GenerateValue(propertyName, element.ValueKind);
        }

        return MockDataGenerator.GenerateValue(propertyName);
    }

    private OpenApiSchema CreateSchemaFromXElement(XElement element)
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description = $"XML element: {element.Name.LocalName}",
            Properties = new Dictionary<string, IOpenApiSchema>()
        };

        // Add XML namespace metadata for best-practice OpenAPI design
        if (!string.IsNullOrEmpty(element.Name.NamespaceName) && Uri.TryCreate(element.Name.NamespaceName, UriKind.RelativeOrAbsolute, out var nsUri))
        {
            schema.Xml = new OpenApiXml
            {
                Namespace = nsUri,
                Prefix = element.GetPrefixOfNamespace(element.Name.Namespace)
            };
        }

        foreach (var attr in element.Attributes())
        {
            // Attributes in OpenAPI XML need to be marked as such
            schema.Properties[$"@{attr.Name.LocalName}"] = new OpenApiSchema 
            { 
                Type = JsonSchemaType.String,
                Xml = new OpenApiXml { Attribute = true, Name = attr.Name.LocalName }
            };
        }

        var childGroups = element.Elements().GroupBy(e => e.Name.LocalName);
        foreach (var group in childGroups)
        {
            var first = group.First();
            var itemSchema = CreateSchemaFromXElement(first);
            
            if (group.Count() > 1)
            {
                schema.Properties[group.Key] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Items = itemSchema
                };
            }
            else
            {
                schema.Properties[group.Key] = itemSchema;
            }
        }

        if (!element.HasElements && !string.IsNullOrWhiteSpace(element.Value))
        {
            return new OpenApiSchema { Type = JsonSchemaType.String, Description = $"Value of {element.Name.LocalName}" };
        }

        return schema;
    }

    private string ResolveContentFilePath(EndpointDefinition endpoint, string endpointName)
    {
        string endpointPath;
        if (endpoint.HasNamespace)
        {
            endpointPath = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Static",
                endpoint.EffectiveNamespace!, endpoint.FolderName ?? endpointName);
        }
        else
        {
            endpointPath = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Static", endpointName);
        }

        if (endpoint.Properties?.TryGetValue("ContentFile", out var contentFileObj) == true)
        {
            var contentFile = contentFileObj.ToString()!;
            return Path.Combine(endpointPath, contentFile);
        }

        return string.Empty;
    }

    private void UpdateEndpointDocumentation(
        OpenApiDocument document, 
        EndpointDefinition definition, 
        string contentType,
        OpenApiSchema schema,
        JsonNode? example)
    {
        var path = $"/api/{{env}}/{definition.FullPath}";
        if (!document.Paths.ContainsKey(path)) return;

        var pathItem = document.Paths[path];
        if (pathItem.Operations == null || !pathItem.Operations.TryGetValue(HttpMethod.Get, out var getOperation))
            return;

        if (getOperation.Responses?.TryGetValue("200", out var response) == true)
        {
            // Clear existing content to ensure we replace placeholders
            // Note: Content is a read-only dictionary, so we use Clear()
            response.Content?.Clear();
            
            var mediaType = new OpenApiMediaType
            {
                Schema = schema
            };

            if (example != null)
            {
                mediaType.Examples = new Dictionary<string, IOpenApiExample>
                {
                    ["mock_data"] = new OpenApiExample
                    {
                        Summary = "Randomized mock example (not real content)",
                        Value = example
                    }
                };
            }

            // Ensure Content is not null before adding
            if (response.Content == null)
            {
                return;
            }

            response.Content[contentType] = mediaType;
        }
    }

    private OpenApiSchema CreateSchemaFromJsonNode(JsonNode node)
    {
        if (node is JsonArray array)
        {
            var schema = new OpenApiSchema
            {
                Type = JsonSchemaType.Array
            };

            if (array.Count > 0 && array[0] != null)
            {
                schema.Items = CreateSchemaFromJsonNode(array[0]!);
            }
            else
            {
                schema.Items = new OpenApiSchema { Type = JsonSchemaType.Object };
            }

            return schema;
        }

        if (node is JsonObject obj)
        {
            var schema = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Properties = new Dictionary<string, IOpenApiSchema>()
            };

            foreach (var property in obj)
            {
                if (property.Value != null)
                {
                    schema.Properties[property.Key] = CreateSchemaFromJsonNode(property.Value);
                }
            }

            return schema;
        }

        if (node is JsonValue value)
        {
            var element = value.GetValue<JsonElement>();
            return element.ValueKind switch
            {
                JsonValueKind.String => new OpenApiSchema { Type = JsonSchemaType.String },
                JsonValueKind.Number => new OpenApiSchema { Type = JsonSchemaType.Number },
                JsonValueKind.True or JsonValueKind.False => new OpenApiSchema { Type = JsonSchemaType.Boolean },
                JsonValueKind.Null => new OpenApiSchema { Type = JsonSchemaType.Null },
                _ => new OpenApiSchema { Type = JsonSchemaType.String }
            };
        }

        return new OpenApiSchema { Type = JsonSchemaType.Object };
    }
}