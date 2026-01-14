using System.IO.Compression;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Instrumentation.SqlClient;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SqlKata.Compilers;
using PortwayApi.Api;
using PortwayApi.Auth;
using PortwayApi.Classes;
using PortwayApi.Endpoints;
using PortwayApi.Helpers;
using PortwayApi.Interfaces;
using PortwayApi.Middleware;
using PortwayApi.Services;
using PortwayApi.Services.Caching;
using PortwayApi.Services.Files;
using System.Text;
using System.Text.Json;
using System.Net;

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
            (logEvent.Properties["RequestPath"].ToString().Contains("/swagger") ||
                logEvent.Properties["RequestPath"].ToString().Contains("/index.html")))
        .CreateLogger();

    builder.Host.UseSerilog();

    LogApplicationAscii();

    // In Docker, HTTPS is opt-in (USE_HTTPS=true). On Windows Server/IIS, HTTPS is enabled by default unless USE_HTTPS=false.
    var useHttpsEnv = Environment.GetEnvironmentVariable("USE_HTTPS");
    var runningInContainer = string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);
    bool useHttps;
    if (runningInContainer)
    {
        // In Docker, only enable HTTPS if explicitly requested
        useHttps = string.Equals(useHttpsEnv, "true", StringComparison.OrdinalIgnoreCase);
    }
    else
    {
        // On Windows Server/IIS, enable HTTPS unless explicitly disabled
        useHttps = !string.Equals(useHttpsEnv, "false", StringComparison.OrdinalIgnoreCase);
    }

    // Configure Kestrel 
    builder.WebHost.ConfigureKestrel(serverOptions =>
    {
        // 1. Disable server header (security)
        serverOptions.AddServerHeader = false;

        // 2. Set appropriate request limits
        serverOptions.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10 MB request body limit
        serverOptions.Limits.MaxRequestHeadersTotalSize = 32 * 1024; // 32 KB for headers

        // 3. Configure timeouts for better client handling
        serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
        serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);

        // 4. Connection rate limiting to prevent DoS
        serverOptions.Limits.MaxConcurrentConnections = 1000;
        serverOptions.Limits.MaxConcurrentUpgradedConnections = 100;

        // 5. Data rate limiting to prevent slow requests
        serverOptions.Limits.MinRequestBodyDataRate = new MinDataRate(
            bytesPerSecond: 100, gracePeriod: TimeSpan.FromSeconds(10));
        serverOptions.Limits.MinResponseDataRate = new MinDataRate(
            bytesPerSecond: 100, gracePeriod: TimeSpan.FromSeconds(10));

        // 6. HTTP/2 specific settings
        serverOptions.Limits.Http2.MaxStreamsPerConnection = 100;
        serverOptions.Limits.Http2.MaxFrameSize = 16 * 1024; // 16 KB
        serverOptions.Limits.Http2.InitialConnectionWindowSize = 128 * 1024; // 128 KB
        serverOptions.Limits.Http2.KeepAlivePingDelay = TimeSpan.FromSeconds(30);
        serverOptions.Limits.Http2.KeepAlivePingTimeout = TimeSpan.FromSeconds(60);

        // 7. Configure HTTPS only if USE_HTTPS is set to 'true' (opt-in)
        if (!builder.Environment.IsDevelopment() && useHttps)
        {
            serverOptions.ConfigureEndpointDefaults(listenOptions =>
            {
                listenOptions.UseHttps();
            });
        }
    });

    // Add response compression
    builder.Services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;
        options.Providers.Add<BrotliCompressionProvider>();
        options.Providers.Add<GzipCompressionProvider>();
    });

    builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
    {
        options.Level = CompressionLevel.Fastest;
    });
    builder.Services.Configure<GzipCompressionProviderOptions>(options =>
    {
        options.Level = CompressionLevel.Fastest;
    });

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

    // Configure CORS
    if (!builder.Environment.IsDevelopment())
    {
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAllOrigins",
                builder =>
                {
                    builder.AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowAnyHeader();
                });
        });
    }

    builder.Services.AddOpenTelemetry()
        .WithTracing(builder =>
        {
            builder
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation()
                .AddSource("PortwayAPI")
                .AddOtlpExporter();
        })
        .WithMetrics(builder =>
        {
            builder
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter("PortwayAPI")
                .AddOtlpExporter();
        });

    // Define server name
    string serverName = Environment.MachineName;

    // Configure logging
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options => options.FormatterName = "simple");
    builder.Logging.AddSimpleConsole(options => options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ");

    // Configure SQLite Authentication Database
    var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "auth.db");
    builder.Services.AddDbContext<AuthDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
    builder.Services.AddScoped<TokenService>();
    builder.Services.AddAuthorization();
    builder.Services.AddHostedService<LogFlusher>();

    // Register configuration reload services for dynamic config updates
    builder.Services.AddHostedService<PortwayApi.Services.Configuration.ConfigurationReloadService>();
    builder.Services.AddHostedService<PortwayApi.Services.Configuration.EnvironmentFileWatcher>();
    builder.Services.AddHostedService<PortwayApi.Services.Configuration.EndpointFileWatcher>();

    // Register EndpointReloading options for feature flag support
    builder.Services.Configure<PortwayApi.Classes.Configuration.EndpointReloadingOptions>(
        builder.Configuration.GetSection("EndpointReloading"));

    // Register route constraint for ProxyConstraint
    builder.Services.Configure<RouteOptions>(options =>
    {
        options.ConstraintMap.Add("proxy", typeof(ProxyConstraintAttribute));
    });
    builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection("FileStorage"));

    // Configure HTTP client
    var proxyUsername = Environment.GetEnvironmentVariable("PROXY_USERNAME");
    var proxyPassword = Environment.GetEnvironmentVariable("PROXY_PASSWORD");
    var proxyDomain = Environment.GetEnvironmentVariable("PROXY_DOMAIN");

    if (!string.IsNullOrEmpty(proxyUsername) && !string.IsNullOrEmpty(proxyPassword))
    {
        builder.Services.AddHttpClient("ProxyClient")
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                return new HttpClientHandler
                {
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(proxyUsername, proxyPassword, proxyDomain),
                    PreAuthenticate = true
                };
            });
    }
    else
    {
        // Fallback to default credentials if not specified
        builder.Services.AddHttpClient("ProxyClient")
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                return new HttpClientHandler
                {
                    UseDefaultCredentials = true,
                    PreAuthenticate = true
                };
            });
    }

    // Register environment settings providers
    builder.Services.AddSingleton<IEnvironmentSettingsProvider, EnvironmentSettingsProvider>();
    builder.Services.AddSingleton<EnvironmentSettings>();

    // Register OData SQL services
    builder.Services.AddSingleton<IHostedService, StartupLogger>();
    builder.Services.AddSingleton<IEdmModelBuilder, EdmModelBuilder>();
    builder.Services.AddSingleton<Compiler, SqlServerCompiler>();
    builder.Services.AddSingleton<IODataToSqlConverter, ODataToSqlConverter>();

    // Register Serilog logger for dependency injection 
    builder.Services.AddSingleton<Serilog.ILogger>(sp => Log.Logger);

    builder.Services.AddSingleton<SqlMetadataService>();
    builder.Services.AddSingleton<FileHandlerService>();
    builder.Services.AddSqlConnectionPooling(builder.Configuration);

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

    // Configure Rate Limiting
    builder.Services.AddRateLimiting(builder.Configuration);

    // Configure SSRF protection
    var urlValidatorPath = Path.Combine(Directory.GetCurrentDirectory(), "environments", "network-access-policy.json");
    if (!File.Exists(urlValidatorPath))
    {
        var directory = Path.GetDirectoryName(urlValidatorPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(urlValidatorPath, JsonSerializer.Serialize(new
        {
            allowedHosts = new[] { "localhost", "127.0.0.1" },
            blockedIpRanges = new[]
            {
                "10.0.0.0/8",
                "172.16.0.0/12",
                "192.168.0.0/16",
                "169.254.0.0/16"
            }
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    var urlValidator = new UrlValidator(urlValidatorPath);
    builder.Services.AddSingleton(urlValidator);

    // Configure Health Checks
    builder.Services.AddHealthChecks();

    // Register HealthCheckService wrapper with all dependencies (uses lazy endpoint lookup)
    builder.Services.AddSingleton<PortwayApi.Services.HealthCheckService>(sp =>
    {
        var healthCheckService = sp.GetRequiredService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>();
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

        return new PortwayApi.Services.HealthCheckService(
            healthCheckService,
            TimeSpan.FromSeconds(30),
            httpClientFactory);
    });

    // Configure Swagger using our centralized configuration (now returns void and registers with IOptionsMonitor)
    SwaggerConfiguration.ConfigureSwagger(builder);

    // Build the application
    var app = builder.Build();

    // Configure PathBase from environment variable or IIS
    var pathBase = Environment.GetEnvironmentVariable("ASPNETCORE_PATHBASE") 
        ?? builder.Configuration["PathBase"];

    if (!string.IsNullOrEmpty(pathBase))
    {
        // Ensure path starts with /
        if (!pathBase.StartsWith("/"))
        {   
            pathBase = "/" + pathBase;
        }
        
        app.UsePathBase(pathBase);
        Log.Information("Application configured with path base: {PathBase}", pathBase);
        
        // Add middleware to strip the path base from request path for internal routing
        app.Use((context, next) =>
        {
            if (context.Request.PathBase.HasValue)
            {
                Log.Debug("Request PathBase: {PathBase}, Path: {Path}", 
                    context.Request.PathBase, context.Request.Path);
            }
            return next();
        });
    }

    // ================================
    // MIDDLEWARE PIPELINE CONFIGURATION
    // ================================

    // 1. Response compression (early in pipeline)
    app.UseResponseCompression();

    // 2. Exception handling (must be first for error handling)
    app.UseExceptionHandlingMiddleware();

    // 3. Security headers (early security)
    app.UseSecurityHeaders();

    // 4. Content negotiation (validates Content-Type, ensures response headers)
    app.UseContentNegotiation();

    // 5. HTTPS redirection and forwarded headers (before static files)
    var forwardedHeadersOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                        ForwardedHeaders.XForwardedProto |
                        ForwardedHeaders.XForwardedHost,
        RequireHeaderSymmetry = false,
        ForwardLimit = null
    };
    forwardedHeadersOptions.KnownNetworks.Clear();
    forwardedHeadersOptions.KnownProxies.Clear();
    app.UseForwardedHeaders(forwardedHeadersOptions);


    // 5. Cloudflare-specific middleware
    app.Use((context, next) =>
    {
        // Check for Cloudflare headers
        if (context.Request.Headers.TryGetValue("CF-Visitor", out var cfVisitor))
        {
            if (cfVisitor.ToString().Contains("\"scheme\":\"https\""))
            {
                context.Request.Scheme = "https";
            }
        }

        // Also check for Cloudflare connecting protocol
        if (context.Request.Headers.TryGetValue("CF-Connecting-IP", out var _))
        {
            // We're behind Cloudflare, so trust the X-Forwarded-Proto header
            if (context.Request.Headers.TryGetValue("X-Forwarded-Proto", out var proto) &&
                proto == "https")
            {
                context.Request.Scheme = "https";
            }
        }

        return next();
    });

    // 6. Configure unified documentation at /docs (uses IOptionsMonitor for dynamic reload)
    var swaggerMonitor = app.Services.GetRequiredService<IOptionsMonitor<SwaggerSettings>>();
    SwaggerConfiguration.ConfigureDocs(app, swaggerMonitor);

    // 7. Static Files Configuration - Use Extension Method (DRY principle)
    app.UseDefaultFilesWithOptions();
    app.UseStaticFiles();

    // 8. Custom root path handling middleware
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        var pathBase = context.Request.PathBase.Value ?? "";
        
        Log.Debug("Incoming request: PathBase={PathBase}, Path={Path}", pathBase, path);

        // Handle root path redirect
        if (path == "/" || path == "")
        {
            // Check if index.html exists in wwwroot
            var webRootPath = app.Environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var indexPath = Path.Combine(webRootPath, "index.html");

            if (File.Exists(indexPath))
            {
                // Check if the request accepts HTML (browser request)
                var acceptHeader = context.Request.Headers.Accept.ToString();

                if (acceptHeader.Contains("text/html") || string.IsNullOrEmpty(acceptHeader))
                {
                    // Redirect to index.html so static file middleware can serve it
                    var redirectPath = $"{pathBase}/index.html";
                    Log.Debug("Redirecting to index.html");
                    context.Response.Redirect(redirectPath, permanent: false);
                    return;
                }
                else
                {
                    // For API requests to root, redirect to openapi JSON
                    var redirectPath = $"{pathBase}/docs/openapi/v1/openapi.json";
                    Log.Debug("API root request, redirecting to {Path}", redirectPath);
                    context.Response.Redirect(redirectPath, permanent: false);
                    return;
                }
            }
            else
            {
                // No index.html exists, redirect directly to docs
                var redirectPath = $"{pathBase}/docs";
                Log.Information("No index.html found, redirecting to {Path}", redirectPath);
                context.Response.Redirect(redirectPath, permanent: false);
                return;
            }
        }
        
        // Handle swagger root redirect (legacy support)
        if (path == "/swagger" && !context.Request.Path.Value!.Contains("/swagger.json"))
        {
            var redirectPath = $"{pathBase}/docs";
            Log.Debug("Legacy Swagger UI redirect to {Path}", redirectPath);
            context.Response.Redirect(redirectPath, permanent: true);
            return;
        }
        
        // Handle documentation paths (logging only)
        if (context.Request.Path.StartsWithSegments("/docs"))
        {
            Log.Debug("Documentation accessed: PathBase={PathBase}, Path={Path}",
                pathBase, context.Request.Path.Value);
        }
        
        await next();
    });

    // 9. Request/response logging
    var enableRequestLogging = builder.Configuration.GetValue<bool>("LogSettings:LogResponseToFile") || builder.Environment.IsDevelopment();

    // 10. CORS (before authentication)
    if (!builder.Environment.IsDevelopment())
    {
        app.UseCors("AllowAllOrigins");
    }
    else
    {
        app.UseCors(builder =>
        {
            builder.AllowAnyOrigin()
                   .AllowAnyMethod()
                   .AllowAnyHeader();
        });
    }

    // 11. Rate limiting (before authentication to limit by IP)
    PortwayApi.Middleware.RateLimiterExtensions.UseRateLimiter(app);

    // 12. Authentication and authorization
    app.UseTokenAuthentication();
    app.UseAuthorization();

    // 13. Caching middleware (after auth)
    app.UseResponseCaching();
    app.UseAuthenticatedCaching();
    app.UseRequestTrafficLogging();

    // 14. Routing
    app.UseRouting();

    // Initialize Database & Token (if required)
    using (var scope = app.Services.CreateScope())
    {
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
                Log.Information("Total active tokens: {Count}", activeTokens.Count());
                Log.Warning("Tokens detected in the tokens directory. Relocate them to a secure location to eliminate this high security risk!");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Database initialization failed: {Message}", ex.Message);
        }
    }


    // Initialize SQL Metadata Service
    using (var scope = app.Services.CreateScope())
    {
        try
        {
            var metadataService = scope.ServiceProvider.GetRequiredService<SqlMetadataService>();
            var sqlEndpoints = EndpointHandler.GetSqlEndpoints();
            var sqlEnvironmentProviderScope = scope.ServiceProvider.GetRequiredService<IEnvironmentSettingsProvider>();
            
            // Initialize metadata cache for all SQL endpoints
            await metadataService.InitializeAsync(
                sqlEndpoints,
                async environment => 
                {
                    try
                    {
                        var (connectionString, _, _) = await sqlEnvironmentProviderScope.LoadEnvironmentOrThrowAsync(environment);
                        return connectionString ?? string.Empty;
                    }
                    catch
                    {
                        return string.Empty;
                    }
                });
            
            Log.Debug("SQL metadata service initialized with {Count} endpoints", 
                metadataService.GetCachedEndpoints().Count());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize SQL metadata service: {Message}", ex.Message);
        }
    }

    // Log cache configuration
    using (var scope = app.Services.CreateScope())
    {
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

    // Get environment settings services and log endpoint summary
    var environmentSettings = app.Services.GetRequiredService<EnvironmentSettings>();
    var sqlEnvironmentProvider = app.Services.GetRequiredService<IEnvironmentSettingsProvider>();

    var sqlEndpointList = EndpointHandler.GetSqlEndpoints();
    var webhookEndpoints = EndpointHandler.GetSqlWebhookEndpoints();
    var fileEndpoints = EndpointHandler.GetFileEndpoints();
    var staticEndpoints = EndpointHandler.GetStaticEndpoints();

    EndpointSummaryHelper.LogEndpointSummary(sqlEndpointList, proxyEndpointMap, webhookEndpoints, fileEndpoints, staticEndpoints);

    // Map controller routes
    app.MapControllers();

    // Register Composite middleware
    app.MapCompositeEndpoint();

    // Map health check endpoints
    PortwayApi.Endpoints.HealthCheckEndpointExtensions.MapHealthCheckEndpoints(app);

    // Fallback for unmatched routes (helpful for debugging)
    app.MapFallback(async context =>
    {
        var path = context.Request.Path.Value;
        Log.Warning("Unmatched route: {Method} {Path}", context.Request.Method, path);

        context.Response.StatusCode = 404;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(new
        {
            error = "Route not found",
            method = context.Request.Method,
            path = path,
            timestamp = DateTime.UtcNow
        });
    });

    // Log application URLs
    var urls = app.Urls;
    if (urls != null && urls.Any())
    {
        Log.Information("Application is hosted on the following URLs:");
        foreach (var url in urls)
        {
            Log.Information("   {Url}", url);
        }
    }
    else if (builder.Environment.IsProduction() && Environment.GetEnvironmentVariable("ASPNETCORE_IIS_PHYSICAL_PATH") != null)
    {
        // We're running in IIS
        Log.Debug("Application is hosted in IIS");
    }
    else
    {
        var serverUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
            ?? builder.Configuration["Kestrel:Endpoints:Http:Url"]
            ?? builder.Configuration["urls"]
            ?? "http://localhost:5000";

        // Add a space after each ; in serverUrls for better readability
        var formattedUrls = serverUrls.Replace(";", "; ");
        Log.Information("Application is hosted on: {Urls}", formattedUrls);
    }

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

void LogApplicationAscii()
{
    Log.Information("");
    Log.Information(" ██████╗  ██████╗ ██████╗ ████████╗██╗    ██╗ █████╗ ██╗   ██╗");
    Log.Information(" ██╔══██╗██╔═══██╗██╔══██╗╚══██╔══╝██║    ██║██╔══██╗╚██╗ ██╔╝");
    Log.Information(" ██████╔╝██║   ██║██████╔╝   ██║   ██║ █╗ ██║███████║ ╚████╔╝ ");
    Log.Information(" ██╔═══╝ ██║   ██║██╔══██╗   ██║   ██║███╗██║██╔══██║  ╚██╔╝  ");
    Log.Information(" ██║     ╚██████╔╝██║  ██║   ██║   ╚███╔███╔╝██║  ██║   ██║   ");
    Log.Information(" ╚═╝      ╚═════╝ ╚═╝  ╚═╝   ╚═╝    ╚══╝╚══╝ ╚═╝  ╚═╝   ╚═╝   ");
    Log.Information("");
}

// Extension method to configure rate limiting services
public static class RateLimitingExtensions
{
    public static IServiceCollection AddRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}