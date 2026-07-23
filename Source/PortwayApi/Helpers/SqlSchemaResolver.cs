namespace PortwayApi.Helpers;

using PortwayApi.Services.Providers;
using Serilog;

/// <summary>Resolves the effective schema for a provider, treating the template default dbo as portable</summary>
public static class SqlSchemaResolver
{
    /// <summary>Returns the schema to use, empty means unqualified (the connection's database scopes the object)</summary>
    public static string Resolve(string? configuredSchema, ISqlProvider provider, string? connectionDatabase = null)
    {
        var fallback = provider.DefaultSchema.Length > 0
            ? provider.DefaultSchema
            : connectionDatabase ?? string.Empty;

        if (string.IsNullOrWhiteSpace(configuredSchema))
            return fallback;

        // dbo outside SQL Server is almost always a copied template default, map it to the provider's own
        if (configuredSchema.Equals("dbo", StringComparison.OrdinalIgnoreCase) &&
            provider.ProviderType != SqlProviderType.SqlServer)
        {
            Log.Debug("Schema 'dbo' mapped to '{Fallback}' for {Provider}; set DatabaseSchema explicitly to override",
                fallback, provider.ProviderType);
            return fallback;
        }

        return configuredSchema;
    }
}
