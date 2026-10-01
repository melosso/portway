using System.Collections.Frozen;

namespace PortwayApi.Helpers;

/// <summary>
/// Header names stripped from client requests or reserved from configuration
/// </summary>
public static class HeaderPolicy
{
    /// <summary>
    /// Hop-by-hop, forwarding and Content-Length headers stripped from client requests before proxying
    /// </summary>
    public static readonly FrozenSet<string> StrippedFromClient = new[]
    {
        "Host", "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailers", "Transfer-Encoding", "Upgrade",
        "X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto", "X-Forwarded-Port",
        "X-Real-IP", "X-Original-For", "Forwarded",
        "Content-Length",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> Reserved = new[]
    {
        "Authorization", "Cookie", "Expect", "Origin", "Accept", "Accept-Encoding", "If-None-Match", "If-Match",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// RFC 9110 token characters, ASCII only
    /// </summary>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name) && name.All(c => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c));

    /// <summary>
    /// True for names a tenancy header cannot use
    /// </summary>
    public static bool IsReserved(string name) =>
        StrippedFromClient.Contains(name)
        || Reserved.Contains(name)
        || name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase);
}
