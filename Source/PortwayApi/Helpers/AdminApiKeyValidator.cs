using Serilog;

namespace PortwayApi.Helpers;

/// <summary>
/// One-time seed value for initial admin account setup.
/// </summary>
public readonly record struct AdminSeedKey(string? Value)
{
    public bool IsConfigured => !string.IsNullOrEmpty(Value);
}

/// <summary>
/// Validates <c>WebUi:AdminApiKey</c> startup configuration.
/// </summary>
public static class AdminApiKeyValidator
{
    private const string PlaceholderKey = "INSECURE-CHANGE-ME-admin-api-key";

    /// <summary>
    /// Resolves the admin seed key, enforcing security checks in non-development environments.
    /// </summary>
    public static AdminSeedKey Resolve(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var adminApiKey = configuration.GetValue<string>("WebUi:AdminApiKey", "") ?? "";

        if (adminApiKey == PlaceholderKey && !environment.IsDevelopment())
        {
            Log.Error("WebUi:AdminApiKey cannot be the default placeholder value; a strong, unique key (32+ chars) is required");
            adminApiKey = "";
        }
        else if (!string.IsNullOrEmpty(adminApiKey) && adminApiKey.Length < 32 && !environment.IsDevelopment())
        {
            Log.Warning("WebUi:AdminApiKey is shorter than 32 characters; use a longer, randomly generated key");
        }

        return new AdminSeedKey(adminApiKey);
    }

    /// <summary>
    /// Determines if Web UI features are enabled via explicit setting or configured seed key.
    /// </summary>
    public static bool IsEnabled(IConfiguration configuration, AdminSeedKey adminKey)
        => configuration.GetValue<bool?>("WebUi:Enabled") ?? adminKey.IsConfigured;
}