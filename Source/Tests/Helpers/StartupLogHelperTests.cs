using System.Net;
using System.Net.Sockets;
using PortwayApi.Helpers;
using PortwayApi.Middleware;
using Xunit;

namespace PortwayApi.Tests.Helpers;

/// <summary>
/// The port preflight reports the first taken API or console port
/// </summary>
public class StartupLogHelperTests
{
    [Fact]
    public void TakenPort_IsReported()
    {
        var listener = TcpListener.Create(0);
        listener.Start();
        try
        {
            var taken = ((IPEndPoint)listener.LocalEndpoint).Port;
            Assert.Equal(taken, StartupLogHelper.FirstTakenPort([taken]));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void FreePort_IsNotReported()
    {
        var probe = TcpListener.Create(0);
        probe.Start();
        var free = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        Assert.Null(StartupLogHelper.FirstTakenPort([free]));
    }

    [Fact]
    public void HostingUrls_ReturnsApiAndConsoleUrlsPerPort()
    {
        var urls = StartupLogHelper.HostingUrls(new PortRouting(8080, 8081), useHttps: false, pathBase: null);

        Assert.Equal([("API", "http://localhost:8080/api"), ("Console", "http://localhost:8081/_")], urls);
    }

    [Fact]
    public void HostingUrls_IncludesSchemeAndPathBase()
    {
        var urls = StartupLogHelper.HostingUrls(new PortRouting(443, 443), useHttps: true, pathBase: "gateway");

        Assert.Equal([("API", "https://localhost:443/gateway/api"), ("Console", "https://localhost:443/gateway/_")], urls);
    }
}
