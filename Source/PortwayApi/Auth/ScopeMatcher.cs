namespace PortwayApi.Auth;

/// <summary>
/// Endpoint scope patterns shared by tokens and environment authentication.
/// </summary>
public static class ScopeMatcher
{
    /// <summary>
    /// True when a pattern is <c>*</c>, equals <paramref name="endpointName"/> or is a prefix ending in <c>*</c>. Case-insensitive.
    /// </summary>
    public static bool Matches(IEnumerable<string> scopes, string endpointName) =>
        !string.IsNullOrWhiteSpace(endpointName) && scopes.Any(s =>
            s == "*"
            || s.Equals(endpointName, StringComparison.OrdinalIgnoreCase)
            || (s.EndsWith('*') && endpointName.StartsWith(s[..^1], StringComparison.OrdinalIgnoreCase)));
}
