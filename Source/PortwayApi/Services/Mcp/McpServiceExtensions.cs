namespace PortwayApi.Services.Mcp;

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Protocol;
using PortwayApi.Helpers;

public static class McpServiceExtensions
{
    public static IServiceCollection AddMcpServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Registered unconditionally so /_/mcp/setup and the settings endpoint can read/write config before Mcp:Enabled is true
        var mcpDbPath = Path.Combine(Directory.GetCurrentDirectory(), "mcp.db");
        services.AddDbContextFactory<McpConfigDbContext>(opts =>
            opts.UseSqlite($"Data Source={mcpDbPath}"));
        services.AddSingleton<McpConfigService>();

        // "mcp" client: external AI provider calls; no fixed timeout, providers handle their own
        services.AddHttpClient("mcp");

        var mcpEnabled = configuration.GetValue<bool>("Mcp:Enabled", false);
        if (mcpEnabled)
        {
            services.Configure<McpOptions>(configuration.GetSection("Mcp"));
            services.AddSingleton<McpEndpointRegistry>();
            services.AddSingleton<McpChatService>();

            // "internal" client: tool-execution calls back into the Portway API; timeout configurable, default 30 s
            var toolTimeout = configuration.GetValue<int>("Mcp:ToolTimeoutSeconds", 30);
            services.AddHttpClient("internal", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(toolTimeout > 0 ? toolTimeout : 30);
            });

            services.AddMcpServer(o => o.ServerInfo = new Implementation { Name = "Portway", Version = ServerVersion() })
                // stateless explicit not implied
                .WithHttpTransport(o => o.Stateless = true)
                .WithToolsFromAssembly();
        }

        return services;
    }

    public static IEndpointRouteBuilder MapMcpEndpoints(this IEndpointRouteBuilder app, IConfiguration configuration)
    {
        var mcpEnabled = configuration.GetValue<bool>("Mcp:Enabled", false);
        if (mcpEnabled)
        {
            var mcpPath = configuration.GetValue<string>("Mcp:Path") ?? "/mcp";
            var allowedOrigins = configuration.GetSection("Console:CorsOrigins").Get<string[]>() ?? [];
            app.MapMcp(mcpPath).Add(endpoint =>
            {
                var next = endpoint.RequestDelegate!;
                endpoint.RequestDelegate = async context =>
                {
                    if (IsOriginAllowed(context.Request.Headers.Origin.ToString(), allowedOrigins))
                    {
                        await next(context);
                        return;
                    }

                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(ErrorResponse.Of("Origin not allowed"));
                };
            });
        }

        return app;
    }

    internal static string ServerVersion() =>
        typeof(McpServiceExtensions).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    /// <summary>
    /// Clients outside a browser send no Origin. A browser origin requires an entry in Console:CorsOrigins
    /// </summary>
    internal static bool IsOriginAllowed(string origin, IReadOnlyCollection<string> allowedOrigins) =>
        origin.Length == 0 || allowedOrigins.Any(a => a.TrimEnd('/').Equals(origin, StringComparison.OrdinalIgnoreCase));
}
