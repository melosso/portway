namespace PortwayApi.Classes;

/// <summary>DELETE operation pattern configuration</summary>
public class DeletePattern
{
    /// <summary>Style of DELETE operation: PathParameter, QueryParameter, ODataGuid, ODataKey</summary>
    public string Style { get; set; } = "PathParameter";

    /// <summary>Parameter name for QueryParameter style (default: "id")</summary>
    public string? Parameter { get; set; }

    /// <summary>Path template for PathParameter style (default: "/{id}")</summary>
    public string? Path { get; set; }

    /// <summary>Description for documentation</summary>
    public string? Description { get; set; }
}
