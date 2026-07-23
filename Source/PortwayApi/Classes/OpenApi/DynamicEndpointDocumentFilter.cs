using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using Serilog;

namespace PortwayApi.Classes;

public partial class DynamicEndpointDocumentFilter : IOpenApiDocumentTransformer
{
    private readonly ILogger<DynamicEndpointDocumentFilter> _logger;

    public DynamicEndpointDocumentFilter(ILogger<DynamicEndpointDocumentFilter> logger)
    {
        _logger = logger;
    }

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        _logger.LogDebug("DynamicEndpointDocumentFilter called");

        try {
            // Remove any controller-discovered paths that we'll be replacing
            RemoveConflictingPaths(document);

            // Generate unique operation IDs
            int operationIdCounter = 1;

            // Collect all tags with descriptions for the document
            var documentTags = new Dictionary<string, string>();

            // Add documentation for each endpoint type
            AddSqlEndpoints(document, ref operationIdCounter, documentTags);
            AddProxyEndpoints(document, ref operationIdCounter, documentTags);
            AddWebhookEndpoints(document, ref operationIdCounter);
            AddStaticEndpoints(document, ref operationIdCounter, documentTags);

            // Collect file endpoint tags (but don't create operations since they're handled by EndpointController)
            CollectFileEndpointTags(documentTags);

            // Add all collected tags to the document with descriptions
            AddTagsToDocument(document, documentTags);


            // Ensure application/json is added automatically to all operations with request bodies
            foreach (var path in document.Paths)
            {
                if (path.Value.Operations == null) continue;
                foreach (var operation in path.Value.Operations)
                {
                    if (operation.Value.RequestBody?.Content != null && !operation.Value.RequestBody.Content.ContainsKey("application/json"))
                    {
                        operation.Value.RequestBody.Content["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema { Type = JsonSchemaType.Object }
                        };
                    }
                }
            }

            return Task.CompletedTask;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error applying document filter");
            return Task.CompletedTask;
        }
    }

    private void RemoveConflictingPaths(OpenApiDocument document)
    {
        var pathsToRemove = document.Paths
            .Where(p => p.Key.Contains("{catchall}") || p.Key.Contains("api/{env}"))
            .Select(p => p.Key)
            .ToList();

        foreach (var path in pathsToRemove)
        {
            document.Paths.Remove(path);
        }
    }

    private void AddOperationToPath(IOpenApiPathItem pathItem, string method, OpenApiOperation operation)
    {
        var operationType = GetOperationType(method);
        if (operationType != null && pathItem.Operations != null)
        {
            pathItem.Operations[operationType] = operation;
        }
    }

    /// <summary>Gets the appropriate OpenAPI schema type for a given content type</summary>
    private static OpenApiSchema GetSchemaForContentType(string contentType)
    {
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            return new OpenApiSchema { Type = JsonSchemaType.Object };

        return new OpenApiSchema { Type = JsonSchemaType.String };
    }

    /// <summary>Gets the operation summary, using custom summary if available or default format</summary>
    private string GetOperationSummary(string method, string endpointName, EndpointDefinition definition)
    {
        // Check if there's a custom summary for this method in MethodDescriptions
        if (definition.Documentation?.MethodDescriptions?.TryGetValue(method.ToUpper(), out var customSummary) == true
            && !string.IsNullOrWhiteSpace(customSummary))
        {
            return customSummary;
        }

        // Use Documentation.Summary if available for better descriptions
        if (!string.IsNullOrWhiteSpace(definition.Documentation?.Summary))
        {
            var summary = definition.Documentation.Summary.Trim();

            // Check if summary already starts with an action verb - if so, use it as-is for GET
            var startsWithActionVerb = summary.StartsWith("Retrieve", StringComparison.OrdinalIgnoreCase) ||
                                     summary.StartsWith("Get", StringComparison.OrdinalIgnoreCase) ||
                                     summary.StartsWith("Fetch", StringComparison.OrdinalIgnoreCase) ||
                                     summary.StartsWith("Load", StringComparison.OrdinalIgnoreCase);

            return method.ToUpper() switch
            {
                "GET" => startsWithActionVerb ? summary : $"Retrieve {summary.ToLower()}",
                "POST" => $"Create new {summary.ToLower()}",
                "PUT" => $"Update {summary.ToLower()}",
                "PATCH" => $"Partially update {summary.ToLower()}",
                "DELETE" => $"Delete {summary.ToLower()}",
                _ => $"{method} {summary}"
            };
        }

        // Return default format using DisplayName if available
        return $"{method} {endpointName}";
    }

    /// <summary>Gets the operation description, using custom description if available or default format</summary>
    private string GetOperationDescription(string method, string endpointName, EndpointDefinition definition)
    {
        // Check if there's a custom detailed description for this method in MethodDocumentation
        if (definition.Documentation?.MethodDocumentation?.TryGetValue(method.ToUpper(), out var customDocumentation) == true
            && !string.IsNullOrWhiteSpace(customDocumentation))
        {
            return customDocumentation;
        }

        // Use Documentation.Description if available for better descriptions
        if (!string.IsNullOrWhiteSpace(definition.Documentation?.Description))
        {
            return definition.Documentation.Description;
        }

        // Return default description based on endpoint type
        if (definition.IsSql)
        {
            return $"{method} operation for entity {definition.DatabaseObjectName}";
        }
        else if (definition.Type == EndpointType.Files)
        {
            return method.ToUpper() switch
            {
                "GET" => $"Download content from the {endpointName} storage endpoint",
                "POST" => $"Upload content to the {endpointName} storage endpoint",
                _ => $"{method} operation for storage endpoint {endpointName}"
            };
        }
        else
        {
            return $"Proxies {method} requests to an internal webservice.";
        }
    }

    /// <summary>Gets the operation description for static endpoints, using custom description if available or default format</summary>
    private string GetStaticOperationDescription(string method, string endpointName, EndpointDefinition definition, string contentType)
    {
        // Check if there's a custom description for this method
        if (definition.Documentation?.MethodDocumentation?.TryGetValue(method.ToUpper(), out var customDescription) == true
            && !string.IsNullOrWhiteSpace(customDescription))
        {
            return customDescription;
        }

        // Return default description for static endpoints
        return $"Returns {contentType} content from the {endpointName} endpoint.";
    }

    /// <summary>Adds all collected tags with descriptions to the OpenAPI document</summary>
    private void AddTagsToDocument(OpenApiDocument document, Dictionary<string, string> documentTags)
    {
        // Initialize tags collection if it doesn't exist
        document.Tags ??= new HashSet<OpenApiTag>();

        // Add each tag with its description (sorting will be handled by AlphabeticalEndpointSorter)
        foreach (var tagEntry in documentTags)
        {
            var existingTag = document.Tags.FirstOrDefault(t => string.Equals(t.Name, tagEntry.Key, StringComparison.OrdinalIgnoreCase));
            if (existingTag == null)
            {
                document.Tags.Add(new OpenApiTag
                {
                    Name = tagEntry.Key,
                    Description = tagEntry.Value
                });
            }
            else
            {
                // Update existing tag description if it's empty
                if (string.IsNullOrWhiteSpace(existingTag.Description))
                {
                    existingTag.Description = tagEntry.Value;
                }
            }
        }

        // Webhook tag descriptions are registered per endpoint in AddWebhookEndpoints (namespaced since v2.0.0)

        // Sort all tags alphabetically
        document.Tags = new HashSet<OpenApiTag>(document.Tags.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Load webhook documentation from entity.json file</summary>
    private Documentation? LoadWebhookDocumentation()
    {
        try
        {
            string webhookEntityPath = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Webhooks", "entity.json");
            if (!File.Exists(webhookEntityPath))
            {
                _logger.LogWarning("Webhook entity.json not found at: {Path}", webhookEntityPath);
                return null;
            }

            string json = File.ReadAllText(webhookEntityPath);
            var webhookEntity = JsonSerializer.Deserialize<WebhookEntity>(json);
            return webhookEntity?.Documentation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading webhook documentation");
            return null;
        }
    }

    // Helper classes for deserializing entity.json
    private class WebhookEntity
    {
        public Documentation? Documentation { get; set; }
    }

    // Match the classes used in EnvironmentSettings
    private class SettingsModel
    {
        public EnvironmentModel Environment { get; set; } = new EnvironmentModel();
    }

    private class EnvironmentModel
    {
        public string ServerName { get; set; } = ".";
        public List<string> AllowedEnvironments { get; set; } = new List<string>();
    }
}
