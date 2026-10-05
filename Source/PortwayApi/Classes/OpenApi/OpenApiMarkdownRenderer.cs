namespace PortwayApi.Classes.OpenApi;

using System.Text;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;

/// <summary>
/// Renders the generated OpenAPI document as one Markdown page for LLMs, grouped by the same tag tree as /docs
/// </summary>
internal static class OpenApiMarkdownRenderer
{
    private static readonly string[] ErrorSchemas = ["ErrorResponse", "ValidationErrorResponse"];

    public static string Render(OpenApiDocument document)
    {
        // operations reference the shared error responses by id, resolved through the host document
        document.SetReferenceHostDocument();

        var md = new StringBuilder();

        md.Append("# ").AppendLine(document.Info?.Title).AppendLine();
        if (!string.IsNullOrWhiteSpace(document.Info?.Description))
        {
            md.AppendLine(document.Info.Description).AppendLine();
        }

        md.Append("- Version: `").Append(document.Info?.Version).AppendLine("`");
        if (document.Servers?.FirstOrDefault()?.Url is { } server)
        {
            md.Append("- Base URL: `").Append(server).AppendLine("`");
        }

        foreach (var scheme in document.Components?.SecuritySchemes?.Values ?? Enumerable.Empty<IOpenApiSecurityScheme>())
        {
            md.Append("- Authentication: ").AppendLine(scheme.Type == SecuritySchemeType.Http
                ? $"`Authorization: {Capitalize(scheme.Scheme)} <token>`"
                : $"`{scheme.Name}` in {scheme.In?.ToString().ToLowerInvariant()}");
        }

        if (document.ExternalDocs?.Url is { } guide)
        {
            md.Append("- Guide: [").Append(document.ExternalDocs.Description ?? guide.ToString()).Append("](").Append(guide).AppendLine(")");
        }

        md.AppendLine();

        var operations = (document.Paths ?? new OpenApiPaths())
            .SelectMany(p => (p.Value.Operations ?? new Dictionary<HttpMethod, OpenApiOperation>()).Select(o => (Path: p.Key, Method: o.Key.Method.ToUpperInvariant(), Operation: o.Value)))
            .ToList();

        var tags = document.Tags?.ToList() ?? [];
        var parents = tags.Where(t => t.Name is not null).ToDictionary(t => t.Name!, t => t.Parent?.Name, StringComparer.Ordinal);
        var rendered = new HashSet<OpenApiOperation>();

        foreach (var tag in tags)
        {
            var level = Math.Min(6, 2 + Depth(tag.Name, parents));
            md.Append('#', level).Append(' ').AppendLine(tag.Summary ?? tag.Name).AppendLine();
            if (!string.IsNullOrWhiteSpace(tag.Description))
            {
                md.AppendLine(tag.Description).AppendLine();
            }

            foreach (var (path, method, operation) in operations.Where(o => o.Operation.Tags?.Any(t => t.Name == tag.Name) == true))
            {
                RenderOperation(md, Math.Min(6, level + 1), path, method, operation);
                rendered.Add(operation);
            }
        }

        var untagged = operations.Where(o => !rendered.Contains(o.Operation)).ToList();
        if (untagged.Count > 0)
        {
            md.AppendLine("## Other").AppendLine();
            foreach (var (path, method, operation) in untagged)
            {
                RenderOperation(md, 3, path, method, operation);
            }
        }

        RenderErrors(md, document);
        return md.ToString();
    }

