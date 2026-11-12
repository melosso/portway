using System.Net;
using System.Net.Sockets;
using PortwayApi.Helpers;
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
}
