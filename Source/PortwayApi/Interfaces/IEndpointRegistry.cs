namespace PortwayApi.Interfaces;

using PortwayApi.Classes;

/// <summary>Provides access to loaded endpoint definitions per type</summary>
public interface IEndpointRegistry
{
    Dictionary<string, EndpointDefinition> GetSqlEndpoints();
    Dictionary<string, EndpointDefinition> GetProxyEndpoints();
    Dictionary<string, EndpointDefinition> GetSqlWebhookEndpoints();
    Dictionary<string, EndpointDefinition> GetStaticEndpoints();
    Dictionary<string, EndpointDefinition> GetFileEndpoints();
}
