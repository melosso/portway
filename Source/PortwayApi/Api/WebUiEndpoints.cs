namespace PortwayApi.Endpoints;

using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Services;

public static class WebUiEndpointExtensions
{
    private const string CookieName = "portway_auth";
    private const int TokenExpiryHours = 24;
    private static readonly DateTime ProcessStartTime = DateTime.UtcNow;

    /// <summary>
    /// Registers the Web UI auth + local-network-only middleware. To not make my same mistake twice: must be called before UseStaticFiles...
    /// </summary>
    public static WebApplication UseWebUiAuth(this WebApplication app, string adminApiKey)
    {
        var uiAuthEnabled = !string.IsNullOrEmpty(adminApiKey);

        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            if (!path.StartsWithSegments("/ui")) { await next(); return; }

            // Restrict the UI to the local machine and allowed network hosts only.
            var remoteIp = context.Connection.RemoteIpAddress;
            var validator = context.RequestServices.GetRequiredService<UrlValidator>();
            if (remoteIp == null || !validator.IsClientIpAllowed(remoteIp))
            {
                context.Response.StatusCode = 403;
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync(
                    "The administration UI is only accessible from the local network.");
                return;
            }

            // Cookie auth check, skip for the login page and the auth endpoints themselves.
            if (uiAuthEnabled &&
                !path.StartsWithSegments("/ui/login") &&
                !path.StartsWithSegments("/ui/api/auth"))
            {
                if (!context.Request.Cookies.TryGetValue(CookieName, out var cookie) ||
                    !ValidateToken(cookie, adminApiKey))
                {
                    context.Response.Redirect("/ui/login");
                    return;
                }
            }

            await next();
        });

        return app;
    }

    /// <summary>
    /// Maps all /ui/* page routes and /ui/api/* data endpoints.
    /// </summary>
    public static WebApplication MapWebUiEndpoints(this WebApplication app, string adminApiKey)
    {
        var uiAuthEnabled = !string.IsNullOrEmpty(adminApiKey);
        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot", "ui");
        var appVersion = typeof(WebUiEndpointExtensions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

        // ── Login ─────────────────────────────────────────────
        app.MapGet("/ui/login", () =>
            uiAuthEnabled
                ? Results.File(Path.Combine(wwwroot, "login.html"), "text/html")
                : Results.Redirect("/ui/dashboard"))
            .ExcludeFromDescription();
        app.MapGet("/ui/login.html", () => Results.Redirect("/ui/login"))
            .ExcludeFromDescription();

        // ── Auth endpoints ────────────────────────────────────
        app.MapPost("/ui/api/auth", async (HttpContext context) =>
        {
            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            var provided = body.TryGetProperty("apiKey", out var kp) ? kp.GetString() ?? "" : "";
            var provHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
            var expHash  = SHA256.HashData(Encoding.UTF8.GetBytes(adminApiKey));
            if (!CryptographicOperations.FixedTimeEquals(provHash, expHash))
                return Results.Json(new { error = "Invalid API key" }, statusCode: 401);

            var token = GenerateToken(adminApiKey);
            context.Response.Cookies.Append(CookieName, token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Path     = "/",
                Expires  = DateTimeOffset.UtcNow.AddHours(TokenExpiryHours)
            });
            return Results.Ok(new { ok = true });
        }).ExcludeFromDescription();

        app.MapPost("/ui/api/auth/logout", (HttpContext context) =>
        {
            context.Response.Cookies.Append(CookieName, "", new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Path     = "/",
                Expires  = DateTimeOffset.UnixEpoch
            });
            return Results.Ok();
        }).ExcludeFromDescription();

        // ── Page routes ───────────────────────────────────────
        app.MapGet("/ui", () => Results.Redirect("/ui/dashboard"))
            .ExcludeFromDescription();

        foreach (var page in new[] { "dashboard", "endpoints", "environments", "settings", "logs" })
        {
            var p        = page;
            var filePath = Path.Combine(wwwroot, $"{p}.html");
            app.MapGet($"/ui/{p}",      () => Results.File(filePath, "text/html")).ExcludeFromDescription();
            app.MapGet($"/ui/{p}.html", () => Results.Redirect($"/ui/{p}")).ExcludeFromDescription();
        }

        // ── Data endpoints ────────────────────────────────────
        app.MapGet("/ui/api/overview", () =>
        {
            var sqlEps        = EndpointHandler.GetSqlEndpoints();
            var proxyEps      = EndpointHandler.GetProxyEndpoints();
            var fileEps       = EndpointHandler.GetFileEndpoints();
            var staticEps     = EndpointHandler.GetStaticEndpoints();
            var webhookEps    = EndpointHandler.GetSqlWebhookEndpoints();
            var compositeCount = proxyEps.Count(e => e.Value.Type.ToString() == "Composite");
            var proxyCount     = proxyEps.Count(e => e.Value.Type.ToString() != "Composite");
            var envSettings   = app.Services.GetRequiredService<EnvironmentSettings>();
            var uptime        = (long)(DateTime.UtcNow - ProcessStartTime).TotalSeconds;

            return Results.Json(new
            {
                version = appVersion,
                uptime  = $"{uptime}s",
                endpoints = new
                {
                    sql       = sqlEps.Count,
                    proxy     = proxyCount,
                    composite = compositeCount,
                    file      = fileEps.Count,
                    @static   = staticEps.Count,
                    webhook   = webhookEps.Count,
                    total     = sqlEps.Count + proxyCount + compositeCount
                                + fileEps.Count + staticEps.Count + webhookEps.Count
                },
                environments = envSettings.AllowedEnvironments.Count
            });
        }).ExcludeFromDescription();

        app.MapGet("/ui/api/endpoints", () =>
        {
            var sqlEps     = EndpointHandler.GetSqlEndpoints();
            var proxyEps   = EndpointHandler.GetProxyEndpoints();
            var fileEps    = EndpointHandler.GetFileEndpoints();
            var staticEps  = EndpointHandler.GetStaticEndpoints();
            var webhookEps = EndpointHandler.GetSqlWebhookEndpoints();

            return Results.Json(new
            {
                sql = sqlEps.Select(e => new
                {
                    name        = e.Key,
                    methods     = e.Value.Methods,
                    is_private  = e.Value.IsPrivate,
                    schema      = e.Value.DatabaseSchema,
                    object_name = e.Value.DatabaseObjectName,
                    object_type = e.Value.DatabaseObjectType ?? "Table"
                }).OrderBy(e => e.name),
                proxy = proxyEps.Where(e => e.Value.Type.ToString() != "Composite").Select(e => new
                {
                    name       = e.Key,
                    url        = e.Value.Url,
                    methods    = e.Value.Methods,
                    is_private = e.Value.IsPrivate
                }).OrderBy(e => e.name),
                composite = proxyEps.Where(e => e.Value.Type.ToString() == "Composite").Select(e => new
                {
                    name       = e.Key,
                    url        = e.Value.Url,
                    methods    = e.Value.Methods,
                    is_private = e.Value.IsPrivate
                }).OrderBy(e => e.name),
                file = fileEps.Select(e => new
                {
                    name       = e.Key,
                    methods    = e.Value.Methods,
                    is_private = e.Value.IsPrivate
                }).OrderBy(e => e.name),
                @static = staticEps.Select(e => new
                {
                    name       = e.Key,
                    methods    = e.Value.Methods,
                    is_private = e.Value.IsPrivate
                }).OrderBy(e => e.name),
                webhook = webhookEps.Select(e => new
                {
                    name       = e.Key,
                    methods    = e.Value.Methods,
                    is_private = e.Value.IsPrivate
                }).OrderBy(e => e.name)
            });
        }).ExcludeFromDescription();

        app.MapGet("/ui/api/environments", () =>
        {
            var envSettings = app.Services.GetRequiredService<EnvironmentSettings>();
            return Results.Json(new
            {
                server_name          = envSettings.ServerName,
                allowed_environments = envSettings.AllowedEnvironments
            });
        }).ExcludeFromDescription();

        app.MapGet("/ui/api/settings", (IConfiguration config) => Results.Json(new
        {
            rate_limiting = new
            {
                enabled              = config.GetValue<bool>("RateLimiting:Enabled"),
                ip_limit             = config.GetValue<int>("RateLimiting:IpLimit"),
                ip_window_seconds    = config.GetValue<int>("RateLimiting:IpWindow"),
                token_limit          = config.GetValue<int>("RateLimiting:TokenLimit"),
                token_window_seconds = config.GetValue<int>("RateLimiting:TokenWindow")
            },
            caching = new
            {
                enabled                  = config.GetValue<bool>("Caching:Enabled"),
                provider                 = config.GetValue<string>("Caching:ProviderType") ?? "Memory",
                default_duration_seconds = config.GetValue<int>("Caching:DefaultCacheDurationSeconds"),
                max_items                = config.GetValue<int>("Caching:MemoryCacheMaxItems"),
                max_size_mb              = config.GetValue<int>("Caching:MemoryCacheSizeLimitMB")
            },
            sql_pooling = new
            {
                enabled            = config.GetValue<bool>("SqlConnectionPooling:Enabled"),
                min_pool_size      = config.GetValue<int>("SqlConnectionPooling:MinPoolSize"),
                max_pool_size      = config.GetValue<int>("SqlConnectionPooling:MaxPoolSize"),
                connection_timeout = config.GetValue<int>("SqlConnectionPooling:ConnectionTimeout"),
                command_timeout    = config.GetValue<int>("SqlConnectionPooling:CommandTimeout")
            },
            file_storage = new
            {
                directory        = config.GetValue<string>("FileStorage:StorageDirectory") ?? "",
                max_file_size_mb = config.GetValue<long>("FileStorage:MaxFileSizeBytes") / 1024 / 1024
            },
            logging = new
            {
                min_level = config.GetSection("Serilog:MinimumLevel:Default").Value ?? "Information",
                sinks     = config.GetSection("Serilog:WriteTo").GetChildren()
                                .Select(s => s["Name"] ?? "")
                                .Where(n => !string.IsNullOrEmpty(n))
                                .ToList()
            },
            endpoint_reloading = new
            {
                enabled     = config.GetValue<bool>("EndpointReloading:Enabled"),
                debounce_ms = config.GetValue<int>("EndpointReloading:DebounceMs")
            }
        })).ExcludeFromDescription();

        app.MapGet("/ui/api/logs", (HttpRequest request) =>
        {
            var limit       = int.TryParse(request.Query["limit"], out var l) ? Math.Min(l, 2000) : 200;
            var offset      = int.TryParse(request.Query["offset"], out var o) ? Math.Max(0, o) : 0;
            var filterLevel = (request.Query["level"].ToString() ?? "").ToUpperInvariant();
            try
            {
                var logDir = Path.Combine(Directory.GetCurrentDirectory(), "log");
                if (!Directory.Exists(logDir))
                    return Results.Json(new { file = "", lines = Array.Empty<object>(), total = 0, has_more = false });

                var logFiles = Directory.GetFiles(logDir, "portwayapi-*.log")
                    .OrderByDescending(f => f).ToList();

                if (logFiles.Count == 0)
                    return Results.Json(new { file = "", lines = Array.Empty<object>(), total = 0, has_more = false });

                var latestFile = logFiles[0];
                var allLines   = File.ReadAllLines(latestFile);
                var totalLines = allLines.Length;
                
                // Filter lines first, then reverse to get newest first
                var filtered = allLines
                    .Where(line => !string.IsNullOrWhiteSpace(line) && !line.Contains("████"))
                    .Select(line => {
                        var ts = ""; var lvl = "INF"; var msg = line;
                        if (line.Length > 12 && line[0] == '[')
                        {
                            var closeBracket = line.IndexOf(']');
                            if (closeBracket > 0)
                            {
                                var header = line[1..closeBracket];
                                var parts  = header.Split(' ', 2);
                                if (parts.Length == 2) { ts = parts[0]; lvl = parts[1].Trim(); }
                                msg = closeBracket + 2 < line.Length ? line[(closeBracket + 2)..].Trim() : "";
                            }
                        }
                        if (!string.IsNullOrEmpty(filterLevel) && filterLevel != "ALL" &&
                            !lvl.StartsWith(filterLevel[..Math.Min(3, filterLevel.Length)], StringComparison.OrdinalIgnoreCase))
                            return null;
                        return new { timestamp = ts, level = lvl, message = msg };
                    })
                    .Where(x => x != null)
                    .Reverse()  // Newest first
                    .ToList();

                var hasMore = offset + limit < filtered.Count;
                var paged = filtered.Skip(offset).Take(limit).ToList();

                return Results.Json(new { file = Path.GetFileName(latestFile), lines = paged, total = filtered.Count, has_more = hasMore });
            }
            catch (Exception ex)
            {
                return Results.Json(new
                {
                    file  = "",
                    lines = new[] { new { timestamp = "", level = "ERR", message = ex.Message } },
                    total = 0,
                    has_more = false
                });
            }
        }).ExcludeFromDescription();

        return app;
    }

    private static string GenerateToken(string adminApiKey)
    {
        var expiry     = DateTimeOffset.UtcNow.AddHours(TokenExpiryHours).ToUnixTimeSeconds().ToString();
        var signingKey = SHA256.HashData(Encoding.UTF8.GetBytes(adminApiKey));
        using var hmac = new HMACSHA256(signingKey);
        var sig        = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(expiry)));
        return $"{expiry}.{sig}";
    }

    private static bool ValidateToken(string token, string adminApiKey)
    {
        var dot = token.IndexOf('.');
        if (dot < 0) return false;
        var expiryStr = token[..dot];
        if (!long.TryParse(expiryStr, out var expiry) ||
            DateTimeOffset.FromUnixTimeSeconds(expiry) < DateTimeOffset.UtcNow)
            return false;
        var signingKey = SHA256.HashData(Encoding.UTF8.GetBytes(adminApiKey));
        using var hmac = new HMACSHA256(signingKey);
        var expected   = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(expiryStr)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(token[(dot + 1)..]));
    }
}
