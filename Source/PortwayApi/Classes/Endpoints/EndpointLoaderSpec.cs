namespace PortwayApi.Classes;

/// <summary>Describes how one endpoint type is discovered and parsed from disk</summary>
/// <remarks>
/// TypeLabel: title-case label used in directory-level log messages (e.g. "Proxy")
/// LowerLabel: label used inside per-file log messages (e.g. "proxy"; SQL keeps "SQL")
/// FailPrefix: type word in the "Failed to load ... endpoint" warning; empty for Proxy to match its historical message
/// NamespaceAware: apply folder namespace inference, override warnings and validation; false = flat folder-name key
/// </remarks>
internal sealed record EndpointLoaderSpec(
    string TypeLabel,
    string LowerLabel,
    string FailPrefix,
    string SearchPattern,
    bool NamespaceAware,
    Func<string, EndpointDefinition?> Parse,
    Func<EndpointDefinition, bool> IsValid,
    Action<string, EndpointDefinition> LogLoaded);
