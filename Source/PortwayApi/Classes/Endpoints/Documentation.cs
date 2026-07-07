namespace PortwayApi.Classes;

/// <summary>OpenAPI documentation settings for an endpoint</summary>
public class Documentation
{
    /// <summary>Brief summary of what this endpoint does (e.g. "Get product details")</summary>
    public string? Summary { get; set; }

    /// <summary>Detailed description of the endpoint functionality (supports Markdown)</summary>
    public string? Description { get; set; }

    /// <summary>Description for the OpenAPI tag (supports Markdown)</summary>
    public string? TagDescription { get; set; }

    /// <summary>Custom summaries per HTTP method for OpenAPI operation summaries (e.g. "GET": "Retrieve account data")</summary>
    public Dictionary<string, string>? MethodDescriptions { get; set; }

    /// <summary>Custom detailed documentation per HTTP method for OpenAPI operation descriptions</summary>
    public Dictionary<string, string>? MethodDocumentation { get; set; }
}
