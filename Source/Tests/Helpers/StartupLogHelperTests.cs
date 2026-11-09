using Microsoft.Extensions.Configuration;
using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

/// <summary>
/// The startup banner and port preflight name the URLs Kestrel binds
/// </summary>
public class StartupLogHelperTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    [Fact]
    public void ContainerHttpPorts_AreUsed() =>
        Assert.Equal("http://*:8080", StartupLogHelper.ConfiguredUrls(Config(("http_ports", "8080"))));

    [Fact]
    public void HttpAndHttpsPorts_AreCombined() =>
        Assert.Equal("http://*:8080;http://*:8081;https://*:8443",
            StartupLogHelper.ConfiguredUrls(Config(("http_ports", "8080;8081"), ("https_ports", "8443"))));

    [Fact]
    public void Urls_WinOverPorts() =>
        Assert.Equal("http://0.0.0.0:9000", StartupLogHelper.ConfiguredUrls(Config(("urls", "http://0.0.0.0:9000"), ("http_ports", "8080"))));

    [Fact]
    public void NothingConfigured_FallsBackToKestrelDefault() =>
        Assert.Equal("http://localhost:5000", StartupLogHelper.ConfiguredUrls(Config()));
}
