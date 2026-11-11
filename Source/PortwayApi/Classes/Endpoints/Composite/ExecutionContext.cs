namespace PortwayApi.Classes;

/// <summary>
/// Execution context to maintain state between composite step executions
/// </summary>
public sealed class ExecutionContext
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public Dictionary<string, object> Variables { get; set; } = new();

    /// <summary>
    /// Secret values used by this request, masked in every response
    /// </summary>
    public HashSet<string> SecretValues { get; } = new(StringComparer.Ordinal);

    public string Mask(string text)
    {
        foreach (var value in SecretValues)
        {
            foreach (var form in (string[])[value, System.Text.Json.JsonEncodedText.Encode(value).Value,
                         System.Text.Json.JsonEncodedText.Encode(value, System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping).Value])
                text = text.Replace(form, "***", StringComparison.Ordinal);
        }
        return text;
    }

    public void SetVariable(string name, object value)
    {
        Variables[name] = value;
    }

    public T? GetVariable<T>(string name)
    {
        if (Variables.TryGetValue(name, out var value))
        {
            if (value is T typedValue)
            {
                return typedValue;
            }

            try
            {
                // Try to convert if direct cast fails
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return default;
            }
        }

        return default;
    }
}