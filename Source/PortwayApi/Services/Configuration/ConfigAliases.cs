namespace PortwayApi.Services.Configuration;

using Serilog;

/// <summary>
/// Configuration names removed in v0.13.0. A configuration that still uses one logs a startup error naming the current key.
/// </summary>
public static class ConfigAliases
{
    public const string Section = "Console";
    public const string FormerSection = "WebUi";

    /// <summary>
    /// Renamed keys as (current, former).
    /// </summary>
    public static readonly IReadOnlyList<(string Current, string Former)> RenamedKeys =
    [
        ("RateLimiting:IpWindowSeconds", "RateLimiting:IpWindow"),
        ("RateLimiting:TokenWindowSeconds", "RateLimiting:TokenWindow"),
        ("RateLimiting:Provider", "RateLimiting:Store"),
        ("RateLimiting:Redis:ConnectionString", "RateLimiting:RedisConnectionString"),
        ("SqlConnectionPooling:ConnectionTimeoutSeconds", "SqlConnectionPooling:ConnectionTimeout"),
        ("SqlConnectionPooling:CommandTimeoutSeconds", "SqlConnectionPooling:CommandTimeout"),
        ("Caching:Provider", "Caching:ProviderType"),
        ("FileStorage:MemoryCacheDurationSeconds", "FileStorage:MemoryCacheTimeSeconds"),
        ("RequestTrafficLogging:Provider", "RequestTrafficLogging:StorageType"),
        ("RequestTrafficLogging:CaptureRequestBodies", "RequestTrafficLogging:IncludeRequestBodies"),
        ("RequestTrafficLogging:CaptureResponseBodies", "RequestTrafficLogging:IncludeResponseBodies"),
        ("RequestTrafficLogging:InfoLoggingEnabled", "RequestTrafficLogging:EnableInfoLogging"),
        ("Console:Customization:LandingPageEnabled", "Console:Customization:EnableLandingPage"),
        ("OpenApi:Scalar:Theme", "OpenApi:ScalarTheme"),
        ("OpenApi:Scalar:Layout", "OpenApi:ScalarLayout"),
        ("OpenApi:Scalar:ShowSidebar", "OpenApi:ScalarShowSidebar"),
        ("OpenApi:Scalar:HideDownloadButton", "OpenApi:ScalarHideDownloadButton"),
        ("OpenApi:Scalar:HideModels", "OpenApi:ScalarHideModels"),
        ("OpenApi:Scalar:HideClientButton", "OpenApi:ScalarHideClientButton"),
        ("OpenApi:Scalar:HideTestRequestButton", "OpenApi:ScalarHideTestRequestButton"),
    ];

    /// <summary>
    /// Keys that no longer apply.
    /// </summary>
    public static readonly IReadOnlyList<string> RemovedKeys = ["Mcp:RequireAuthentication", "Mcp:AppsEnabled"];

    /// <summary>
    /// Returns one message per former name present in <paramref name="root"/>, naming its source and current key.
    /// </summary>
    public static IReadOnlyList<string> FormerNames(IConfigurationRoot root)
    {
        var messages = new List<string>();
        foreach (var provider in root.Providers)
        {
            if (provider.GetChildKeys([], FormerSection).Any())
                messages.Add($"Configuration section {FormerSection} in {provider} is no longer read; rename it to {Section}");

            foreach (var (current, former) in RenamedKeys)
            {
                if (provider.TryGet(former, out _))
                    messages.Add($"Configuration key {former} in {provider} is no longer read; rename it to {current}");
            }

            foreach (var removed in RemovedKeys)
            {
                if (provider.TryGet(removed, out _))
                    messages.Add($"Configuration key {removed} in {provider} no longer applies; remove it");
            }
        }
        return messages;
    }

    public static void ReportFormerNames(IConfigurationRoot root)
    {
        foreach (var message in FormerNames(root))
            Log.Error("{Message:l}", message);
    }
}
