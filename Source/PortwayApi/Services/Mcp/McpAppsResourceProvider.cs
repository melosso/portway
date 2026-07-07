namespace PortwayApi.Services.Mcp;

public class McpAppsResourceProvider
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _uiResources = new();

    public void RegisterUiResource(string uri, string htmlContent)
    {
        _uiResources[uri] = htmlContent;
    }

    public string? GetUiResource(string uri)
    {
        return _uiResources.TryGetValue(uri, out var content) ? content : null;
    }

    public IReadOnlyDictionary<string, string> GetAllResources() => _uiResources;

    public static string GenerateEndpointExplorerHtml(IEnumerable<EndpointMcpInfo> endpoints) =>
        EndpointExplorerHtml.Generate(endpoints);
}
