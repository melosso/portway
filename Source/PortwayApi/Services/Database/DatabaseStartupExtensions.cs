namespace PortwayApi.Services.Database;

using Microsoft.EntityFrameworkCore;
using PortwayApi.Auth;
using PortwayApi.Services.Caching;
using PortwayApi.Services.Mcp;
using Serilog;

/// <summary>
/// Startup extensions for database initialization and cache connection logging.
/// </summary>
public static class DatabaseStartupExtensions
{
    /// <summary>
    /// Initializes the MCP configuration database.
    /// </summary>
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

    /// <summary>
    /// Initializes the auth database, seeds the initial admin account, and generates a default token if none exist.
    /// </summary>
    public static async Task InitializeAuthDatabaseAsync(this WebApplication app, string serverName, PortwayApi.Helpers.AdminSeedKey adminApiKey, string? seedPassword = null)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();
        var users = scope.ServiceProvider.GetRequiredService<AdminUserService>();

        context.Database.EnsureCreated();
        context.EnsureTablesCreated();

        await users.SeedFirstAccountAsync(adminApiKey, seedPassword);
        PortwayApi.Helpers.WebUiAuthState.Enabled = await users.CountAsync() > 0;

        try
        {
            var activeTokens = await tokenService.GetActiveTokensAsync();
            if (!activeTokens.Any())
            {
                var token = await tokenService.GenerateTokenAsync(serverName);
                Log.Information("Token has been saved to tokens/{ServerName}.txt", serverName);
            }
            else
            {
                Log.Debug("Total active tokens: {Count}", activeTokens.Count());
                Log.Warning("Tokens detected in the tokens directory; relocate them to a secure location");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not create the default token");
        }
    }

    /// <summary>
    /// Logs configured cache provider status and connection state.
    /// </summary>
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
                Log.Warning("Cache is not connected; caching functionality may be limited, enable Debug logs for details");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error initializing cache manager");
        }
    }
}