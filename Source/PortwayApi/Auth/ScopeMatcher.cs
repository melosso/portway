namespace PortwayApi.Auth;

public static class ScopeMatcher
{
    public static bool Matches(IEnumerable<string> scopes, string endpointName) =>
        !string.IsNullOrWhiteSpace(endpointName) && scopes.Any(s =>
            s == "*"
            || s.Equals(endpointName, StringComparison.OrdinalIgnoreCase)
            || (s.EndsWith('*') && endpointName.StartsWith(s[..^1], StringComparison.OrdinalIgnoreCase)));
}
