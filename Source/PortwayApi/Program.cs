using System.IO.Compression;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.Kestrel.Core;
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

    // Register Serilog logger for dependency injection (so Serilog.ILogger can be injected)
    builder.Services.AddSingleton<Serilog.ILogger>(sp => Log.Logger);

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

    // Register HealthCheckService wrapper with all dependencies
    builder.Services.AddSingleton<PortwayApi.Services.HealthCheckService>(sp =>
    {
        var healthCheckService = sp.GetRequiredService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>();
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var endpointMap = EndpointHandler.GetEndpoints(proxyEndpointsDirectory);

        return new PortwayApi.Services.HealthCheckService(
            healthCheckService,
            TimeSpan.FromSeconds(30),
            httpClientFactory,
            endpointMap);
    });

    // Configure Swagger using our centralized configuration
    var swaggerSettings = SwaggerConfiguration.ConfigureSwagger(builder);

    // Build the application
    var app = builder.Build();

    // ================================
    // MIDDLEWARE PIPELINE CONFIGURATION
    // ================================

    // 1. Response compression (early in pipeline)
    app.UseResponseCompression();

    // 2. Exception handling (must be first for error handling)
    app.UseExceptionHandlingMiddleware();

    // 3. Security headers (early security)
    app.UseSecurityHeaders();

    // 4. HTTPS redirection and forwarded headers (before static files)
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

    // 6. Configure unified documentation at /docs
    SwaggerConfiguration.ConfigureDocs(app, swaggerSettings);

    // 7. Static Files Configuration - Use Extension Method (DRY principle)
    app.UseDefaultFilesWithOptions();
    app.UseStaticFilesWithFallback(app.Environment);

    // 8. Development request/response logging
    var enableRequestLogging = builder.Configuration.GetValue<bool>("LogSettings:LogResponseToFile") || builder.Environment.IsDevelopment();
    if (enableRequestLogging)
    {
        app.Use(async (context, next) =>
        {
            // Exclude swagger and docs paths from logging
            var path = context.Request.Path.Value?.ToLowerInvariant();
            var shouldLog = !string.IsNullOrEmpty(path) &&
                           !path.Contains("/swagger") &&
                           !path.Contains("/docs");

            if (shouldLog)
            {
                Log.Information("📥 Incoming request: {Method} {Path}{QueryString}",
                    context.Request.Method,
                    context.Request.Path,
                    context.Request.QueryString);
            }

            var startTime = DateTime.UtcNow;
            await next();
            var duration = DateTime.UtcNow - startTime;

            if (shouldLog)
            {
                Log.Information("📤 Outgoing response: {StatusCode} for {Path} - Took {Duration}ms",
                    context.Response.StatusCode,
                    context.Request.Path,
                    Math.Round(duration.TotalMilliseconds));
            }
        });
    }

    // 9. CORS (before authentication)
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

    // 10. Rate limiting (before authentication to limit by IP)
    PortwayApi.Middleware.RateLimiterExtensions.UseRateLimiter(app);

    // 11. Authentication and authorization
    app.UseTokenAuthentication();
    app.UseAuthorization();

    // 12. Caching middleware (after auth)
    app.UseResponseCaching();
    app.UseAuthenticatedCaching();
    app.UseRequestTrafficLogging();

    // 13. Routing
    app.UseRouting();

    // Initialize Database & Create Default Token if needed
    using (var scope = app.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();

        try
        {
            // Set up database and migrate if needed
            context.Database.EnsureCreated();
            context.EnsureTablesCreated();

            // Create a default token if none exist
            var activeTokens = await tokenService.GetActiveTokensAsync();
            if (!activeTokens.Any())
            {
                var token = await tokenService.GenerateTokenAsync(serverName);
                Log.Information("📁 Token has been saved to tokens/{ServerName}.txt", serverName);
            }
            else
            {
                Log.Information("🔐 Total active tokens: {Count}", activeTokens.Count());
                Log.Warning("💥 Tokens detected in the tokens directory. Relocate them to a secure location to eliminate this high security risk!");
            }
        }
        catch (Exception ex)
        {
            Log.Error("❌ Database initialization failed: {Message}", ex.Message);
        }
    }

    // Log cache configuration
    using (var scope = app.Services.CreateScope())
    {
        try
        {
            var cacheManager = scope.ServiceProvider.GetRequiredService<CacheManager>();
            Log.Information("🧠 Cache configured with provider: {ProviderType}", cacheManager.ProviderType);
            Log.Information("🔄 Cache connection status: {Status}", cacheManager.IsConnected ? "Connected" : "Disconnected");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error initializing cache manager");
        }
    }

    // Get environment settings services and log endpoint summary
    var environmentSettings = app.Services.GetRequiredService<EnvironmentSettings>();
    var sqlEnvironmentProvider = app.Services.GetRequiredService<IEnvironmentSettingsProvider>();

    var sqlEndpoints = EndpointHandler.GetSqlEndpoints();
    var webhookEndpoints = EndpointHandler.GetSqlWebhookEndpoints();
    var fileEndpoints = EndpointHandler.GetFileEndpoints();
    var staticEndpoints = EndpointHandler.GetStaticEndpoints();

    EndpointSummaryHelper.LogEndpointSummary(sqlEndpoints, proxyEndpointMap, webhookEndpoints, fileEndpoints, staticEndpoints);

    // ================================
    // ENDPOINT MAPPING
    // ================================

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
        Log.Warning("🔍 Unmatched route: {Method} {Path}", context.Request.Method, path);

        context.Response.StatusCode = 404;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(new
        {
            error = "Route not found",
            method = context.Request.Method,
            path = path,
            suggestion = "Try visiting /docs for API documentation",
            timestamp = DateTime.UtcNow
        });
    });

    // Log application URLs
    var urls = app.Urls;
    if (urls != null && urls.Any())
    {
        Log.Information("🌐 Application is hosted on the following URLs:");
        foreach (var url in urls)
        {
            Log.Information("   {Url}", url);
        }
    }
    else if (builder.Environment.IsProduction() && Environment.GetEnvironmentVariable("ASPNETCORE_IIS_PHYSICAL_PATH") != null)
    {
        // We're running in IIS
        Log.Debug("🌐 Application is hosted in IIS");
    }
    else
    {
        var serverUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
            ?? builder.Configuration["Kestrel:Endpoints:Http:Url"]
            ?? builder.Configuration["urls"]
            ?? "http://localhost:5000";

        // Add a space after each ; in serverUrls for better readability
        var formattedUrls = serverUrls.Replace(";", "; ");
        Log.Information("🌐 Application is hosted on: {Urls}", formattedUrls);
    }

    // Register application shutdown handler
    app.Lifetime.ApplicationStopping.Register(() =>
    {
        Log.Information("Application shutting down...");
        Log.CloseAndFlush();
    });

    // Run the application
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application failed to start.");
}
finally
{
    Log.CloseAndFlush();
}

void LogApplicationAscii()
{
    var logo = new StringBuilder();
    logo.AppendLine(@"");
    logo.AppendLine(@"  _____           _                        ");
    logo.AppendLine(@" |  __ \         | |                       ");
    logo.AppendLine(@" | |__) |__  _ __| |___      ____ _ _   _  ");
    logo.AppendLine(@" |  ___/ _ \| '__| __\ \ /\ / / _` | | | | ");
    logo.AppendLine(@" | |  | (_) | |  | |_ \ V  V / (_| | |_| | ");
    logo.AppendLine(@" |_|   \___/|_|   \__| \_/\_/ \__,_|\__, | ");
    logo.AppendLine(@"                                      _/ | ");
    logo.AppendLine(@"                                     |___/ ");
    logo.AppendLine(@"");
    Log.Information(logo.ToString());
}

// Extension method to configure rate limiting services
public static class RateLimitingExtensions
{
    public static IServiceCollection AddRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}