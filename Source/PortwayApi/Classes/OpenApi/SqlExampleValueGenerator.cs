using System.Text.Json.Nodes;
using PortwayApi.Classes;

namespace PortwayApi.Classes.OpenApi;

/// <summary>
/// Generates example JSON values for OpenAPI documentation based on SQL column/parameter metadata.
/// Extracted from SqlMetadataDocumentFilter to enable unit testing.
/// </summary>
public static class SqlExampleValueGenerator
{
    /// <summary>
    /// Generates an example value based on column metadata.
    /// Returns null for nullable columns.
    /// </summary>
    public static JsonNode? FromColumn(ColumnMetadata column)
    {
        if (column.IsNullable)
            return null;

        return column.ClrType switch
        {
            "System.String"                      => JsonValue.Create(column.IsPrimaryKey ? "ABC123" : "example"),
            "System.Int32"                       => JsonValue.Create(column.IsPrimaryKey ? 1 : 42),
            "System.Int64"                       => JsonValue.Create(column.IsPrimaryKey ? 1L : 42L),
            "System.Boolean"                     => JsonValue.Create(true),
            "System.Decimal" or "System.Double"  => JsonValue.Create(99.99),
            "System.DateTime"                    => JsonValue.Create(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")),
            "System.Guid"                        => JsonValue.Create(Guid.NewGuid().ToString()),
            _                                    => JsonValue.Create("value")
        };
    }

    /// <summary>
    /// Generates an example value based on parameter metadata and property name heuristics.
    /// </summary>
    public static JsonNode? FromParameter(ParameterMetadata parameter, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            return null;

        var propName = propertyName.ToLowerInvariant();

        if (propName.Contains("id") && parameter.ClrType == "System.Guid")
            return JsonValue.Create(Guid.NewGuid().ToString());

        if (propName.Contains("id") && parameter.ClrType.Contains("Int"))
            return JsonValue.Create(1);

        if (propName.Contains("name") || propName.Contains("title"))
            return JsonValue.Create($"Example {propertyName}");

        if (propName.Contains("email"))
            return JsonValue.Create("user@example.com");

        if (propName.Contains("date") || propName.Contains("time"))
            return JsonValue.Create(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"));

        if (propName.StartsWith("is") || propName.StartsWith("has") || propName.Contains("active"))
            return JsonValue.Create(true);

        if (propName.Contains("amount") || propName.Contains("price") || propName.Contains("cost"))
            return JsonValue.Create(99.99);

        // Fallback to type-based examples
        return parameter.ClrType switch
        {
            "System.String"                      => JsonValue.Create($"example {propertyName}"),
            "System.Int32"                       => JsonValue.Create(42),
            "System.Int64"                       => JsonValue.Create(42L),
            "System.Boolean"                     => JsonValue.Create(true),
            "System.Decimal" or "System.Double"  => JsonValue.Create(99.99),
            "System.DateTime"                    => JsonValue.Create(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")),
            "System.Guid"                        => JsonValue.Create(Guid.NewGuid().ToString()),
            _                                    => parameter.IsNullable || parameter.HasDefaultValue ? null : JsonValue.Create("value")
        };
    }
}
