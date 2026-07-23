using Microsoft.Extensions.Options;
using PortwayApi.Api;
using PortwayApi.Auth;
using PortwayApi.Classes;
using PortwayApi.Services.Configuration;
using PortwayApi.Endpoints;
using PortwayApi.Interfaces;
using PortwayApi.Services;
using PortwayApi.Services.Files;
using PortwayApi.Helpers;
using PortwayApi.Middleware;
using PortwayApi.Services.Caching;
using PortwayApi.Services.Database;
using PortwayApi.Services.Health;
using PortwayApi.Services.Mcp;
using PortwayApi.Services.Providers;
using PortwayApi.Services.Telemetry;
using PortwayApi.Services.Telemetry.Prometheus;
using Serilog;
using System.Reflection;

// Create log directory
Directory.CreateDirectory("log");

// Spawn the main application
try
{
    var builder = WebApplication.CreateBuilder(args);

    Log.Logger = new LoggerConfiguration()
        .ReadFrom.Configuration(builder.Configuration)
        .Filter.ByExcluding(logEvent =>
            logEvent.Properties.ContainsKey("RequestPath") &&
            (logEvent.Properties["RequestPath"].ToString().Contains("/docs") ||
                logEvent.Properties["RequestPath"].ToString().Contains("/index.html")))
        .CreateLogger();

    builder.Host.UseSerilog();

    builder.Services.Configure<HostOptions>(opts =>
        {
            opts.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
        }
    );

    var assemblyVersion = typeof(Program).Assembly
        .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion?.Split('+')[0] ?? "0.0.0";

    StartupLogHelper.LogAsciiBanner(assemblyVersion);

    // Kestrel hardening, HTTPS opt-in detection and response compression
    builder.ConfigurePortwayWebHost();

    // Add services
    builder.Services.AddControllers();
    builder.Services.AddResponseCaching(options =>
    {
        options.UseCaseSensitivePaths = true;
        options.SizeLimit = 1024 * 1024 * 10; // 10 MB
        options.MaximumBodySize = 1024 * 1024 * 10; // 10 MB
    });

    // Add caching services (Redis and/or memory cache)
    builder.Services.AddCachingServices(builder.Configuration);

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddRequestTrafficLogging(builder.Configuration);
    builder.Services.AddHttpContextAccessor();

    // CORS allowlist from WebUi:CorsOrigins; never AllowAnyOrigin in production
    builder.AddPortwayCors();

    builder.Services.AddPortwayTelemetry(builder.Configuration, assemblyVersion);

    // Define server name
    string serverName = Environment.MachineName;

    // Configure logging
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options => options.FormatterName = "simple");
    builder.Logging.AddSimpleConsole(options => options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ");

    // Register Serilog logger for dependency injection 
    builder.Services.AddSingleton<Serilog.ILogger>(sp => Log.Logger);

    // Authentication and configuration reload
    builder.Services.AddPortwayAuth();
    builder.Services.AddPortwayConfigurationReload(builder.Configuration);

    // Nightly SQLite self-tuning for auth, metrics, MCP and traffic databases
    builder.Services.AddPortwayDatabaseMaintenance(builder.Configuration);

    builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection("FileStorage"));

    // HTTP client and SQL/OData services
    builder.Services.AddPortwayProxyHttpClient(builder.Configuration);
    builder.Services.AddPortwaySqlServices(builder.Configuration);
    
    // MCP services (conditionally enabled)
    builder.Services.AddMcpServices(builder.Configuration);

    // Initialize endpoints directories
    DirectoryHelper.EnsureDirectoryStructure();

    // Load Proxy endpoints
    var proxyEndpointsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Proxy");
    var proxyEndpointMap = EndpointHandler.GetEndpoints(proxyEndpointsDirectory);

    // Register CompositeEndpointHandler with loaded endpoints
    builder.Services.AddSingleton<CompositeEndpointHandler>(provider =>
        new CompositeEndpointHandler(
            provider.GetRequiredService<IHttpClientFactory>(),
            proxyEndpointMap,
            serverName
        )
    );

    // Central endpoint resolution and per-type request handlers
    builder.Services.AddSingleton<IEndpointRegistry, EndpointRegistry>();
    builder.Services.AddSingleton<EndpointResolver>();
    builder.Services.AddSingleton<CompositeRequestHandler>();
    builder.Services.AddSingleton<StaticRequestHandler>();
    builder.Services.AddSingleton<SqlRequestHandler>();
    builder.Services.AddSingleton<ProxyRequestHandler>();

    // Configure Rate Limiting
    builder.Services.AddRateLimiting(builder.Configuration);

    // Configure SSRF protection
    var urlValidatorPath = NetworkAccessPolicy.EnsurePolicyFile();
    var urlValidator = new UrlValidator(urlValidatorPath);
    builder.Services.AddSingleton(urlValidator);
    builder.Services.AddPortwayHealthServices();

    OpenApiConfiguration.ConfigureOpenApi(builder);

    // Build the application
    var app = builder.Build();

    // Web UI admin key; placeholder rejected in production, empty disables Web UI auth
    var adminApiKey = AdminApiKeyValidator.Resolve(builder.Configuration, app.Environment);

    var publicOrigins = builder.Configuration.GetSection("WebUi:PublicOrigins").Get<string[]>() ?? [];
    var enableLandingPage = builder.Configuration.GetValue<bool>("WebUi:EnableLandingPage", true);

    // Path base from ASPNETCORE_PATHBASE or PathBase config
    app.UsePortwayPathBase();

    // Middleware configuration
    app.UseResponseCompression();
    app.UseExceptionHandlingMiddleware();
    app.UseSecurityHeaders();
    app.UseContentNegotiation();

    // Forwarded headers for reverse proxies plus Cloudflare client IP and scheme restoration
    app.UsePortwayForwardedHeaders();

    // Configure unified documentation
    var openApiMonitor = app.Services.GetRequiredService<IOptionsMonitor<OpenApiSettings>>();
    OpenApiConfiguration.ConfigureDocs(app, openApiMonitor);

    // Configure Web UI authentication and static file serving..
    if (!string.IsNullOrEmpty(adminApiKey))
        app.UseWebUiAuth(adminApiKey);

    // Record request metrics for all non-health paths (UI and API tracked separately)
    app.UsePortwayRequestMetrics();

    app.UseDefaultFilesWithOptions();

    // Inject PathBase into index.html before static files can serve it
    app.UseIndexHtmlPathBaseInjection();

    app.UseStaticFiles();

    // Root path, legacy /swagger and docs redirects
    app.UsePortwayRootRedirects(adminApiKey, publicOrigins, enableLandingPage);

    app.UsePortwayCors();

    PortwayApi.Middleware.RateLimiterExtensions.UseRateLimiter(app, adminApiKey);

    app.UseTokenAuthentication();
    app.UseAuthorization();

    // Removed UseResponseCaching() to prevent ASP.NET Core from intercepting requests before they reach CacheManager!
    
    app.UseAuthenticatedCaching();

    // Strong ETags on GET /api responses; If-None-Match revalidation returns 304
    app.UseETagCaching();
    app.UseRequestTrafficLogging();
    app.UseRouting();

    // Initialise mcp.db unconditionally so the setup wizard works before Mcp:Enabled is true
    await app.InitializeMcpConfigDatabaseAsync();

    // Initialise auth.db and create a default token if none exist
    await app.InitializeAuthDatabaseAsync(serverName);

    // Log cache configuration
    app.LogCacheConfiguration();

    // Get environment settings services and log endpoint summary
    var environmentSettings = app.Services.GetRequiredService<EnvironmentSettings>();
    var sqlEnvironmentProvider = app.Services.GetRequiredService<IEnvironmentSettingsProvider>();

    var sqlEndpointList = EndpointHandler.GetSqlEndpoints();
    var webhookEndpoints = EndpointHandler.GetSqlWebhookEndpoints();
    var fileEndpoints = EndpointHandler.GetFileEndpoints();
    var staticEndpoints = EndpointHandler.GetStaticEndpoints();

    EndpointSummaryHelper.LogEndpointSummary(sqlEndpointList, proxyEndpointMap, webhookEndpoints, fileEndpoints, staticEndpoints);

    // Map controller routes; data plane is Bearer authenticated so automatic cross-origin CSRF marking must not poison form access for CORS clients
    app.MapControllers().DisableAntiforgery();

    // Register Composite middleware
    app.MapCompositeEndpoint();

    // Map health check endpoints
    PortwayApi.Endpoints.HealthCheckEndpointExtensions.MapHealthCheckEndpoints(app);

    // Map Prometheus scrape endpoint (conditionally enabled)
    app.MapPortwayPrometheusScraping();
    
    // Map MCP endpoints (conditionally enabled)
    var mcpEnabled = builder.Configuration.GetValue<bool>("Mcp:Enabled", false);
    if (mcpEnabled)
        app.MapMcpRegistry(proxyEndpointsDirectory);

    app.MapMcpEndpoints(builder.Configuration);
    if (mcpEnabled)
        app.MapMcpChatEndpoints();

    // Web UI Routes
    if (!string.IsNullOrEmpty(adminApiKey))
        app.MapWebUiEndpoints(adminApiKey);

    // Fallback for unmatched routes; HTML 404 for browsers, JSON for API clients
    app.MapPortwayFallback();

    // Pre-flight: verify configured ports are available before Kestrel tries to bind
    if (!StartupLogHelper.TryReservePorts(app, builder.Configuration))
        return;

    // Log hosting URLs, Web UI auth status and configuration reload status
    StartupLogHelper.LogHostingSummary(app, builder.Configuration, adminApiKey);

    // Register application shutdown handler
    app.Lifetime.ApplicationStopping.Register(() =>
    {
        Log.Information("");
        Log.Information("Application shutting down...");
        Log.CloseAndFlush();
    });

    // Run the application
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal("");
    Log.Fatal(ex, "Application failed to start.");
}
finally
{
    Log.CloseAndFlush();
}


public partial class Program { }