    private static void RenderOperation(StringBuilder md, int level, string path, string method, OpenApiOperation operation)
    {
        md.Append('#', level).Append(" `").Append(method).Append(' ').Append(path).Append('`');
        if (!string.IsNullOrWhiteSpace(operation.Summary))
        {
            md.Append(' ').Append(operation.Summary);
        }

        md.AppendLine().AppendLine();

        if (operation.Deprecated)
        {
            md.AppendLine("Deprecated.").AppendLine();
        }

        if (operation.Extensions?.TryGetValue("x-badges", out var badges) == true && badges is JsonNodeExtension { Node: JsonArray list })
        {
            md.Append("Supports: ").AppendLine(string.Join(", ", list.Select(b => b?["name"]?.GetValue<string>()))).AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(operation.Description))
        {
            md.AppendLine(operation.Description).AppendLine();
        }

        if (operation.Parameters is { Count: > 0 } parameters)
        {
            md.AppendLine("| Parameter | In | Type | Required | Description |").AppendLine("|---|---|---|---|---|");
            foreach (var parameter in parameters)
            {
                var type = parameter.Schema is not null ? TypeOf(parameter.Schema) : string.Join(", ", parameter.Content?.Keys ?? []);
                md.Append("| `").Append(parameter.Name).Append("` | ").Append(Location(parameter.In)).Append(" | ").Append(Cell(type))
                  .Append(" | ").Append(parameter.Required ? "yes" : "no").Append(" | ").Append(Cell(parameter.Description)).AppendLine(" |");
            }

            md.AppendLine();
        }

        if (operation.RequestBody?.Content is { Count: > 0 } body)
        {
            md.AppendLine("Request body:").AppendLine();
            foreach (var (mediaType, media) in body)
            {
                md.Append("- `").Append(mediaType).Append('`');
                md.AppendLine(media.Schema is null ? "" : $": {TypeOf(media.Schema)}");
                RenderProperties(md, media.Schema, "  ");
            }

            md.AppendLine();
        }

        if (operation.Responses is { Count: > 0 } responses)
        {
            md.AppendLine("Responses:").AppendLine();
            foreach (var (status, response) in responses)
            {
                md.Append("- `").Append(status).Append('`');
                var summary = (response as IOpenApiSummarizedElement)?.Summary;
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    md.Append(' ').Append(summary);
                }

                if (!string.IsNullOrWhiteSpace(response.Description) && response.Description != summary)
                {
                    md.Append(": ").Append(Line(response.Description));
                }

                var schemas = (response.Content ?? new Dictionary<string, IOpenApiMediaType>())
                    .Where(c => c.Value.Schema is not null)
                    .Select(c => $"`{c.Key}` {TypeOf(c.Value.Schema)}");
                var content = string.Join(", ", schemas);
                md.AppendLine(content.Length > 0 ? $" ({content})" : "");
            }

            md.AppendLine();
        }
    }

    // the error envelope is shared by every operation, so it is described once at the end
    private static void RenderErrors(StringBuilder md, OpenApiDocument document)
    {
        var schemas = document.Components?.Schemas;
        var present = ErrorSchemas.Where(name => schemas?.ContainsKey(name) == true).ToList();
        if (present.Count == 0)
        {
            return;
        }

        md.AppendLine("## Errors").AppendLine();
        foreach (var name in present)
        {
            md.Append("`").Append(name).AppendLine("`:").AppendLine();
            RenderProperties(md, schemas![name], "");
            md.AppendLine();
        }
    }

    private static void RenderProperties(StringBuilder md, IOpenApiSchema? schema, string indent)
    {
        var target = schema?.Type?.HasFlag(JsonSchemaType.Array) == true ? schema.Items : schema;
        foreach (var (name, property) in target?.Properties ?? new Dictionary<string, IOpenApiSchema>())
        {
            md.Append(indent).Append("- `").Append(name).Append("` ").Append(TypeOf(property));
            if (target!.Required?.Contains(name) == true)
            {
                md.Append(", required");
            }

            if (!string.IsNullOrWhiteSpace(property.Description))
            {
                md.Append(": ").Append(Line(property.Description));
            }

            md.AppendLine();
        }
    }

    private static string TypeOf(IOpenApiSchema? schema) => schema switch
    {
        null => "any",
        OpenApiSchemaReference reference => reference.Reference.Id ?? "object",
        { Type: { } type } when type.HasFlag(JsonSchemaType.Array) => $"array of {TypeOf(schema.Items)}",
        { Type: { } type } => TypeName(type) + (schema.Format is { } format ? $" ({format})" : "") + EnumValues(schema),
        { OneOf.Count: > 0 } => string.Join(" or ", schema.OneOf.Select(TypeOf)),
        { AnyOf.Count: > 0 } => string.Join(" or ", schema.AnyOf.Select(TypeOf)),
        _ => "any"
    };

    private static string TypeName(JsonSchemaType type) => string.Join(" or ",
        Enum.GetValues<JsonSchemaType>().Where(v => v != JsonSchemaType.Null && type.HasFlag(v)).Select(v => v.ToString().ToLowerInvariant()));

    private static string EnumValues(IOpenApiSchema schema) =>
        schema.Enum is { Count: > 0 } values ? ": " + string.Join(", ", values.Select(v => v?.ToString())) : "";

    private static int Depth(string? name, Dictionary<string, string?> parents)
    {
        var depth = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (name is not null && parents.TryGetValue(name, out var parent) && parent is not null && seen.Add(parent))
        {
            depth++;
            name = parent;
        }

        return depth;
    }

    private static string Location(ParameterLocation? location) => location?.ToString().ToLowerInvariant() ?? "";

    private static string Capitalize(string? value) => string.IsNullOrEmpty(value) ? "Bearer" : char.ToUpperInvariant(value[0]) + value[1..];

    private static string Line(string? value) => (value ?? "").ReplaceLineEndings(" ").Trim();

    private static string Cell(string? value) => Line(value).Replace("|", "\\|");
}
