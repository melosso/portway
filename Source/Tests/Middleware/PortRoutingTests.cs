using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using PortwayApi.Middleware;
using Xunit;

namespace PortwayApi.Tests.Middleware;

/// <summary>
/// The API port never serves the console; the console port and a shared port serve everything
/// </summary>
public class PortRoutingTests
{
    private static readonly PortRouting Split = new(8080, 8081);

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    [Theory]
    [InlineData("/_")]
    [InlineData("/_/login")]
    [InlineData("/_/api/tokens")]
    [InlineData("/_/API/users")]
    [InlineData("/index.html")]
    public void ConsolePaths_AreDeniedOnApiPort(string path) =>
        Assert.True(Split.IsDenied(8080, new PathString(path)));

    [Theory]
    [InlineData("/api/prod/Accounts")]
    [InlineData("/docs")]
    [InlineData("/health/live")]
    [InlineData("/mcp")]
    [InlineData("/_x")]
    [InlineData("/css/site.css")]
    public void ApiPaths_AreServedOnApiPort(string path) =>
        Assert.False(Split.IsDenied(8080, new PathString(path)));

    [Theory]
    [InlineData("/_/login")]
    [InlineData("/api/prod/Accounts")]
    public void ConsolePort_ServesEverything(string path) =>
        Assert.False(Split.IsDenied(8081, new PathString(path)));

    [Fact]
    public void SharedPort_ServesEverything() =>
        Assert.False(new PortRouting(8080, 8080).IsDenied(8080, new PathString("/_/login")));

    [Fact]
    public void UnknownPort_ServesEverything() =>
        Assert.False(Split.IsDenied(443, new PathString("/_/login")));

    [Fact]
    public void Defaults_AreSplit() =>
        Assert.Equal(new PortRouting(8080, 8081), PortRouting.From(Config()));

    [Fact]
    public void Ports_AreReadFromHosting() =>
        Assert.Equal(new PortRouting(9000, 9001), PortRouting.From(Config(("Hosting:ApiPort", "9000"), ("Hosting:ConsolePort", "9001"))));

    [Theory]
    [InlineData("0")]
    [InlineData("70000")]
    public void OutOfRangePort_FailsStartup(string port) =>
        Assert.Throws<InvalidOperationException>(() => PortRouting.From(Config(("Hosting:ApiPort", port))));
}
