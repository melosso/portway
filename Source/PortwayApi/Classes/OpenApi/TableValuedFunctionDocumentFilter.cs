namespace PortwayApi.Classes.OpenApi;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PortwayApi.Classes;
using PortwayApi.Helpers;

/// <summary>
/// Documents Query and Header function parameters on table-valued function endpoints; parameters named in Tenancy are left to TenancyDocumentFilter
/// </summary>
public sealed class TableValuedFunctionDocumentFilter : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (document.Paths is null)
            return Task.CompletedTask;

        var functions = OpenApiEndpointCatalog.All()
            .Where(e => e.Definition.IsSql && SqlTableValuedFunctionHelper.IsTableValuedFunction(e.Definition) && e.Definition.FunctionParameters is { Count: > 0 })
            .ToList();

        foreach (var (pathKey, pathItem) in document.Paths)
        {
            if (pathItem.Operations is null)
                continue;

            foreach (var (_, definition) in functions.Where(e => OpenApiEndpointCatalog.Covers(e.BasePath, pathKey)))
            {
                var tenantTargets = definition.Tenancy?.Values.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
                var documented = definition.FunctionParameters!
                    .Where(p => !tenantTargets.Contains(p.Name))
                    .Select(p => (Parameter: p, Location: p.Source.ToLowerInvariant() switch
                    {
                        "query" => ParameterLocation.Query,
                        "header" => ParameterLocation.Header,
                        _ => (ParameterLocation?)null
                    }))
                    .Where(p => p.Location is not null)
                    .ToList();

                foreach (var operation in pathItem.Operations.Values)
                {
                    operation.Parameters ??= [];
                    foreach (var (parameter, location) in documented)
                    {
                        var name = location == ParameterLocation.Header ? parameter.HeaderName ?? parameter.Name : parameter.QueryParameterName ?? parameter.Name;
                        if (operation.Parameters.Any(p => p.In == location && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        operation.Parameters.Add(new OpenApiParameter
                        {
                            Name = name,
                            In = location,
                            Required = parameter.Required && string.IsNullOrEmpty(parameter.DefaultValue),
                            Description = $"Function parameter {parameter.Name} ({parameter.SqlType})",
                            Schema = new OpenApiSchema { Type = SchemaType(parameter.SqlType), Pattern = parameter.ValidationPattern }
                        });
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    private static JsonSchemaType SchemaType(string sqlType)
    {
        var type = sqlType.ToUpperInvariant();
        if (type.Contains("INT"))
            return JsonSchemaType.Integer;
        if (type.StartsWith("DECIMAL") || type.StartsWith("NUMERIC") || type.Contains("FLOAT") || type.Contains("REAL") || type.Contains("MONEY"))
            return JsonSchemaType.Number;
        return type == "BIT" || type.StartsWith("BOOL") ? JsonSchemaType.Boolean : JsonSchemaType.String;
    }
}
