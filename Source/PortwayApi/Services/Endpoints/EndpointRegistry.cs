namespace PortwayApi.Services;

using PortwayApi.Classes;
using PortwayApi.Interfaces;

/// <summary>Instance facade over the static endpoint loader, first step toward removing static coupling</summary>
public sealed class EndpointRegistry : IEndpointRegistry
{
    public Dictionary<string, EndpointDefinition> GetSqlEndpoints() => EndpointHandler.GetSqlEndpoints();
    public Dictionary<string, EndpointDefinition> GetProxyEndpoints() => EndpointHandler.GetProxyEndpoints();
    public Dictionary<string, EndpointDefinition> GetSqlWebhookEndpoints() => EndpointHandler.GetSqlWebhookEndpoints();
    public Dictionary<string, EndpointDefinition> GetStaticEndpoints() => EndpointHandler.GetStaticEndpoints();
    public Dictionary<string, EndpointDefinition> GetFileEndpoints() => EndpointHandler.GetFileEndpoints();
}
