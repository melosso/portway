using System.Net;
using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public class CloudflareIpRangesTests
{
    [Theory]
    [InlineData("103.21.244.1")]
    [InlineData("104.16.0.1")]
    [InlineData("172.64.0.1")]
    [InlineData("162.158.0.1")]
    [InlineData("198.41.128.1")]
    public void IsCloudflareIp_KnownCloudflareIpv4_ReturnsTrue(string ip)
        => Assert.True(CloudflareIpRanges.IsCloudflareIp(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("8.8.8.8")]
    public void IsCloudflareIp_NonCloudflareIp_ReturnsFalse(string ip)
        => Assert.False(CloudflareIpRanges.IsCloudflareIp(IPAddress.Parse(ip)));

    [Fact]
    public void IsCloudflareIp_Null_ReturnsFalse()
        => Assert.False(CloudflareIpRanges.IsCloudflareIp(null));
}
