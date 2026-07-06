namespace PortwayApi.Classes;

public class ColumnMetadata
{
    public string ColumnName { get; set; } = string.Empty;
    public string DatabaseColumnName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string ClrType { get; set; } = string.Empty;
    public int? MaxLength { get; set; }
    public bool IsNullable { get; set; }
    public int? NumericPrecision { get; set; }
    public int? NumericScale { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsIdentity { get; set; }
    public bool IsComputed { get; set; }
}
