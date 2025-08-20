namespace PortwayApi.Helpers;

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using Serilog;

public class UrlValidator
{
    private readonly List<string> _allowedHosts;
    private readonly List<string> _blockedRanges;
    private readonly ConcurrentDictionary<string, bool> _hostCache;
    private readonly ConcurrentDictionary<string, IPAddress[]> _dnsCache = new();

    public UrlValidator(string configPath)
    {
        _hostCache = new ConcurrentDictionary<string, bool>();

        EnsureConfigFileExists(configPath);

        var config = JsonSerializer.Deserialize<HostConfig>(
            File.ReadAllText(configPath), 
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        if (config == null)
        {
            throw new InvalidOperationException("Configuration could not be loaded.");
        }

        // Prioritize hosts from configuration
        _allowedHosts = config.AllowedHosts?.Count > 0
            ? config.AllowedHosts
            : new List<string> 
            { 
                "localhost", 
                "127.0.0.1" 
            };

        // Only add discovered hosts if no hosts are specified in config
        if (_allowedHosts.Count <= 2) // default localhost hosts
        {
            _allowedHosts.AddRange(DiscoverAllowedHosts());
        }

        _blockedRanges = config.BlockedIpRanges?.Count > 0
            ? config.BlockedIpRanges
            : new List<string> 
            { 
                "10.0.0.0/8",
                "172.16.0.0/12",
                "192.168.0.0/16",
                "169.254.0.0/16"
            };

        Log.Information("🔒 URL Validator configured with allowed hosts: {Hosts}", 
            string.Join(", ", _allowedHosts));
    }

    /// <summary>
    /// Discovers allowed hosts based on the local machine name and network interfaces.
    /// </summary>
    private List<string> DiscoverAllowedHosts()
    {
        var discoveredHosts = new HashSet<string>();

        try
        {
            // Get local machine name
            discoveredHosts.Add(Environment.MachineName.ToLowerInvariant());

            // Get all network interfaces and their DNS addresses
            var networkInterfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            foreach (var network in networkInterfaces)
            {
                // Skip loopback and non-operational interfaces
                if (network.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                    continue;

                // Get IP properties
                var ipProperties = network.GetIPProperties();

                // Add unicast addresses
                foreach (var unicast in ipProperties.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        // Add IP address
                        discoveredHosts.Add(unicast.Address.ToString());

                        // Try to get hostname for the IP
                        try
                        {
                            var hostEntry = Dns.GetHostEntry(unicast.Address);
                            if (!string.IsNullOrEmpty(hostEntry.HostName))
                            {
                                discoveredHosts.Add(hostEntry.HostName.ToLowerInvariant());
                            }
                        }
                        catch
                        {
                            // Ignore DNS resolution errors
                            Log.Warning("Failed to resolve hostname for IP: {Ip}", unicast.Address);
                        }
                    }
                }
            }

            // Add any configured domain if available
            var configuredDomain = GetConfiguredDomain();
            if (!string.IsNullOrEmpty(configuredDomain))
            {
                discoveredHosts.Add(configuredDomain.ToLowerInvariant());
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error discovering allowed hosts");
        }

        return discoveredHosts.ToList();
    }

    private string GetConfiguredDomain()
    {
        try
        {
            var envDomain = Environment.GetEnvironmentVariable("ASPNETCORE_DOMAIN");
            if (!string.IsNullOrEmpty(envDomain))
                return envDomain;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error getting configured domain");
        }

        return string.Empty;
    }

    /// <summary>  
    /// Validates if the given URL is safe to access.  
    /// </summary>
    public bool IsUrlSafe(string url)
    {
        try
        {
            var uri = new Uri(url);
            string host = uri.Host.Split(':')[0];
            
            Log.Debug("🕵️ Validating URL: {Url}", url);
            Log.Debug("🏠 Host to validate: {Host}", host);
            
            var addresses = Dns.GetHostAddresses(host);
            Log.Debug("🌐 Resolved Addresses: {Addresses}", 
                string.Join(", ", addresses.Select(a => a.ToString())));
            
            // Track blocked IPs for detailed logging
            var blockedIps = new List<string>();
            bool anyIpBlocked = addresses.Any(ip => 
            {
                bool isBlocked = _blockedRanges.Any(range => 
                {
                    bool inRange = IsIpInRange(ip, range);
                    if (inRange)
                    {
                        blockedIps.Add($"{ip} in range {range}");
                    }
                    return inRange;
                });
                return isBlocked;
            });
                
            if (anyIpBlocked)
            {
                Log.Warning("❌ Host {Host} blocked due to IP restrictions", host);
                Log.Warning("🚫 Blocked IP Details: {BlockedIpDetails}", 
                    string.Join(", ", blockedIps));
                return false;
            }
            
            bool isHostAllowed = _allowedHosts.Any(allowed => 
                string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase) ||
                MatchHostPattern(host, allowed));
                
            if (!isHostAllowed)
            {
                Log.Warning("❌ Host {Host} is NOT in allowed hosts", host);
                Log.Warning("📋 Allowed Hosts: {AllowedHosts}", 
                    string.Join(", ", _allowedHosts));
                return false;
            }
            
            Log.Debug("✅ URL {Url} validated successfully", url);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ URL Validation Error for {Url}", url);
            return false;
        }
    }

    /// <summary>
    /// Validates if the given host is allowed based on the configuration.
    /// </summary>
    public bool IsHostAllowed(string host)
    {
        if (_hostCache.TryGetValue(host, out bool isAllowed))
            return isAllowed;

        if (IsHostPatternAllowed(host))
        {
            _hostCache[host] = true;
            return true;
        }

        bool isValid = ValidateHost(host);
        _hostCache[host] = isValid;
        return isValid;
    }
    
    /// <summary>
    /// Ensures that the configuration file exists. If not, creates a default one.
    /// </summary>
    private void EnsureConfigFileExists(string configPath)
    {
        if (!File.Exists(configPath))
        {
            Log.Warning("Configuration file not found at {ConfigPath}. Creating default.", configPath);
            var directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            File.WriteAllText(configPath, JsonSerializer.Serialize(new HostConfig()));
        }
    }

    /// <summary>
    /// Validates the host against the allowed hosts and blocked IP ranges.
    /// </summary>
    private bool ValidateHost(string host)
    {
         // Resolve DNS and check IP ranges
        var addresses = ResolveDnsWithCache(host);
        return addresses.All(IsIpAllowed);
    }

    /// <summary>
    /// Checks if the host matches any of the allowed patterns.
    /// </summary>
    private bool IsHostPatternAllowed(string host)
    {
        return _allowedHosts.Any(pattern => 
            MatchHostPattern(host, pattern));
    }

    /// <summary>
    /// Helper method to match host against a pattern.
    /// </summary>
    private bool MatchHostPattern(string host, string pattern)
    {
        // Compiled regex for performance
        var regex = new Regex(
            "^" + Regex.Escape(pattern)
                .Replace(@"\*", "[^.]*") + "$", 
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );
        return regex.IsMatch(host);
    }

    /// <summary>
    /// Resolves the DNS for the given host and caches the result.
    /// </summary>
    private IPAddress[] ResolveDnsWithCache(string host)
    {
        return _dnsCache.GetOrAdd(host, key => 
        {
            try 
            {
                return Dns.GetHostAddresses(key) ?? Array.Empty<IPAddress>();
            }
            catch
            {
                return Array.Empty<IPAddress>();
            }
        });
    }

    /// <summary>
    /// Helper method to check if the given IP address is allowed based on the blocked ranges.
    /// </summary>
    private bool IsIpAllowed(IPAddress ip)
    {
        // Check against blocked ranges with detailed logging
        var blockedBy = _blockedRanges
            .Where(range => IsIpInRange(ip, range))
            .ToList();

        if (blockedBy.Any())
        {
            Log.Warning("🚫 IP {IpAddress} is blocked by the following ranges: {BlockedRanges}", 
                ip, string.Join(", ", blockedBy));
            return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if the given IP address is within the specified CIDR range.
    /// </summary>
    private bool IsIpInRange(IPAddress ip, string cidrRange)
    {
        try
        {
            var parts = cidrRange.Split('/');
            var baseIp = IPAddress.Parse(parts[0]);
            var cidrBits = int.Parse(parts[1]);

            // Convert IPs to byte arrays for comparison
            byte[] ipBytes = ip.GetAddressBytes();
            byte[] baseIpBytes = baseIp.GetAddressBytes();

            // Ensure we're comparing the same IP type (IPv4)
            if (ipBytes.Length != baseIpBytes.Length)
                return false;

            // Create subnet mask
            byte[] maskBytes = new byte[ipBytes.Length];
            for (int i = 0; i < maskBytes.Length; i++)
            {
                int bitStart = i * 8;
                int bitEnd = Math.Min(bitStart + 8, cidrBits);
                int bits = bitEnd - bitStart;
                maskBytes[i] = (byte)(bits > 0 ? ((0xFF << (8 - bits)) & 0xFF) : 0);
            }

            // Compare masked IP addresses
            for (int i = 0; i < ipBytes.Length; i++)
            {
                if ((ipBytes[i] & maskBytes[i]) != (baseIpBytes[i] & maskBytes[i]))
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private class HostConfig
    {
        public List<string>? AllowedHosts { get; set; }
        public List<string>? BlockedIpRanges { get; set; }
    }
}