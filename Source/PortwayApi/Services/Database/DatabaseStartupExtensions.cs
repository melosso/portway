namespace PortwayApi.Services.Database;

using Microsoft.EntityFrameworkCore;
using PortwayApi.Auth;
using PortwayApi.Services.Caching;
using PortwayApi.Services.Mcp;
using Serilog;

/// <summary>Startup initialisation for the MCP config and auth databases plus cache connectivity logging</summary>
public static class DatabaseStartupExtensions
{
    /// <summary>Initialises mcp.db unconditionally so the setup wizard works even before Mcp:Enabled is true</summary>
    public static async Task InitializeMcpConfigDatabaseAsync(this WebApplication app)
    {
        using var mcpScope = app.Services.CreateScope();
        try
        {
            var mcpDbFactory = mcpScope.ServiceProvider
                .GetRequiredService<IDbContextFactory<McpConfigDbContext>>();
            await using var mcpDb = await mcpDbFactory.CreateDbContextAsync();
            mcpDb.Database.EnsureCreated();
            mcpDb.EnsureTablesCreated();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "MCP config database initialisation failed");
        }
    }

    /// <summary>Creates auth.db when needed and generates a default token if none exist</summary>
    public static async Task InitializeAuthDatabaseAsync(this WebApplication app, string serverName)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();

        try
        {
            // Set up database and migrate if required
            context.Database.EnsureCreated();
            context.EnsureTablesCreated();

            // Create a default token if none exist
            var activeTokens = await tokenService.GetActiveTokensAsync();
            if (!activeTokens.Any())
            {
                var token = await tokenService.GenerateTokenAsync(serverName);
                Log.Information("Token has been saved to tokens/{ServerName}.txt", serverName);
            }
            else
            {
                Log.Debug("Total active tokens: {Count}", activeTokens.Count());
                Log.Warning("Tokens detected in the tokens directory. Relocate them to a secure location to eliminate this security risk.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Database initialization failed: {Message}", ex.Message);
        }
    }

    /// <summary>Logs the configured cache provider and its connection state</summary>
    public static void LogCacheConfiguration(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        try
        {
            var cacheManager = scope.ServiceProvider.GetRequiredService<CacheManager>();
            Log.Information("Cache configured with provider: {ProviderType}", cacheManager.ProviderType);
            if (cacheManager.IsConnected)
            {
                Log.Debug("Cache connection successful");
            }
            else
            {
                Log.Warning("Cache is not connected. Caching functionality may be limited. Please enable Debug logs for more details.");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error initializing cache manager");
        }
    }
}
