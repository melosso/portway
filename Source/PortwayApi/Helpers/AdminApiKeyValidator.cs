using Serilog;

namespace PortwayApi.Helpers;

/// <summary>Validates the WebUi:AdminApiKey at startup; rejects the shipped placeholder in production</summary>
public static class AdminApiKeyValidator
{
    private const string PlaceholderKey = "INSECURE-CHANGE-ME-admin-api-key";

    /// <summary>Returns the effective admin key; empty string disables Web UI auth</summary>
    public static string Resolve(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var adminApiKey = configuration.GetValue<string>("WebUi:AdminApiKey", "") ?? "";

        // Reject the shipped placeholder key in production; disable Web UI auth and warn loudly
        // In Development the placeholder is intentionally allowed for local testing convenience
        if (adminApiKey == PlaceholderKey && !environment.IsDevelopment())
        {
            Log.Error("WebUi:AdminApiKey is set to the default placeholder value. " +
                      "Web UI authentication has been DISABLED. Set a strong, unique key (≥32 chars) to enable it.");
            adminApiKey = "";
        }
        else if (!string.IsNullOrEmpty(adminApiKey) && adminApiKey.Length < 32 && !environment.IsDevelopment())
        {
            Log.Warning("WebUi:AdminApiKey is shorter than 32 characters. Consider using a longer, randomly generated key.");
        }

        return adminApiKey;
    }
}
