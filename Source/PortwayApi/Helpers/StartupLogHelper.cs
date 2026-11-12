using PortwayApi.Middleware;
using Serilog;

namespace PortwayApi.Helpers;

/// <summary>
/// Startup banner, port availability preflight and hosting URL logging
/// </summary>
public static class StartupLogHelper
{
    public static void LogApplicationBanner(string version)
    {
        Log.Information("");
        Log.Information("Portway {Version} - Remote integration gateway built on .NET", version);
        Log.Information("");
        Log.Debug("Host {Host} ({OS}, .NET {DotNet})", Environment.MachineName, Environment.OSVersion.Platform, Environment.Version);
    }

    /// <summary>
    /// Verifies the API and console ports are free before Kestrel binds; returns false when a port is taken
    /// </summary>
    public static bool TryReservePorts(WebApplication app, PortRouting ports)
    {
        // iis in-process binds its own ports
        var server = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        if (server.GetType().Assembly != typeof(Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions).Assembly)
            return true;

        if (FirstTakenPort(ports.IsSplit ? [ports.ApiPort, ports.ConsolePort] : [ports.ApiPort]) is not { } taken)
            return true;

        Log.Fatal("Port {Port} is already in use. Stop the existing process, or set PORTWAY_PORT_API and PORTWAY_PORT_CONSOLE.", taken);
        return false;
    }

    internal static int? FirstTakenPort(IEnumerable<int> ports)
    {
        foreach (var port in ports)
        {
            try
            {
                var probe = System.Net.Sockets.TcpListener.Create(port);
                probe.Start();
                probe.Stop();
            }
            catch (System.Net.Sockets.SocketException ex)
                when (ex.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
            {
                return port;
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                // anything else, a reserved port or a blocked address, is Kestrel's to report
                Log.Debug(ex, "Could not probe port {Port}; leaving the bind to Kestrel", port);
            }
        }

        return null;
    }

    /// <summary>
    /// Logs hosting URLs and configuration reload status
    /// </summary>
    public static void LogHostingSummary(WebApplication app, IConfiguration configuration, PortRouting ports)
    {
        if (app.Environment.IsProduction() && Environment.GetEnvironmentVariable("ASPNETCORE_IIS_PHYSICAL_PATH") != null)
            Log.Debug("Application is hosted in IIS");
        else if (ports.IsSplit)
            Log.Information("Application is hosted on API port {ApiPort} and console port {ConsolePort}", ports.ApiPort, ports.ConsolePort);
        else
            Log.Information("Application is hosted on port {Port} for API and console", ports.ApiPort);

        if (!app.Environment.IsProduction())
            Log.Warning("Application Environment: {Environment}", app.Environment.EnvironmentName);

        var endpointReloadEnabled = configuration.GetValue<bool>("EndpointReloading:Enabled", true);
        if (endpointReloadEnabled)
            Log.Debug("Configuration reload enabled: appsettings.json, /endpoints, /environments");
    }
}
