namespace PortwayApi.Services.Mcp;

using Microsoft.EntityFrameworkCore;
using PortwayApi.Helpers;
using Serilog;

/// <summary>
/// Singleton service that reads and writes MCP chat configuration from the encrypted SQLite store (mcp.db).
/// Sensitive values (ApiKey, InternalApiToken) are encrypted at rest using
/// <see cref="SettingsEncryptionHelper"/> before being persisted.
///
/// An in-memory cache avoids hitting the DB on every chat turn.
/// The cache is invalidated whenever <see cref="SaveConfigAsync"/> is called.
/// </summary>
public sealed class McpConfigService
{
    // Keys whose values are encrypted with PWENC before storage.
    private static readonly HashSet<string> _sensitiveKeys =
        new(StringComparer.OrdinalIgnoreCase) { "ApiKey", "InternalApiToken" };

    // Environment variable that overrides the DB api key — checked first.
    private const string ApiKeyEnvVar = "PORTWAY_CHAT_API_KEY";

    private readonly IDbContextFactory<McpConfigDbContext> _dbFactory;

    // Volatile reference — assignment is atomic on 64-bit .NET, safe without locks for this pattern.
    private volatile ConfigSnapshot? _cache;

    public McpConfigService(IDbContextFactory<McpConfigDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Immutable snapshot of the current MCP chat configuration.
    /// </summary>
    public sealed record ConfigSnapshot(
        string  Provider,
        string  Model,
        string? ApiKey,
        string? InternalApiToken
    )
    {
        /// <summary>True when a provider and API key are both present.</summary>
        public bool IsConfigured => !string.IsNullOrWhiteSpace(Provider)
                                 && !string.IsNullOrWhiteSpace(ApiKey);
    }

    /// <summary>
    /// Returns the current config snapshot, reading from DB on the first call
    /// and after each <see cref="SaveConfigAsync"/> call.
    /// The environment variable <c>PORTWAY_CHAT_API_KEY</c> overrides the stored API key.
    /// </summary>
    public async Task<ConfigSnapshot> GetConfigAsync(CancellationToken ct = default)
    {
        if (_cache is { } cached) return cached;

        await using var db  = await _dbFactory.CreateDbContextAsync(ct);
        var entries         = await db.Config.ToListAsync(ct);
        var dict            = entries.ToDictionary(
            e => e.Key,
            e => (e.Value, e.IsEncrypted),
            StringComparer.OrdinalIgnoreCase);

        string Resolve(string key)
        {
            if (!dict.TryGetValue(key, out var pair)) return string.Empty;
            if (!pair.IsEncrypted) return pair.Value;
            if (SettingsEncryptionHelper.TryDecryptValue(pair.Value, out var plain)) return plain;
            Log.Warning("McpConfig: failed to decrypt value for key '{Key}' — key may be rotated", key);
            return string.Empty;
        }

        // Environment variable takes priority over DB for the API key.
        var apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvVar);
        if (string.IsNullOrWhiteSpace(apiKey))
            apiKey = Resolve("ApiKey").NullIfEmpty();
        else
            Log.Debug("McpConfig: using API key from environment variable {Var}", ApiKeyEnvVar);

        var snapshot = new ConfigSnapshot(
            Provider:         Resolve("Provider"),
            Model:            Resolve("Model"),
            ApiKey:           apiKey,
            InternalApiToken: Resolve("InternalApiToken").NullIfEmpty()
        );

        _cache = snapshot;
        return snapshot;
    }

    /// <summary>
    /// Persists chat configuration to the DB.
    /// <para>ApiKey and InternalApiToken are encrypted before storage.</para>
    /// Pass <c>null</c> to leave a value unchanged.
    /// </summary>
    public async Task SaveConfigAsync(
        string? provider,
        string? model,
        string? apiKey,
        string? internalApiToken,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        if (provider is not null)
            await UpsertAsync(db, "Provider", provider, encrypt: false, ct);

        if (model is not null)
            await UpsertAsync(db, "Model", model, encrypt: false, ct);

        if (apiKey is not null)
            await UpsertAsync(db, "ApiKey", SettingsEncryptionHelper.Encrypt(apiKey), encrypt: true, ct);

        if (internalApiToken is not null)
        {
            // Empty string = explicitly clearing the token (no tool-call auth)
            var valueToStore = internalApiToken.Length == 0
                ? string.Empty
                : SettingsEncryptionHelper.Encrypt(internalApiToken);
            await UpsertAsync(db, "InternalApiToken", valueToStore, encrypt: internalApiToken.Length > 0, ct);
        }

        _cache = null; // invalidate cache — next GetConfigAsync reads from DB
        Log.Information("McpConfig: configuration saved");
    }

    /// <summary>
    /// Removes all stored configuration entries and invalidates the cache.
    /// Chat will be unavailable until <see cref="SaveConfigAsync"/> is called again.
    /// </summary>
    public async Task ClearConfigAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Config.RemoveRange(db.Config);
        await db.SaveChangesAsync(ct);
        _cache = null;
        Log.Information("McpConfig: configuration cleared");
    }

    /// <summary>Returns a masked view of the config for safe display in the UI (API key is never returned).</summary>
    public async Task<object> GetStatusAsync(CancellationToken ct = default)
    {
        var cfg = await GetConfigAsync(ct);
        return new
        {
            configured       = cfg.IsConfigured,
            provider         = cfg.Provider,
            model            = cfg.Model,
            has_api_key      = !string.IsNullOrWhiteSpace(cfg.ApiKey),
            has_internal_token = !string.IsNullOrWhiteSpace(cfg.InternalApiToken),
            api_key_source   = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ApiKeyEnvVar))
                               ? "environment"
                               : (string.IsNullOrWhiteSpace(cfg.ApiKey) ? "none" : "database")
        };
    }

    private static async Task UpsertAsync(
        McpConfigDbContext db,
        string key,
        string value,
        bool encrypt,
        CancellationToken ct)
    {
        var entry = await db.Config.FirstOrDefaultAsync(e => e.Key == key, ct);
        if (entry is null)
        {
            db.Config.Add(new McpConfigEntry
            {
                Key         = key,
                Value       = value,
                IsEncrypted = encrypt,
                UpdatedAt   = DateTime.UtcNow
            });
        }
        else
        {
            entry.Value       = value;
            entry.IsEncrypted = encrypt;
            entry.UpdatedAt   = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }
}

internal static class StringExtensions
{
    /// <summary>Returns null when the string is null, empty, or whitespace-only.</summary>
    internal static string? NullIfEmpty(this string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;
}
