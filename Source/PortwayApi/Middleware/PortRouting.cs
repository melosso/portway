using Microsoft.Extensions.Options;
using PortwayApi.Classes.OpenApi;

namespace PortwayApi.Middleware;

public sealed record PortRouting(int ApiPort, int ConsolePort)
{
    public const int DefaultApiPort = 8080;
    public const int DefaultConsolePort = 8081;

    public bool IsSplit => ApiPort != ConsolePort;

    public static PortRouting From(IConfiguration configuration) => new(
        Validate(configuration.GetValue("Hosting:ApiPort", DefaultApiPort), "Hosting:ApiPort"),
        Validate(configuration.GetValue("Hosting:ConsolePort", DefaultConsolePort), "Hosting:ConsolePort"));

    public bool IsApiPort(int localPort) => IsSplit && localPort == ApiPort;

    public bool IsDenied(int localPort, PathString path) =>
        IsApiPort(localPort)
        && (path.StartsWithSegments("/_", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/index.html", StringComparison.OrdinalIgnoreCase));

    private static int Validate(int port, string key) =>
        port is > 0 and <= 65535 ? port : throw new InvalidOperationException($"{key} must be between 1 and 65535, got {port}.");
}

public static class PortRoutingExtensions
{
    public static WebApplication UsePortRouting(this WebApplication app)
    {
        var ports = app.Services.GetRequiredService<PortRouting>();
        if (!ports.IsSplit)
            return app;
        var docsEnabled = app.Services.GetRequiredService<IOptionsMonitor<OpenApiSettings>>().CurrentValue.Enabled;

        app.Use((context, next) =>
        {
            var localPort = context.Connection.LocalPort;
            if (ports.IsDenied(localPort, context.Request.Path))
                return FallbackEndpointExtensions.WriteNotFoundAsync(context);

            if (ports.IsApiPort(localPort) && context.Request.Path == "/")
            {
                if (!docsEnabled)
                    return FallbackEndpointExtensions.WriteNotFoundAsync(context);
                context.Response.Redirect($"{context.Request.PathBase}/docs");
                return Task.CompletedTask;
            }

            return next(context);
        });
        return app;
    }
}
