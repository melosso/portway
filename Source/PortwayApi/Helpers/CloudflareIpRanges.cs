using System.Net;

namespace PortwayApi.Helpers;

/// <summary>Cloudflare published IP ranges > https://www.cloudflare.com/ips/</summary>
/// <remarks>
/// CF headers (CF-Connecting-IP, CF-Visitor) are only trustworthy when the TCP
/// connection originates from one of these ranges. Any client can send these
/// headers; gating on the real connection IP prevents spoofing
/// </remarks>
public static class CloudflareIpRanges
{
    private static readonly IPNetwork[] _ranges =
    [
        // IPv4; https://www.cloudflare.com/ips-v4
        IPNetwork.Parse("103.21.244.0/22"),
        IPNetwork.Parse("103.22.200.0/22"),
        IPNetwork.Parse("103.31.4.0/22"),
        IPNetwork.Parse("104.16.0.0/13"),
        IPNetwork.Parse("104.24.0.0/14"),
        IPNetwork.Parse("108.162.192.0/18"),
        IPNetwork.Parse("131.0.72.0/22"),
        IPNetwork.Parse("141.101.64.0/18"),
        IPNetwork.Parse("162.158.0.0/15"),
        IPNetwork.Parse("172.64.0.0/13"),
        IPNetwork.Parse("173.245.48.0/20"),
        IPNetwork.Parse("188.114.96.0/20"),
        IPNetwork.Parse("190.93.240.0/20"),
        IPNetwork.Parse("197.234.240.0/22"),
        IPNetwork.Parse("198.41.128.0/17"),

        // IPv6; https://www.cloudflare.com/ips-v6
        IPNetwork.Parse("2400:cb00::/32"),
        IPNetwork.Parse("2405:8100::/32"),
        IPNetwork.Parse("2405:b500::/32"),
        IPNetwork.Parse("2606:4700::/32"),
        IPNetwork.Parse("2803:f800::/32"),
        IPNetwork.Parse("2c0f:f248::/32"),
        IPNetwork.Parse("2a06:98c0::/29"),
    ];

    public static bool IsCloudflareIp(IPAddress? ip)
    {
        if (ip is null) return false;
        // Map IPv4-mapped IPv6 addresses to plain IPv4 before comparing
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        foreach (var range in _ranges)
            if (range.Contains(ip)) return true;
        return false;
    }
}
