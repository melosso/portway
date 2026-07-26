namespace PortwayApi.Classes;

/// <summary>Represents a Static endpoint entity for serving predefined content</summary>
public class StaticEndpointEntity : EndpointEntityBase
{
    /// <summary>MIME type for the response (application/json, text/plain, image/png, etc.)</summary>
    public string ContentType { get; set; } = "text/plain";

    /// <summary>Filename containing the static content (relative to the endpoint directory)</summary>
    public string ContentFile { get; set; } = "content.txt";

    /// <summary>Whether OData filtering ($filter, $select, etc.) is enabled for this endpoint</summary>
    public bool EnableFiltering { get; set; } = false;
}
