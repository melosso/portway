using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Serilog;

namespace PortwayApi.Classes;

/// <summary>
/// Document transformer that ensures endpoints are sorted alphabetically by path
/// rather than grouped by tag
/// </summary>
public class AlphabeticalEndpointSorter : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var paths = document.Paths.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value);

        document.Paths.Clear();
        foreach (var path in paths)
        {
            document.Paths.Add(path.Key, path.Value);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Helps fix issues with catch-all route parameters in OpenAPI documentation
/// </summary>
public class OpenApiDocumentFilter : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        // Remove any catch-all route paths that can cause conflicts
        var paths = document.Paths.Keys.Where(p => p.Contains("{**")).ToList();
        foreach (var path in paths)
        {
            document.Paths.Remove(path);
        }

        // Remove any duplicate paths that might exist
        var duplicateKeys = document.Paths.Keys
            .GroupBy(path => path)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        foreach (var key in duplicateKeys)
        {
            document.Paths.Remove(key);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Custom document transformer to properly handle catchall parameters
/// </summary>
public class OpenApiCatchAllParameterFilter : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        // Find and remove any path with {**catchall} in it, as these are causing conflicts
        var pathsToRemove = document.Paths.Keys
            .Where(p => p.Contains("{**catchall}"))
            .ToList();

        foreach (var path in pathsToRemove)
        {
            Log.Debug($"Removing catchall path from OpenAPI docs: {path}");
            document.Paths.Remove(path);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Custom operation transformer to add security requirements to all operations
/// </summary>
public class OpenApiOperationFilter : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        // Skip if this is an OpenAPI docs operation
        if (context.Description.RelativePath?.StartsWith("openapi-docs") == true)
        {
            return Task.CompletedTask;
        }

        // Add security requirements to all operations
        operation.Security ??= new List<OpenApiSecurityRequirement>();
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecuritySchemeReference("Bearer"),
                new List<string>()
            }
        });

        // Add standard response codes
        operation.Responses ??= new OpenApiResponses();

        if (!operation.Responses.ContainsKey("401"))
            operation.Responses.Add("401", new OpenApiResponse { Description = "Unauthorized" });

        if (!operation.Responses.ContainsKey("403"))
            operation.Responses.Add("403", new OpenApiResponse { Description = "Forbidden" });

        if (!operation.Responses.ContainsKey("500"))
            operation.Responses.Add("500", new OpenApiResponse { Description = "Server Error" });

        return Task.CompletedTask;
    }
}
