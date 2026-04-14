using System.IO.Compression;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using PortwayApi.Api;
using PortwayApi.Auth;
using PortwayApi.Classes;
using PortwayApi.Endpoints;
using PortwayApi.Interfaces;
using PortwayApi.Services.Files;
using PortwayApi.Helpers;
using PortwayApi.Middleware;
using PortwayApi.Services.Caching;
using PortwayApi.Services.Configuration;
using PortwayApi.Services.Health;
using PortwayApi.Services.Mcp;
using PortwayApi.Services.Providers;
using PortwayApi.Services.Telemetry;
using Serilog;
using System.Text.Json;
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

    LogApplicationAscii();

    // In Docker, HTTPS is opt-in (Use_HTTPS=true). On Windows Server/IIS, HTTPS is enabled by default unless Use_HTTPS=false.
    var useHttpsEnv = Environment.GetEnvironmentVariable("Use_HTTPS");
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

        // 7. Configure HTTPS only if Use_HTTPS is set to 'true' (opt-in)
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

    // Configure CORS — never use AllowAnyOrigin() in production; require explicit allowlist.
    // Operators configure allowed origins via WebUi:CorsOrigins in appsettings.json or env vars.
    var corsOrigins = builder.Configuration.GetSection("WebUi:CorsOrigins").Get<string[]>() ?? [];
    if (!builder.Environment.IsDevelopment())
    {
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowConfiguredOrigins", policy =>
            {
                if (corsOrigins.Length > 0)
                {
                    policy.WithOrigins(corsOrigins)
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .AllowCredentials();
                }
                else
                {
                    // No origins configured — block all cross-origin requests
                    policy.SetIsOriginAllowed(_ => false);
                }
            });
        });
    }

    var assemblyVersion = typeof(Program).Assembly
        .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion?.Split('+')[0] ?? "0.0.0";

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

    // Register route constraint for ProxyConstraint
    builder.Services.Configure<RouteOptions>(options =>
    {
        options.ConstraintMap.Add("proxy", typeof(ProxyConstraintAttribute));
    });
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
    builder.Services.AddPortwayHealthServices();

    OpenApiConfiguration.ConfigureOpenApi(builder);

    // Build the application
    var app = builder.Build();

    var adminApiKey = builder.Configuration.GetValue<string>("WebUi:AdminApiKey", "") ?? "";

    // Reject the shipped placeholder key in production — disable Web UI auth and warn loudly.
    // In Development the placeholder is intentionally allowed for local testing convenience.
    const string PlaceholderKey = "INSECURE-CHANGE-ME-admin-api-key";
    if (adminApiKey == PlaceholderKey && !app.Environment.IsDevelopment())
    {
        Log.Error("WebUi:AdminApiKey is set to the default placeholder value. " +
                  "Web UI authentication has been DISABLED. Set a strong, unique key (≥32 chars) to enable it.");
        adminApiKey = "";
    }
    else if (!string.IsNullOrEmpty(adminApiKey) && adminApiKey.Length < 32 && !app.Environment.IsDevelopment())
    {
        Log.Warning("WebUi:AdminApiKey is shorter than 32 characters. Consider using a longer, randomly generated key.");
    }

    var publicOrigins = builder.Configuration.GetSection("WebUi:PublicOrigins").Get<string[]>() ?? [];
    var enableLandingPage = builder.Configuration.GetValue<bool>("WebUi:EnableLandingPage", true);
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

    // Middleware configuration
    app.UseResponseCompression();
    app.UseExceptionHandlingMiddleware();
    app.UseSecurityHeaders();
    app.UseContentNegotiation();

    // Configure forwarded headers with security best practices to prevent
    var forwardedHeadersOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                        ForwardedHeaders.XForwardedProto |
                        ForwardedHeaders.XForwardedHost,
        
        // Enforce header symmetry to prevent desync between front-end and back-end
        RequireHeaderSymmetry = true,
        
        // Support deep proxy chains
        ForwardLimit = null
    };

    // Configure known proxies based on environment
    var isProduction = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        ?.Equals("Production", StringComparison.OrdinalIgnoreCase) ?? false;

    if (!isProduction)
    {
        forwardedHeadersOptions.KnownIPNetworks.Clear();
        forwardedHeadersOptions.KnownProxies.Clear();
        Log.Warning("ForwardedHeaders: No known proxies configured. Add trusted proxies in production.");
    }
    else
    {
        // Production: cleared by default; configure via config file if needed
        forwardedHeadersOptions.KnownIPNetworks.Clear();
        forwardedHeadersOptions.KnownProxies.Clear();
    }

    app.UseForwardedHeaders(forwardedHeadersOptions);

    // Trust Cloudflare headers only if CF-Ray is present (added by Cloudflare edge, not clients)
    app.Use((context, next) =>
    {
        if (context.Request.Headers.TryGetValue("CF-Ray", out _))
        {
            if (context.Request.Headers.TryGetValue("CF-Visitor", out var cfVisitor) &&
                cfVisitor.ToString().Contains("\"scheme\":\"https\""))
            {
                context.Request.Scheme = "https";
            }

            if (context.Request.Headers.TryGetValue("CF-Connecting-IP", out var cfIp) &&
                IPAddress.TryParse(cfIp.ToString(), out var cfIpAddress))
            {
                context.Connection.RemoteIpAddress = cfIpAddress;
            }
        }

        return next();
    });

    // Configure unified documentation
    var openApiMonitor = app.Services.GetRequiredService<IOptionsMonitor<OpenApiSettings>>();
    OpenApiConfiguration.ConfigureDocs(app, openApiMonitor);

    // Configure Web UI authentication and static file serving..
    if (!string.IsNullOrEmpty(adminApiKey))
        app.UseWebUiAuth(adminApiKey);

    // Record request metrics for all non-health paths (UI and API tracked separately)
    var metricsService  = app.Services.GetRequiredService<PortwayApi.Services.MetricsService>();
    var portwayMetrics  = app.Services.GetRequiredService<PortwayMetrics>();
    app.Use(async (context, next) =>
    {
        var startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        await next();
        var duration = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp);

        var path = context.Request.Path;
        if (path.StartsWithSegments("/health") || path.StartsWithSegments("/scalar")) return;
        string source, endpoint;
        if (path.StartsWithSegments("/ui"))
        {
            source = "ui"; endpoint = "";
        }
        else
        {
            source   = "api";
            endpoint = ParseEndpointName(path.Value);
        }
        metricsService.Record(context.Response.StatusCode, context.Request.Method, source, endpoint);
        portwayMetrics.RequestCompleted(context.Request.Method, context.Response.StatusCode, source, duration);
    });

    app.UseDefaultFilesWithOptions();

    // Inject PathBase into index.html before static files can serve it
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.Value?.Equals("/index.html", StringComparison.OrdinalIgnoreCase) == true)
        {
            var webRoot = app.Environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var filePath = Path.Combine(webRoot, "index.html");
            if (File.Exists(filePath))
            {
                var pb = context.Request.PathBase.Value ?? "";
                var html = await File.ReadAllTextAsync(filePath);
                html = html.Replace("<head>", $"<head>\n  <base href=\"{pb}/\">\n  <script>window.PortwayBase=\"{pb}\";</script>");
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(html);
                return;
            }
        }
        await next();
    });

    app.UseStaticFiles();

    // Root path handling middleware
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        var pathBase = context.Request.PathBase.Value ?? "";
        
        Log.Debug("Incoming request: PathBase={PathBase}, Path={Path}", pathBase, path);

        // Handle root path redirect
        if (path == "/" || path == "")
        {
            var acceptHeader = context.Request.Headers.Accept.ToString();
            var isHtmlRequest = acceptHeader.Contains("text/html") || string.IsNullOrEmpty(acceptHeader);

            if (!isHtmlRequest)
            {
                // Non-browser requests always get the OpenAPI JSON
                var redirectPath = $"{pathBase}/docs/openapi/v1/openapi.json";
                Log.Debug("API root request, redirecting to {Path}", redirectPath);
                context.Response.Redirect(redirectPath, permanent: false);
                return;
            }

            // Browser request: show the landing page selector for local clients or external clients
            // whose origin matches a configured PublicOrigins pattern. Others go straight to docs.
            var remoteIp = context.Connection.RemoteIpAddress;
            var urlValidator = context.RequestServices.GetRequiredService<UrlValidator>();
            var isLocalClient = remoteIp != null && urlValidator.IsClientIpAllowed(
                remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp);
            var isPublicOrigin = publicOrigins.Length > 0 &&
                WebUiEndpointExtensions.IsPublicOriginAllowed(context.Request, publicOrigins);

            if (enableLandingPage && (isLocalClient || isPublicOrigin) && !string.IsNullOrEmpty(adminApiKey))
            {
                var webRootPath = app.Environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var indexPath = Path.Combine(webRootPath, "index.html");
                if (File.Exists(indexPath))
                {
                    Log.Debug("Local client at root, serving landing page");
                    context.Response.Redirect($"{pathBase}/index.html", permanent: false);
                    return;
                }
            }

            // External client or no landing page; redirect to docs
            {
                var redirectPath = $"{pathBase}/docs";
                Log.Debug("Redirecting root to {Path}", redirectPath);
                context.Response.Redirect(redirectPath, permanent: false);
                return;
            }
        }
        
        // Handle now removed /swagger redirect (backward compatibility)
        if (path == "/swagger" && !context.Request.Path.Value!.Contains("/swagger.json"))
        {
            var redirectPath = $"{pathBase}/docs";
            Log.Debug("Legacy /swagger redirect to {Path}", redirectPath);
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

    // Logging middleware
    var enableRequestLogging = builder.Configuration.GetValue<bool>("LogSettings:LogResponseToFile") || builder.Environment.IsDevelopment();

    if (!builder.Environment.IsDevelopment())
    {
        app.UseCors("AllowConfiguredOrigins");
    }
    else
    {
        // Development only: allow all origins for local testing convenience
        app.UseCors(corsBuilder =>
        {
            corsBuilder.AllowAnyOrigin()
                       .AllowAnyMethod()
                       .AllowAnyHeader();
        });
    }

    PortwayApi.Middleware.RateLimiterExtensions.UseRateLimiter(app);
    app.UseTokenAuthentication();
    app.UseAuthorization();

    // UseResponseCaching() removed as we'll be using CacheManager to handles all server-side caching.
    // Leaving it in causes ASP.NET Core to intercept repeat requests before reaching
    // the controller, so CacheManager.GetAsync is never called on hits.
    
    app.UseAuthenticatedCaching();
    app.UseRequestTrafficLogging();
    app.UseRouting();

    // Initialise MCP config DB (mcp.db), runs unconditionally so the setup wizard works even before Mcp:Enabled is set to true.
    using (var mcpScope = app.Services.CreateScope())
    {
        try
        {
            var mcpDbFactory = mcpScope.ServiceProvider
                .GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<PortwayApi.Services.Mcp.McpConfigDbContext>>();
            await using var mcpDb = await mcpDbFactory.CreateDbContextAsync();
            mcpDb.Database.EnsureCreated();
            mcpDb.EnsureTablesCreated();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "MCP config database initialisation failed");
        }
    }

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
                Log.Debug("Total active tokens: {Count}", activeTokens.Count());
                Log.Warning("Tokens detected in the tokens directory. Relocate them to a secure location to eliminate this security risk.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Database initialization failed: {Message}", ex.Message);
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
    
    // Map MCP endpoints (conditionally enabled)
    var mcpEnabled = builder.Configuration.GetValue<bool>("Mcp:Enabled", false);
    if (mcpEnabled)
    {
        var mcpRegistry     = app.Services.GetRequiredService<McpEndpointRegistry>();
        var mcpAppsProvider = app.Services.GetRequiredService<McpAppsResourceProvider>();
        
        // Collect all MCP-exposed endpoints
        var mcpEndpoints = new List<EndpointMcpInfo>();
        
        // Helper to add MCP-exposed endpoints from EndpointDefinition dictionaries
        void AddMcpEndpoints(Dictionary<string, EndpointDefinition> endpoints)
        {
            foreach (var kvp in endpoints)
            {
                if (kvp.Value.IsMcpExposed)
                {
                    var endpointKind = kvp.Value.Type switch {
                        EndpointType.Static => "static",
                        EndpointType.Files  => "file",
                        _                   => "api"
                    };
                    mcpEndpoints.Add(new EndpointMcpInfo
                    {
                        Name = kvp.Value.EndpointName,
                        Namespace = kvp.Value.EffectiveNamespace,
                        Url = kvp.Value.Url,
                        // File endpoints: POST (upload) and DELETE are not useful in chat — GET only
                        Methods = kvp.Value.Type == EndpointType.Files
                            ? kvp.Value.Methods.Where(m => m.Equals("GET", StringComparison.OrdinalIgnoreCase)).ToList()
                            : kvp.Value.Methods,
                        AllowedEnvironments = kvp.Value.AllowedEnvironments,
                        MethodDescriptions = kvp.Value.Documentation?.MethodDescriptions,
                        Description = kvp.Value.Documentation?.TagDescription ?? kvp.Value.Documentation?.Description,
                        AvailableFields = kvp.Value.AllowedColumns is { Count: > 0 }
                            // Strip Portway alias syntax (e.g. "ItemCode;ProductNumber" → "ProductNumber")
                            ? kvp.Value.AllowedColumns
                                .Select(c => c.Contains(';') ? c[(c.IndexOf(';') + 1)..] : c)
                                .ToList()
                            : null,
                        ContentType = kvp.Value.Properties?.TryGetValue("ContentType", out var ct) == true
                            ? ct?.ToString() : null,
                        EndpointKind = endpointKind,
                        Instruction = kvp.Value.Mcp?.Instruction
                    });
                }
            }
        }
        
        // Helper for GetEndpoints which returns tuples
        void AddMcpEndpointsFromTuple(Dictionary<string, (string Url, HashSet<string> Methods, bool IsPrivate, bool IsMcpExposed, string Type, List<string>? AllowedEnvironments)> endpoints)
        {
            foreach (var kvp in endpoints)
            {
                if (kvp.Value.IsMcpExposed)
                {
                    mcpEndpoints.Add(new EndpointMcpInfo
                    {
                        Name = kvp.Key,
                        Url = kvp.Value.Url,
                        Methods = kvp.Value.Methods.ToList(),
                        AllowedEnvironments = kvp.Value.AllowedEnvironments
                    });
                }
            }
        }
        
        // Local function: builds the full MCP endpoint list and registers it.
        // Stored as a delegate so EndpointFileWatcher can re-run it after hot-reload.
        void RefreshMcpRegistry()
        {
            mcpEndpoints.Clear();
            AddMcpEndpoints(EndpointHandler.GetSqlEndpoints());
            AddMcpEndpointsFromTuple(EndpointHandler.GetEndpoints(proxyEndpointsDirectory));
            AddMcpEndpoints(EndpointHandler.GetSqlWebhookEndpoints());
            AddMcpEndpoints(EndpointHandler.GetFileEndpoints());
            AddMcpEndpoints(EndpointHandler.GetStaticEndpoints());
            mcpRegistry.RegisterEndpoints(mcpEndpoints);
            var html = McpAppsResourceProvider.GenerateEndpointExplorerHtml(mcpEndpoints);
            mcpAppsProvider.RegisterUiResource("ui://portway/endpoint-explorer", html);
            Log.Information("MCP registry refreshed: {Count} tools registered", mcpEndpoints.Count);
        }

        AddMcpEndpoints(sqlEndpointList);
        AddMcpEndpointsFromTuple(proxyEndpointMap);
        AddMcpEndpoints(webhookEndpoints);
        AddMcpEndpoints(fileEndpoints);
        AddMcpEndpoints(staticEndpoints);

        mcpRegistry.RegisterEndpoints(mcpEndpoints);

        // Wire up hot-reload: when endpoint files change, re-build the registry from disk
        mcpRegistry.RefreshAction = RefreshMcpRegistry;

        // Register MCP Apps UI resources
        var explorerHtml = McpAppsResourceProvider.GenerateEndpointExplorerHtml(mcpEndpoints);
        mcpAppsProvider.RegisterUiResource("ui://portway/endpoint-explorer", explorerHtml);

        // Initialize the MCP protocol tools
        PortwayMcpTools.Initialize(mcpRegistry, mcpAppsProvider);

        // Add MCP Apps UI resource endpoint
        app.MapGet("/mcp/apps/{*path}", async (HttpContext context, CancellationToken ct) =>
        {
            var path = context.Request.RouteValues["path"]?.ToString() ?? "";
            var resourceUri = $"ui://portway/{path.TrimStart('/')}";

            if (mcpAppsProvider.GetUiResource(resourceUri) is { } html)
            {
                context.Response.ContentType = "text/html; profile=mcp-app";
                await context.Response.WriteAsync(html, ct);
                return;
            }

            context.Response.StatusCode = 404;
        }).ExcludeFromDescription();

        Log.Information("MCP Apps support enabled, resources registered: {Count}", mcpAppsProvider.GetAllResources().Count);
    }
    
    app.MapMcpEndpoints(builder.Configuration);
    if (builder.Configuration.GetValue<bool>("Mcp:Enabled", false))
        app.MapMcpChatEndpoints();

    // Web UI Routes
    if (!string.IsNullOrEmpty(adminApiKey))
        app.MapWebUiEndpoints(adminApiKey);

    // Fallback for unmatched routes (helpful for debugging)
    // Return HTML 404 for browser requests, JSON for API clients.
    app.MapFallback(async context =>
    {
        var path = context.Request.Path.Value;
        Log.Warning("Unmatched route: {Method} {Path}", context.Request.Method, path);

        context.Response.StatusCode = 404;

        var acceptHeader = context.Request.Headers.Accept.ToString();
        var isBrowserRequest = acceptHeader.Contains("text/html");

        if (isBrowserRequest)
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync("""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                  <meta charset="utf-8">
                  <meta name="viewport" content="width=device-width, initial-scale=1">
                  <title>404 Not Found · Portway</title>
                  <link href="/css/site.css" rel="stylesheet">
                </head>
                <body style="display:flex;align-items:center;justify-content:center;height:100vh">
                  <div style="text-align:center;max-width:420px;padding:2rem">
                    <div style="font-size:3rem;font-weight:700;letter-spacing:-0.04em;color:hsl(var(--muted-foreground))">404</div>
                    <div style="font-size:1rem;font-weight:600;margin:0.75rem 0 0.5rem">Page not found</div>
                    <div style="font-size:0.875rem;color:hsl(var(--muted-foreground));margin-bottom:1.5rem">
                      The page you're looking for doesn't exist or has been moved.
                    </div>
                    <a href="/ui/dashboard" style="display:inline-flex;align-items:center;gap:0.375rem;padding:0.5rem 1rem;background:hsl(var(--primary));color:hsl(var(--primary-foreground));border-radius:var(--radius);text-decoration:none;font-size:0.875rem;font-weight:500">
                      Go to Dashboard
                    </a>
                  </div>
                </body>
                </html>
                """);
        }
        else
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Route not found",
                path = path,
                timestamp = DateTime.UtcNow
            });
        }
    });

    // Pre-flight: verify configured ports are available before Kestrel tries to bind.
    // Resolves the same URL list that the else-branch below uses as a fallback.
    var urlsToCheck = app.Urls.Count > 0
        ? app.Urls
        : (Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
            ?? builder.Configuration["Kestrel:Endpoints:Http:Url"]
            ?? builder.Configuration["urls"]
            ?? "http://localhost:5000").Split(';');

    foreach (var rawUrl in urlsToCheck)
    {
        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri)) continue;
        if (uri.Scheme is not ("http" or "https")) continue;

        try
        {
            var address = uri.Host is "localhost" or "0.0.0.0" or "*" or "+"
                ? System.Net.IPAddress.Loopback
                : System.Net.IPAddress.Parse(uri.Host);

            using var probe = new System.Net.Sockets.TcpListener(address, uri.Port);
            probe.Start();
            probe.Stop();
        }
        catch (System.Net.Sockets.SocketException)
        {
            Log.Fatal("Port {Port} is already in use. Stop the existing process and try again.", uri.Port);
            return;
        }
    }

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

        var formattedUrls = serverUrls.Replace(";", "; ");
        Log.Information("Application is hosted on: {Urls}", formattedUrls);
    }

    var webUiAuthStatus = string.IsNullOrEmpty(adminApiKey) ? "Disabled" : "Enabled";
    Log.Information("Web UI: {Status}", webUiAuthStatus);

    var endpointReloadEnabled = builder.Configuration.GetValue<bool>("EndpointReloading:Enabled", true);
    if (endpointReloadEnabled)
        Log.Information("Configuration reload enabled: appsettings.json, /endpoints, /environments");

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


/// Parses "/api/{env}/{name}" or "/webhook/{env}/{name}" → "{name}" (or "{env}/composite/{name}" → "composite/{name}")
static string ParseEndpointName(string? path)
{
    if (string.IsNullOrEmpty(path)) return "";
    var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
    if (segs.Length < 3) return "";
    var prefix = segs[0].ToLowerInvariant();
    if (prefix != "api" && prefix != "webhook") return "";
    // segs[1] = env, segs[2] = name (or "composite"), segs[3] = composite name
    if (segs.Length >= 4 && segs[2].Equals("composite", StringComparison.OrdinalIgnoreCase))
        return $"composite/{segs[3]}";
    return segs[2];
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

public partial class Program { }
