namespace PortwayApi.Classes;

/// <summary>Represents a parameter for a Table Valued Function</summary>
public class TVFParameter
{
    /// <summary>Name of the parameter in the function (without @)</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>SQL data type (e.g. "NVARCHAR(20)", "INT", "DATETIME")</summary>
    public string SqlType { get; set; } = string.Empty;

    /// <summary>Source of the parameter value: Path, Query, or Header</summary>
    public string Source { get; set; } = "Query"; // Path, Query, Header

    /// <summary>Position in the URL path (for Path parameters)</summary>
    public int Position { get; set; } = 0;

    /// <summary>Query parameter name (for Query parameters, defaults to Name if not specified)</summary>
    public string? QueryParameterName { get; set; }

    /// <summary>Header name (for Header parameters)</summary>
    public string? HeaderName { get; set; }

    /// <summary>Whether this parameter is required</summary>
    public bool Required { get; set; } = true;

    /// <summary>Default value if parameter is not provided (SQL expression)</summary>
    public string? DefaultValue { get; set; }

    /// <summary>Optional validation pattern (regex)</summary>
    public string? ValidationPattern { get; set; }
}
