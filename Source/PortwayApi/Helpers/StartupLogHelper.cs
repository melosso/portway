using Serilog;

namespace PortwayApi.Helpers;

/// <summary>Startup banner, port availability preflight and hosting URL logging</summary>
public static class StartupLogHelper
{
    // Single log event so sinks cannot interleave the banner with other startup lines
    private const string Banner = @"
 ██████╗  ██████╗ ██████╗ ████████╗██╗    ██╗ █████╗ ██╗   ██╗
 ██╔══██╗██╔═══██╗██╔══██╗╚══██╔══╝██║    ██║██╔══██╗╚██╗ ██╔╝
 ██████╔╝██║   ██║██████╔╝   ██║   ██║ █╗ ██║███████║ ╚████╔╝
 ██╔═══╝ ██║   ██║██╔══██╗   ██║   ██║███╗██║██╔══██║  ╚██╔╝
 ██║     ╚██████╔╝██║  ██║   ██║   ╚███╔███╔╝██║  ██║   ██║
 ╚═╝      ╚═════╝ ╚═╝  ╚═╝   ╚═╝    ╚══╝╚══╝ ╚═╝  ╚═╝   ╚═╝";

    public static void LogAsciiBanner(string version)
    {
        Log.Information("{Banner}", Banner);
        Log.Information("Portway {Version} starting on {Host} ({OS}, .NET {DotNet})",
            version, Environment.MachineName, Environment.OSVersion.Platform, Environment.Version);
    }

    /// <summary>Verifies configured ports are free before Kestrel binds; returns false when a port is taken</summary>
    public static bool TryReservePorts(WebApplication app, IConfiguration configuration)
    {
        // Same order the host itself resolves: --urls beats ASPNETCORE_URLS, and configuration
        // holds both because the ASPNETCORE_ prefix is stripped into the "urls" key
        var urlsToCheck = app.Urls.Count > 0
            ? app.Urls
            : (configuration["urls"]
                ?? configuration["Kestrel:Endpoints:Http:Url"]
                ?? "http://localhost:5000").Split(';');

        foreach (var rawUrl in urlsToCheck)
        {
            if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri)) continue;
            if (uri.Scheme is not ("http" or "https")) continue;

            try
            {
                var address = uri.Host is "localhost" or "0.0.0.0" or "*" or "+"
                    ? System.Net.IPAddress.Loopback
                    : System.Net.IPAddress.Parse(uri.Host);

                using var probe = new System.Net.Sockets.TcpListener(address, uri.Port);
                probe.Start();
                probe.Stop();
            }
            catch (System.Net.Sockets.SocketException ex)
                when (ex.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
            {
                Log.Fatal("Port {Port} is already in use. Stop the existing process, or pass --urls to use another port.", uri.Port);
                return false;
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                // Anything else, a reserved port or a blocked address, is Kestrel's to report
                Log.Debug(ex, "Could not probe {Url}; leaving the bind to Kestrel", rawUrl);
            }
        }

        return true;
    }

    /// <summary>Logs hosting URLs, Web UI auth status and configuration reload status</summary>
    public static void LogHostingSummary(WebApplication app, IConfiguration configuration, string adminApiKey)
    {
        var urls = app.Urls;
        if (urls != null && urls.Any())
        {
            Log.Information("Application is hosted on the following URLs:");
            foreach (var url in urls)
            {
                Log.Information("   {Url}", url);
            }
        }
        else if (app.Environment.IsProduction() && Environment.GetEnvironmentVariable("ASPNETCORE_IIS_PHYSICAL_PATH") != null)
        {
            // We're running in IIS
            Log.Debug("Application is hosted in IIS");
        }
        else
        {
            // Same order as TryReservePorts, so the banner names the ports actually bound
            var serverUrls = configuration["urls"]
                ?? configuration["Kestrel:Endpoints:Http:Url"]
                ?? "http://localhost:5000";

            var formattedUrls = serverUrls.Replace(";", "; ");
            Log.Information("Application is hosted on: {Urls}", formattedUrls);
        }

        var webUiAuthStatus = string.IsNullOrEmpty(adminApiKey) ? "Disabled" : "Enabled";
        Log.Information("Web UI: {Status}", webUiAuthStatus);

        var endpointReloadEnabled = configuration.GetValue<bool>("EndpointReloading:Enabled", true);
        if (endpointReloadEnabled)
            Log.Information("Configuration reload enabled: appsettings.json, /endpoints, /environments");
    }
}
