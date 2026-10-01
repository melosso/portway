namespace PortwayApi.Classes.OpenApi;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PortwayApi.Classes;

/// <summary>
/// Adds an optional header parameter per tenancy header to every operation of a tenant endpoint and marks SQL tenant columns readOnly in request bodies
/// </summary>
public sealed class TenancyDocumentFilter : IOpenApiDocumentTransformer
{
    private const string ValuePattern = "^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (document.Paths is null)
            return Task.CompletedTask;

        var tenantEndpoints = OpenApiEndpointCatalog.All().Where(e => e.Definition.HasTenancy).ToList();
        foreach (var (pathKey, pathItem) in document.Paths)
        {
            if (pathItem.Operations is null)
                continue;

            foreach (var (basePath, definition) in tenantEndpoints.Where(e => OpenApiEndpointCatalog.Covers(e.BasePath, pathKey)))
            {
                var tenantProperties = TenantPropertyNames(definition);
                foreach (var operation in pathItem.Operations.Values)
                {
                    MarkReadOnly(operation, tenantProperties);
                    operation.Parameters ??= [];
                    foreach (var header in definition.Tenancy!.Keys)
                    {
                        if (operation.Parameters.Any(p => p.In == ParameterLocation.Header && string.Equals(p.Name, header, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        operation.Parameters.Add(new OpenApiParameter
                        {
                            Name = header,
                            In = ParameterLocation.Header,
                            Required = false,
                            Description = $"Tenant selector. Required when the token holds more than one {header} value.",
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String, Pattern = ValuePattern }
                        });
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    private static HashSet<string> TenantPropertyNames(EndpointDefinition definition)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (definition.Type != EndpointType.SQL)
            return names;
        foreach (var column in definition.Tenancy!.Values)
        {
            names.Add(column);
            var alias = definition.DatabaseToAlias.FirstOrDefault(m => string.Equals(m.Key, column, StringComparison.OrdinalIgnoreCase)).Value;
            if (alias is not null)
                names.Add(alias);
        }
        return names;
    }

    private static void MarkReadOnly(OpenApiOperation operation, HashSet<string> names)
    {
        if (names.Count == 0 || operation.RequestBody?.Content is null)
            return;
        foreach (var media in operation.RequestBody.Content.Values)
        {
            if (media.Schema is not OpenApiSchema { Properties: { } properties })
                continue;
            foreach (var (name, property) in properties)
            {
                if (names.Contains(name) && property is OpenApiSchema schema)
                    schema.ReadOnly = true;
            }
        }
    }
}
