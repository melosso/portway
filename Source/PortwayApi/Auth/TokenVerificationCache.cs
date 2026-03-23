namespace PortwayApi.Auth;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

public interface ITokenVerificationCache
{
    bool TryGet(string cacheKey, out AuthToken? token);
    void Set(string cacheKey, AuthToken token, int tokenId);
    void Invalidate(int tokenId);
}

/// <summary>
/// Singleton cache for token verification results. Reduces per-request PBKDF2 hashing
/// and SQLite round-trips from 3 to 0 on cache hit (30s TTL).
/// Supports explicit invalidation by token ID on revocation.
/// </summary>
public sealed class TokenVerificationCache : ITokenVerificationCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private readonly IMemoryCache _cache;
    // Maps tokenId → cache key to enable invalidation when a token is revoked by ID
    private readonly ConcurrentDictionary<int, string> _idToKey = new();

    public TokenVerificationCache(IMemoryCache cache) => _cache = cache;

    /// <summary>
    /// Builds a stable, fixed-length cache key from the raw bearer token using SHA-256.
    /// The raw token is never stored; only its hash is used as the key.
    /// </summary>
    public static string BuildKey(string rawToken)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(rawToken), hash);
        return $"ptok:{Convert.ToHexString(hash)}";
    }

    public bool TryGet(string cacheKey, out AuthToken? token) =>
        _cache.TryGetValue(cacheKey, out token);

    public void Set(string cacheKey, AuthToken token, int tokenId)
    {
        _cache.Set(cacheKey, token, Ttl);
        _idToKey[tokenId] = cacheKey;
    }

    public void Invalidate(int tokenId)
    {
        if (_idToKey.TryRemove(tokenId, out var key))
            _cache.Remove(key);
    }
}
