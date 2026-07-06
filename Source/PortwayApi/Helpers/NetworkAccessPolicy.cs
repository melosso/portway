using System.Text.Json;

namespace PortwayApi.Helpers;

/// <summary>Bootstraps environments/network-access-policy.json with safe defaults when missing</summary>
public static class NetworkAccessPolicy
{
    public static string EnsurePolicyFile()
    {
        var urlValidatorPath = Path.Combine(Directory.GetCurrentDirectory(), "environments", "network-access-policy.json");
        if (!File.Exists(urlValidatorPath))
        {
            var directory = Path.GetDirectoryName(urlValidatorPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(urlValidatorPath, JsonSerializer.Serialize(new
            {
                allowedHosts = new[] { "localhost", "127.0.0.1" },
                blockedIpRanges = new[]
                {
                    "10.0.0.0/8",
                    "172.16.0.0/12",
                    "192.168.0.0/16",
                    "169.254.0.0/16"
                }
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        return urlValidatorPath;
    }
}
