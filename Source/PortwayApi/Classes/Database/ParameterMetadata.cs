namespace PortwayApi.Classes;

public class ParameterMetadata
{
    public string ParameterName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string ClrType { get; set; } = string.Empty;
    public int? MaxLength { get; set; }
    public bool IsNullable { get; set; }
    public int? NumericPrecision { get; set; }
    public int? NumericScale { get; set; }
    public bool IsOutput { get; set; }
    public bool HasDefaultValue { get; set; }
    public int Position { get; set; }
}
