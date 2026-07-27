namespace PortwayApi.Services;

using PortwayApi.Classes;

/// <summary>Central namespace-aware lookup for endpoint definitions across all endpoint types</summary>
public sealed class EndpointResolver
{
    public bool TryResolve(EndpointType type, string? namespaceName, string endpointName, out EndpointDefinition? endpoint)
    {
        endpoint = null;

        var endpoints = type switch
        {
            EndpointType.SQL => EndpointHandler.GetSqlEndpoints(),
            EndpointType.Proxy => EndpointHandler.GetProxyEndpoints(),
            EndpointType.Webhook => EndpointHandler.GetSqlWebhookEndpoints(),
            EndpointType.Static => EndpointHandler.GetStaticEndpoints(),
            EndpointType.Files => EndpointHandler.GetFileEndpoints(),
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
