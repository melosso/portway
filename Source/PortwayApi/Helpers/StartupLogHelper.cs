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
    /// The URLs Kestrel binds without explicit app.Urls: urls, the Kestrel endpoint, ASPNETCORE_HTTP_PORTS and ASPNETCORE_HTTPS_PORTS, then http://localhost:5000
    /// </summary>
    internal static string ConfiguredUrls(IConfiguration configuration)
    {
        if ((configuration["urls"] ?? configuration["Kestrel:Endpoints:Http:Url"]) is { Length: > 0 } urls)
            return urls;

        var ports = (configuration["http_ports"] ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(p => $"http://*:{p}")
            .Concat((configuration["https_ports"] ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(p => $"https://*:{p}"))
            .ToList();
        return ports.Count > 0 ? string.Join(';', ports) : "http://localhost:5000";
    }

    /// <summary>
    /// Verifies configured ports are free before Kestrel binds; returns false when a port is taken
    /// </summary>
    public static bool TryReservePorts(WebApplication app, IConfiguration configuration)
    {
        // Only Kestrel binds sockets; if the server is not Kestrel, we can't probe the ports thus return true
        var server = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        if (server.GetType().Assembly != typeof(Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions).Assembly)
            return true;

        var urlsToCheck = app.Urls.Count > 0 ? app.Urls : ConfiguredUrls(configuration).Split(';');

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

    /// <summary>
    /// Logs hosting URLs and configuration reload status
    /// </summary>
    public static void LogHostingSummary(WebApplication app, IConfiguration configuration)
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
            var formattedUrls = ConfiguredUrls(configuration).Replace(";", "; ");
            Log.Information("Application is hosted on: {Urls}", formattedUrls);
        }

        if (!app.Environment.IsProduction())
            Log.Warning("Application Environment: {Environment}", app.Environment.EnvironmentName);

        var endpointReloadEnabled = configuration.GetValue<bool>("EndpointReloading:Enabled", true);
        if (endpointReloadEnabled)
            Log.Debug("Configuration reload enabled: appsettings.json, /endpoints, /environments");
    }
}
