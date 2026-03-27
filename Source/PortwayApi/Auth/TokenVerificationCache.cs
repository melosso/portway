namespace PortwayApi.Auth;

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

public interface ITokenVerificationCache
{
    bool TryGet(string cacheKey, [NotNullWhen(true)] out AuthToken? token);
    void Set(string cacheKey, AuthToken token, int tokenId);
    void Invalidate(int tokenId);
}

/// <summary>
/// Singleton cache for token verification results. Reduces per-request PBKDF2 hashing
/// and SQLite round-trips from 3 to 0 on cache hit (30s TTL).
/// Supports explicit invalidation by token ID on revocation.
/// </summary>
public sealed class TokenVerificationCache(IMemoryCache cache) : ITokenVerificationCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    // Maps tokenId → cache key to enable invalidation when a token is revoked by ID
    private readonly ConcurrentDictionary<int, string> _idToKey = new();

    /// <summary>
    /// Builds a stable, fixed-length cache key from the raw bearer token using SHA-256.
    /// The raw token is never stored; only its hash is used as the key.
    /// Uses a stack buffer for tokens ≤256 UTF-8 bytes, ArrayPool otherwise.
    /// </summary>
    public static string BuildKey(string rawToken)
    {
        const int StackLimit = 256;
        var enc = Encoding.UTF8;
        var maxBytes = enc.GetMaxByteCount(rawToken.Length);

        byte[]? rented = null;
        Span<byte> tokenBuf = maxBytes <= StackLimit
            ? stackalloc byte[StackLimit]
            : (rented = ArrayPool<byte>.Shared.Rent(maxBytes));

        Span<byte> hash = stackalloc byte[32];
        try
        {
            var written = enc.GetBytes(rawToken.AsSpan(), tokenBuf);
            SHA256.HashData(tokenBuf[..written], hash);
            return $"ptok:{Convert.ToHexString(hash)}";
        }
        finally
        {
            if (rented is not null) ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public bool TryGet(string cacheKey, [NotNullWhen(true)] out AuthToken? token) =>
        cache.TryGetValue(cacheKey, out token);

    public void Set(string cacheKey, AuthToken token, int tokenId)
    {
        cache.Set(cacheKey, token, Ttl);
        _idToKey[tokenId] = cacheKey;
    }

    public void Invalidate(int tokenId)
    {
        if (_idToKey.TryRemove(tokenId, out var key))
            cache.Remove(key);
    }
}
