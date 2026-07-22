using System.Text.Json.Nodes;
using PortwayApi.Classes;
using Serilog;

namespace PortwayApi.Helpers;

/// <summary>Applies declarative remove, rename and mask rules to JSON proxy responses</summary>
public static class ResponseTransformHelper
{
    private const string MaskValue = "***";

    /// <summary>Transforms top-level object fields, array elements and OData value wrappers; returns input unchanged on parse failure</summary>
    public static string Apply(string json, ProxyResponseTransforms transforms)
    {
        if (string.IsNullOrWhiteSpace(json) || !transforms.HasRules)
            return json;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Response transform skipped: content is not valid JSON");
            return json;
        }

        switch (root)
        {
            case JsonObject obj:
                TransformObjectAndWrapper(obj, transforms);
                break;
            case JsonArray array:
                TransformElements(array, transforms);
                break;
            default:
                return json;
        }

        return root.ToJsonString();
    }

    private static void TransformObjectAndWrapper(JsonObject obj, ProxyResponseTransforms transforms)
    {
        TransformObject(obj, transforms);

        // OData style collection wrappers keep their items shaped too
        foreach (var wrapper in new[] { "value", "d" })
        {
            if (obj[wrapper] is JsonArray wrapped)
                TransformElements(wrapped, transforms);
        }
    }

    private static void TransformElements(JsonArray array, ProxyResponseTransforms transforms)
    {
        foreach (var item in array)
        {
            if (item is JsonObject element)
                TransformObject(element, transforms);
        }
    }

    /// <summary>Applies the same rules to dictionary-shaped rows, e.g. SQL query results; returns transformed copies</summary>
    public static List<object> ApplyToRows(IEnumerable<object> rows, ProxyResponseTransforms transforms)
    {
        var result = new List<object>();
        foreach (var row in rows)
        {
            if (row is IDictionary<string, object?> source)
            {
                var copy = new Dictionary<string, object?>(source, StringComparer.OrdinalIgnoreCase);
                TransformRow(copy, transforms);
                result.Add(copy);
            }
            else
            {
                result.Add(row);
            }
        }

        return result;
    }

    private static void TransformRow(Dictionary<string, object?> row, ProxyResponseTransforms transforms)
    {
        if (transforms.Remove != null)
        {
            foreach (var field in transforms.Remove)
                row.Remove(field);
        }

        if (transforms.Mask != null)
        {
            foreach (var field in transforms.Mask)
            {
                if (row.ContainsKey(field))
                    row[field] = MaskValue;
            }
        }

        if (transforms.Rename != null)
        {
            foreach (var (from, to) in transforms.Rename)
            {
                if (string.IsNullOrWhiteSpace(to) || !row.TryGetValue(from, out var value) || row.ContainsKey(to))
                    continue;

                row.Remove(from);
                row[to] = value;
            }
        }
    }

    private static void TransformObject(JsonObject obj, ProxyResponseTransforms transforms)
    {
        if (transforms.Remove != null)
        {
            foreach (var field in transforms.Remove)
                obj.Remove(field);
        }

        if (transforms.Mask != null)
        {
            foreach (var field in transforms.Mask)
            {
                if (obj.ContainsKey(field))
                    obj[field] = MaskValue;
            }
        }

        if (transforms.Rename != null)
        {
            foreach (var (from, to) in transforms.Rename)
            {
                if (string.IsNullOrWhiteSpace(to) || !obj.ContainsKey(from) || obj.ContainsKey(to))
                    continue;

                var value = obj[from];
                obj.Remove(from);
                obj[to] = value?.DeepClone();
            }
        }
    }
}
