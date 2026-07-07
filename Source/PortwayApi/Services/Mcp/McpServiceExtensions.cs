namespace PortwayApi.Services.Mcp;

using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

public static class McpServiceExtensions
{
    public static IServiceCollection AddMcpServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Registered unconditionally so /ui/mcp/setup and the settings endpoint can read/write config before Mcp:Enabled is true
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
            services.AddSingleton<McpAppsResourceProvider>();
            services.AddSingleton<McpChatService>();

            // "internal" client: tool-execution calls back into the Portway API; timeout configurable, default 30 s
            var toolTimeout = configuration.GetValue<int>("Mcp:ToolTimeoutSeconds", 30);
            services.AddHttpClient("internal", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(toolTimeout > 0 ? toolTimeout : 30);
            });

            services.AddMcpServer()
                .WithHttpTransport();
        }

        return services;
    }

    public static IEndpointRouteBuilder MapMcpEndpoints(this IEndpointRouteBuilder app, IConfiguration configuration)
    {
        var mcpEnabled = configuration.GetValue<bool>("Mcp:Enabled", false);
        if (mcpEnabled)
        {
            var mcpPath = configuration.GetValue<string>("Mcp:Path") ?? "/mcp";
            app.MapMcp(mcpPath);
        }

        return app;
    }
}
