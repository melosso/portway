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
    /// Initializes the auth database, seeds the initial admin account, and writes a default token file if none exist and the console is disabled.
    /// </summary>
    public static async Task InitializeAuthDatabaseAsync(this WebApplication app, string serverName, PortwayApi.Helpers.AdminSeedKey adminApiKey, bool webUiEnabled, string? seedPassword = null)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();
        var users = scope.ServiceProvider.GetRequiredService<AdminUserService>();

        context.Database.EnsureCreated();
        context.EnsureTablesCreated();

        await users.SeedFirstAccountAsync(adminApiKey, seedPassword);
        Helpers.WebUiAuthState.Enabled = await users.CountAsync() > 0;

        try
        {
            var activeTokens = await tokenService.GetActiveTokensAsync();
            if (activeTokens.Any())
            {
                Log.Debug("Total active tokens: {Count}", activeTokens.Count());
                var tokensDir = Path.Combine(Directory.GetCurrentDirectory(), "tokens");
                if (Directory.Exists(tokensDir) && Directory.EnumerateFiles(tokensDir, "*.txt").Any())
                    Log.Warning("Tokens detected in the tokens directory; relocate them to a secure location");
            }
            else if (webUiEnabled)
            {
                Log.Information("No access tokens exist; create one in the console under Access Tokens");
            }
            else
            {
                var token = await tokenService.GenerateTokenAsync(serverName);
                await tokenService.SaveTokenToFileAsync(serverName, token);
                Log.Information("Token has been saved to tokens/{ServerName}.txt", serverName);
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