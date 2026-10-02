namespace PortwayApi.Classes;

/// <summary>
/// Discovery and parsing rules for one endpoint type, including its log labels
/// </summary>
internal sealed record EndpointLoaderSpec(
    string TypeLabel,
    string LowerLabel,
    string FailPrefix,
    string SearchPattern,
    bool NamespaceAware,
    Func<string, EndpointDefinition?> Parse,
    Func<EndpointDefinition, bool> IsValid,
    Action<string, EndpointDefinition> LogLoaded);
