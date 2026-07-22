namespace PortwayApi.Services;

using PortwayApi.Classes;
using PortwayApi.Interfaces;

/// <summary>Central namespace-aware lookup for endpoint definitions across all endpoint types</summary>
public class EndpointResolver
{
    private readonly IEndpointRegistry _registry;

    public EndpointResolver(IEndpointRegistry registry)
    {
        _registry = registry;
    }

    public bool TryResolve(EndpointType type, string? namespaceName, string endpointName, out EndpointDefinition? endpoint)
    {
        endpoint = null;

        var endpoints = type switch
        {
            EndpointType.SQL => _registry.GetSqlEndpoints(),
            EndpointType.Proxy => _registry.GetProxyEndpoints(),
            EndpointType.Webhook => _registry.GetSqlWebhookEndpoints(),
            EndpointType.Static => _registry.GetStaticEndpoints(),
            EndpointType.Files => _registry.GetFileEndpoints(),
            _ => null
        };

        if (endpoints == null)
            return false;

        if (!string.IsNullOrEmpty(namespaceName) &&
            endpoints.TryGetValue($"{namespaceName}/{endpointName}", out endpoint))
            return true;

        return endpoints.TryGetValue(endpointName, out endpoint);
    }
}
