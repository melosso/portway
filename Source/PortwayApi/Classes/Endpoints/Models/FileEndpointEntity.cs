namespace PortwayApi.Classes;

/// <summary>Represents a File endpoint entity for local file handling</summary>
public class FileEndpointEntity : EndpointEntityBase
{
    /// <summary>Type of storage (Local, S3, etc.)</summary>
    public string StorageType { get; set; } = "Local";

    /// <summary>Base directory for this endpoint (relative to the root storage directory)</summary>
    public string? BaseDirectory { get; set; }

    /// <summary>Extensions accepted on upload; also documents the multipart part encoding</summary>
    public List<string>? AllowedExtensions { get; set; }
}
