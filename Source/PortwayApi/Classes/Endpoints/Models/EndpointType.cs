namespace PortwayApi.Classes;

/// <summary>Defines the types of endpoints supported by the API</summary>
public enum EndpointType
{
    /// <summary>Standard endpoint (fallback)</summary>
    Standard,

    /// <summary>SQL database endpoint</summary>
    SQL,

    /// <summary>Proxy endpoint to forward requests to another service</summary>
    Proxy,

    /// <summary>Composite endpoint that combines multiple operations</summary>
    Composite,

    /// <summary>Webhook endpoint for receiving external events</summary>
    Webhook,

    /// <summary>Files endpoint for file storage and retrieval</summary>
    Files,

    /// <summary>Static endpoint for serving predefined content</summary>
    Static,

    /// <summary>Private endpoint (not publicly accessible)</summary>
    Private
}
