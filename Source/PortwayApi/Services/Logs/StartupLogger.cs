using Microsoft.Extensions.Hosting;
using Serilog;
using System.Text;

namespace PortwayApi.Services;

/// <summary>
/// Background service that logs application startup information
/// </summary>
public class StartupLogger : IHostedService
{
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly IConfiguration _configuration;

    public StartupLogger(
        IHostApplicationLifetime appLifetime,
        IConfiguration configuration)
    {
        _appLifetime = appLifetime;
        _configuration = configuration;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Register a callback that will be invoked when the application has started
        _appLifetime.ApplicationStarted.Register(OnApplicationStarted);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void OnApplicationStarted()
    {
        try
        {
            LogEnvironmentInfo();
            LogConfigurationInfo();
            LogApplicationStartup();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during startup logging");
        }
    }

    private void LogApplicationStartup()
    {
        Log.Information("✅ Portway has started successfully");
    }  

    private void LogEnvironmentInfo()
    {
        try
        {
            var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
            Log.Information("🌎 Environment: {Environment}", env);
            Log.Debug("🖥️ Host: {MachineName}", Environment.MachineName);
            Log.Debug("💾 Working Directory: {WorkingDirectory}", Directory.GetCurrentDirectory());
            Log.Debug("⏰ Current Time: {Time}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Log.Debug("🔧 .NET Version: {DotNetVersion}", Environment.Version);
            Log.Debug("💻 OS: {OS}", Environment.OSVersion);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to log complete environment information");
        }
    }

    private void LogConfigurationInfo()
    {
        try
        {
            // Log connection string providers (not the actual connection strings)
            var keyvaultConfigured = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KEYVAULT_URI"));
            Log.Information("🔐 Using Azure Key Vault: {IsConfigured}", keyvaultConfigured ? "Yes" : "No");

            // Log allowed environments
            var environmentsSection = _configuration.GetSection("Environment:AllowedEnvironments");
            if (environmentsSection.Exists())
            {
                var environments = environmentsSection.GetChildren().Select(c => c.Value).Where(v => v != null).ToList();
                Log.Information("🌍 Allowed Environments: {Environments}", string.Join(", ", environments));
            }

            // Log rate limiting status
            var rateLimitingEnabled = _configuration.GetValue<bool>("RateLimiting:Enabled", false);
            if (rateLimitingEnabled)
            {
                var ipLimit = _configuration.GetValue<int>("RateLimiting:IpLimit", 100);
                var tokenLimit = _configuration.GetValue<int>("RateLimiting:TokenLimit", 1000);
                Log.Information("🚦 Rate Limiting: Enabled (IP: {IpLimit}/min, Token: {TokenLimit}/min)", ipLimit, tokenLimit);
            }
            else
            {
                Log.Information("🚦 Rate Limiting: Disabled");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to log complete configuration information");
        }
    }
}