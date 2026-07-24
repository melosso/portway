namespace PortwayApi.Classes;

/// <summary>Column validation rule configuration for input validation</summary>
public class ColumnValidationRule
{
    /// <summary>Regular expression pattern to validate against</summary>
    public string Regex { get; set; } = string.Empty;

    /// <summary>Custom validation error message to display when validation fails</summary>
    public string ValidationMessage { get; set; } = string.Empty;
}
