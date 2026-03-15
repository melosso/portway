namespace PortwayApi.Endpoints;

using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using Serilog;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PortwayApi.Auth;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Interfaces;
using PortwayApi.Services;

public static class WebUiEndpointExtensions
{
    private const string CookieName = "portway_auth";
    private const int TokenExpiryHours = 12;
    private static readonly DateTime ProcessStartTime = DateTime.UtcNow;

    /// <summary>
    /// Registers the UI authorz. and local network-only middleware. To not make my same mistake twice: must be called before UseStaticFiles...
    /// </summary>
    public static WebApplication UseWebUiAuth(this WebApplication app, string adminApiKey)
    {
        var uiAuthEnabled = !string.IsNullOrEmpty(adminApiKey);
        var publicOrigins = app.Configuration.GetSection("WebUi:PublicOrigins").Get<string[]>() ?? [];

        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            if (!path.StartsWithSegments("/ui")) { await next(); return; }

            // Allow external clients whose origin matches a configured PublicOrigins pattern.
            // Otherwise restrict to local network only.
            var isPublicOrigin = publicOrigins.Length > 0 && IsPublicOriginAllowed(context.Request, publicOrigins);

            if (!isPublicOrigin)
            {
                var remoteIp = context.Connection.RemoteIpAddress;
                var validator = context.RequestServices.GetRequiredService<UrlValidator>();

                if (remoteIp == null || !validator.IsClientIpAllowed(remoteIp))
                {
                    context.Response.StatusCode = 403;
                    context.Response.ContentType = "application/json";

                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "Access denied",
                        clientIp = remoteIp?.ToString() ?? "Unknown",
                        requestedPath = context.Request.Path.Value,
                        success = false
                    });

                    return;
                }
            }

            // Cookie auth check, skip for the login page and the auth endpoints themselves.
            if (uiAuthEnabled &&
                !path.StartsWithSegments("/ui/login") &&
                !path.StartsWithSegments("/ui/api/auth") &&
                !path.StartsWithSegments("/ui/api/customization"))
            {
                if (!context.Request.Cookies.TryGetValue(CookieName, out var cookie) ||
                    !ValidateToken(cookie, adminApiKey))
                {
                    context.Response.Redirect($"{context.Request.PathBase}/ui/login");
                    return;
                }
            }

            await next();
        });

        return app;
    }

    /// <summary>
    /// Returns true if the request's effective origin matches any of the configured PublicOrigins patterns.
    /// Patterns support a single wildcard (*) per segment, e.g. "https://*.melosso.com".
    /// </summary>
    internal static bool IsPublicOriginAllowed(HttpRequest request, string[] patterns)
    {
        // Origin header is present on XHR/fetch; for navigation requests fall back to scheme+host.
        var origin = request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrEmpty(origin))
        {
            var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
            origin = $"{scheme}://{request.Host.Value}";
        }

        return patterns.Any(p => MatchesOriginPattern(origin, p));
    }

    private static bool MatchesOriginPattern(string origin, string pattern)
    {
        if (!pattern.Contains('*'))
            return string.Equals(origin.TrimEnd('/'), pattern.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

        // * matches a single host label (no dots), e.g. https://*.melosso.com matches
        // https://foo.melosso.com but NOT https://a.b.melosso.com.
        var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", "[^.]+") + "/?$";
        return Regex.IsMatch(origin, regexPattern, RegexOptions.IgnoreCase);
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

        // Get security settings from configuration
        var secureCookies = app.Configuration.GetValue<bool>("WebUi:SecureCookies", false);
        
        // Login
        app.MapGet("/ui/login", (HttpContext ctx) =>
            uiAuthEnabled
                ? ServeHtml(Path.Combine(wwwroot, "login.html"), ctx.Request.PathBase, appVersion, app.Configuration)
                : Results.Redirect($"{ctx.Request.PathBase}/ui/dashboard"))
            .ExcludeFromDescription();
        app.MapGet("/ui/login.html", (HttpContext ctx) => Results.Redirect($"{ctx.Request.PathBase}/ui/login"))
            .ExcludeFromDescription();

        // CSRF token endpoint
        app.MapGet("/ui/api/auth/csrf", () => Results.Json(new { csrf = WebUiAuthHelper.GenerateCsrfToken() }))
            .ExcludeFromDescription();

        // Auth endpoints
        app.MapPost("/ui/api/auth", async (HttpContext context) =>
        {
            // Rate limiting and lockout check
            var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var blockReason = WebUiAuthHelper.CheckAccess(clientIp);
            if (blockReason != null)
            {
                return Results.Json(new { error = blockReason }, statusCode: 429);
            }

            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            
            // CSRF validation
            var csrfToken = body.TryGetProperty("csrf", out var csrf) ? csrf.GetString() : null;
            if (!WebUiAuthHelper.ValidateCsrfToken(csrfToken))
            {
                WebUiAuthHelper.RecordFailedAttempt(clientIp);
                return Results.Json(new { error = "Invalid or expired CSRF token" }, statusCode: 403);
            }
            
            var provided = body.TryGetProperty("apiKey", out var kp) ? kp.GetString() ?? "" : "";
            var provHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
            var expHash  = SHA256.HashData(Encoding.UTF8.GetBytes(adminApiKey));
            if (!CryptographicOperations.FixedTimeEquals(provHash, expHash))
            {
                WebUiAuthHelper.RecordFailedAttempt(clientIp);
                return Results.Json(new { error = "Invalid API key" }, statusCode: 401);
            }

            // Success - clear failed attempts
            WebUiAuthHelper.ClearFailedAttempts(clientIp);
            // Consume the CSRF token (one-time use)
            WebUiAuthHelper.ConsumeCsrfToken(csrfToken!);

            var token = GenerateToken(adminApiKey);
            context.Response.Cookies.Append(CookieName, token, new CookieOptions
            {
                HttpOnly = true,
                Secure = secureCookies, // Configurable - set to true in production with HTTPS
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
                Secure = secureCookies,
                SameSite = SameSiteMode.Lax,
                Path     = "/",
                Expires  = DateTimeOffset.UnixEpoch
            });
            return Results.Ok();
        }).ExcludeFromDescription();

        // Page routes
        app.MapGet("/ui", (HttpContext ctx) => Results.Redirect($"{ctx.Request.PathBase}/ui/dashboard"))
            .ExcludeFromDescription();

        foreach (var page in new[] { "dashboard", "endpoints", "environments", "tokens", "settings", "logs" })
        {
            var p        = page;
            var filePath = Path.Combine(wwwroot, $"{p}.html");
            app.MapGet($"/ui/{p}",      (HttpContext ctx) => ServeHtml(filePath, ctx.Request.PathBase, appVersion, app.Configuration)).ExcludeFromDescription();
            app.MapGet($"/ui/{p}.html", (HttpContext ctx) => Results.Redirect($"{ctx.Request.PathBase}/ui/{p}")).ExcludeFromDescription();
        }

        // Data endpoints
        app.MapGet("/ui/api/customization", (IConfiguration config) =>
        {
            return Results.Json(new
            {
                promo_text = config.GetValue<string>("WebUi:Customization:PromoText"),
                promo_login = config.GetValue<bool>("WebUi:Customization:PromoLogin", false)
            });
        }).ExcludeFromDescription();

        app.MapGet("/ui/api/overview", (IOptionsMonitor<OpenApiSettings> openApiMonitor) =>
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
            var promoText     = app.Configuration.GetValue<string>("WebUi:Customization:PromoText");
            var promoLogin    = app.Configuration.GetValue<bool>("WebUi:Customization:PromoLogin", false);

            return Results.Json(new
            {
                version = appVersion,
                uptime  = $"{uptime}s",
                promo_text = promoText,
                promo_login = promoLogin,
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
                environments    = envSettings.AllowedEnvironments.Count,
                openapi_enabled = openApiMonitor.CurrentValue.Enabled
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
            var envSettings  = app.Services.GetRequiredService<EnvironmentSettings>();
            var globalPath   = Path.Combine(Directory.GetCurrentDirectory(), "environments", "settings.json");
            var lastModified = File.Exists(globalPath)
                ? new DateTimeOffset(File.GetLastWriteTimeUtc(globalPath), TimeSpan.Zero).ToUnixTimeSeconds()
                : 0L;
            return Results.Json(new
            {
                server_name          = envSettings.ServerName,
                allowed_environments = envSettings.AllowedEnvironments,
                last_modified        = lastModified
            });
        }).ExcludeFromDescription();

        // Environment CRUD
        app.MapPut("/ui/api/environments/settings", async (HttpContext context) =>
        {
            var body        = await context.Request.ReadFromJsonAsync<JsonElement>();
            var envSettings = app.Services.GetRequiredService<EnvironmentSettings>();
            var globalPath  = Path.Combine(Directory.GetCurrentDirectory(), "environments", "settings.json");

            var serverName  = body.TryGetProperty("server_name", out var sn) && sn.ValueKind == JsonValueKind.String
                ? sn.GetString() ?? envSettings.ServerName : envSettings.ServerName;
            var allowedEnvs = body.TryGetProperty("allowed_environments", out var ae) && ae.ValueKind == JsonValueKind.Array
                ? ae.EnumerateArray().Select(e => e.GetString() ?? "").Where(e => !string.IsNullOrEmpty(e)).ToList()
                : envSettings.AllowedEnvironments;

            var model = new { Environment = new { ServerName = serverName, AllowedEnvironments = allowedEnvs } };
            await File.WriteAllTextAsync(globalPath, JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true }));
            envSettings.Reload();
            return Results.Ok(new { ok = true });
        }).ExcludeFromDescription();

        app.MapGet("/ui/api/environments/{name}", (string name) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$"))
                return Results.Json(new { error = "Invalid environment name" }, statusCode: 400);

            var envPath = Path.Combine(Directory.GetCurrentDirectory(), "environments", name, "settings.json");
            if (!File.Exists(envPath))
                return Results.Json(new
                {
                    name, exists = false, server_name = (string?)null,
                    connection_string = (string?)null, connection_string_encrypted = false,
                    headers = new Dictionary<string, string>(), last_modified = 0L
                });

            try
            {
                var json  = File.ReadAllText(envPath);
                using var doc = JsonDocument.Parse(json);
                var root  = doc.RootElement;
                var cs    = root.TryGetProperty("ConnectionString", out var csEl) ? csEl.GetString() : null;
                var sn    = root.TryGetProperty("ServerName",       out var snEl) ? snEl.GetString() : null;
                var hdrs  = new Dictionary<string, string>();
                if (root.TryGetProperty("Headers", out var hEl) && hEl.ValueKind == JsonValueKind.Object)
                    foreach (var h in hEl.EnumerateObject())
                        hdrs[h.Name] = h.Value.GetString() ?? "";
                var lastMod = new DateTimeOffset(File.GetLastWriteTimeUtc(envPath), TimeSpan.Zero).ToUnixTimeSeconds();
                var isEncrypted = cs != null && cs.StartsWith("PWENC:");
                return Results.Json(new
                {
                    name, exists = true, server_name = sn,
                    connection_string = isEncrypted ? null : cs,
                    connection_string_encrypted = isEncrypted,
                    headers = hdrs, last_modified = lastMod
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = $"Failed to read settings: {ex.Message}" }, statusCode: 500);
            }
        }).ExcludeFromDescription();

        app.MapGet("/ui/api/environments/{name}/raw", (string name) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$"))
                return Results.Json(new { error = "Invalid environment name" }, statusCode: 400);

            var envPath = Path.Combine(Directory.GetCurrentDirectory(), "environments", name, "settings.json");
            if (!File.Exists(envPath))
                return Results.Json(new { error = "File not found" }, statusCode: 404);

            try
            {
                var raw     = File.ReadAllText(envPath);
                var lastMod = new DateTimeOffset(File.GetLastWriteTimeUtc(envPath), TimeSpan.Zero).ToUnixTimeSeconds();
                return Results.Json(new { content = raw, last_modified = lastMod });
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 500);
            }
        }).ExcludeFromDescription();

        app.MapPut("/ui/api/environments/{name}/raw", async (string name, HttpContext context) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$"))
                return Results.Json(new { error = "Invalid environment name" }, statusCode: 400);

            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            if (!body.TryGetProperty("content", out var contentEl) || contentEl.ValueKind != JsonValueKind.String)
                return Results.Json(new { error = "content field required" }, statusCode: 400);

            var raw = contentEl.GetString() ?? "";
            JsonElement root;
            try
            {
                using var testDoc = JsonDocument.Parse(raw);
                root = testDoc.RootElement.Clone();
                if (!root.TryGetProperty("ConnectionString", out _) &&
                    !root.TryGetProperty("ServerName", out _) &&
                    !root.TryGetProperty("Headers", out _))
                    return Results.Json(new { error = "JSON must contain at least one of: ConnectionString, ServerName, Headers" }, statusCode: 400);
            }
            catch (JsonException ex)
            {
                return Results.Json(new { error = $"Invalid JSON: {ex.Message}" }, statusCode: 400);
            }

            // Automagicallyy encrypt any plaintext ConnectionString so raw saves never bypass encryptionn
            if (root.TryGetProperty("ConnectionString", out var csEl) &&
                csEl.ValueKind == JsonValueKind.String)
            {
                var cs = csEl.GetString() ?? "";
                if (!string.IsNullOrEmpty(cs) && !cs.StartsWith("PWENC:"))
                {
                    var encrypted = PortwayApi.Helpers.SettingsEncryptionHelper.Encrypt(cs);
                    // Rebuild JSON with encrypted value
                    var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(raw)!;
                    var rebuilt = new Dictionary<string, object?>();
                    foreach (var (k, v) in dict)
                        rebuilt[k] = k == "ConnectionString"
                            ? (object?)encrypted
                            : JsonSerializer.Deserialize<object>(v.GetRawText());
                    raw = JsonSerializer.Serialize(rebuilt, new JsonSerializerOptions { WriteIndented = true });
                }
            }

            var envPath = Path.Combine(Directory.GetCurrentDirectory(), "environments", name, "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(envPath)!);
            await File.WriteAllTextAsync(envPath, raw);
            var lastMod = new DateTimeOffset(File.GetLastWriteTimeUtc(envPath), TimeSpan.Zero).ToUnixTimeSeconds();
            return Results.Ok(new { ok = true, last_modified = lastMod });
        }).ExcludeFromDescription();

        app.MapPost("/ui/api/environments/{name}/test", async (string name) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$"))
                return Results.Json(new { ok = false, error = "Invalid environment name" }, statusCode: 400);

            var envPath = Path.Combine(Directory.GetCurrentDirectory(), "environments", name, "settings.json");
            if (!File.Exists(envPath))
                return Results.Json(new { ok = false, error = "No settings.json found for this environment" }, statusCode: 404);

            try
            {
                var provider = app.Services.GetRequiredService<IEnvironmentSettingsProvider>();
                var (connectionString, _, _) = await provider.LoadEnvironmentOrThrowAsync(name);

                var sqlProviderFactory = app.Services.GetRequiredService<PortwayApi.Services.Providers.ISqlProviderFactory>();
                var sqlProvider = sqlProviderFactory.GetProvider(connectionString);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await using var conn = sqlProvider.CreateConnection(connectionString);
                await conn.OpenAsync(cts.Token);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText  = sqlProvider.HealthCheckQuery;
                cmd.CommandTimeout = 4;
                await cmd.ExecuteScalarAsync(cts.Token);

                return Results.Ok(new { ok = true, message = "Connection successful" });
            }
            catch (Exception ex)
            {
                var msg = ex.InnerException?.Message ?? ex.Message;
                return Results.Json(new { ok = false, error = msg });
            }
        }).ExcludeFromDescription();

        app.MapPost("/ui/api/environments", async (HttpContext context) =>
        {
            var body        = await context.Request.ReadFromJsonAsync<JsonElement>();
            var name        = body.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var envSettings = app.Services.GetRequiredService<EnvironmentSettings>();
            var globalPath  = Path.Combine(Directory.GetCurrentDirectory(), "environments", "settings.json");

            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$"))
                return Results.Json(new { error = "Invalid environment name. Use only letters, numbers, hyphens, and underscores." }, statusCode: 400);
            if (envSettings.AllowedEnvironments.Contains(name, StringComparer.OrdinalIgnoreCase))
                return Results.Json(new { error = "Environment already exists" }, statusCode: 409);

            var envDir         = Path.Combine(Directory.GetCurrentDirectory(), "environments", name);
            var envSettingsPath = Path.Combine(envDir, "settings.json");
            Directory.CreateDirectory(envDir);

            var serverName = body.TryGetProperty("server_name", out var sn) && sn.ValueKind == JsonValueKind.String
                ? sn.GetString() ?? envSettings.ServerName : envSettings.ServerName;
            var connStr    = body.TryGetProperty("connection_string", out var cs) && cs.ValueKind == JsonValueKind.String
                ? cs.GetString() ?? "" : "";
            var headers    = new Dictionary<string, string>();
            if (body.TryGetProperty("headers", out var hdrs) && hdrs.ValueKind == JsonValueKind.Object)
                foreach (var h in hdrs.EnumerateObject())
                    headers[h.Name] = h.Value.GetString() ?? "";

            var encryptedCs = !string.IsNullOrEmpty(connStr)
                ? PortwayApi.Helpers.SettingsEncryptionHelper.Encrypt(connStr) : "";
            var envModel = new { ConnectionString = encryptedCs, ServerName = serverName, Headers = headers };
            await File.WriteAllTextAsync(envSettingsPath,
                JsonSerializer.Serialize(envModel, new JsonSerializerOptions { WriteIndented = true }));

            var newAllowed = envSettings.AllowedEnvironments;
            newAllowed.Add(name);
            var globalModel = new { Environment = new { ServerName = envSettings.ServerName, AllowedEnvironments = newAllowed } };
            await File.WriteAllTextAsync(globalPath,
                JsonSerializer.Serialize(globalModel, new JsonSerializerOptions { WriteIndented = true }));
            envSettings.Reload();
            return Results.Json(new { ok = true, name }, statusCode: 201);
        }).ExcludeFromDescription();

        app.MapPut("/ui/api/environments/{name}", async (string name, HttpContext context) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$"))
                return Results.Json(new { error = "Invalid environment name" }, statusCode: 400);

            var envDir          = Path.Combine(Directory.GetCurrentDirectory(), "environments", name);
            var envSettingsPath = Path.Combine(envDir, "settings.json");
            var existingJson    = File.Exists(envSettingsPath) ? File.ReadAllText(envSettingsPath) : "{}";
            JsonElement existing;
            try { existing = JsonDocument.Parse(existingJson).RootElement.Clone(); }
            catch { existing = JsonDocument.Parse("{}").RootElement.Clone(); }

            var body       = await context.Request.ReadFromJsonAsync<JsonElement>();
            var serverName = body.TryGetProperty("server_name", out var sn) && sn.ValueKind == JsonValueKind.String
                ? sn.GetString()
                : (existing.TryGetProperty("ServerName", out var esn) ? esn.GetString() : null);

            var existingCs = existing.TryGetProperty("ConnectionString", out var ecs) ? ecs.GetString() ?? "" : "";
            string newCs;
            if (body.TryGetProperty("connection_string", out var cs) && cs.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(cs.GetString()))
                newCs = PortwayApi.Helpers.SettingsEncryptionHelper.Encrypt(cs.GetString()!);
            else
                newCs = existingCs;

            var headers = new Dictionary<string, string>();
            if (body.TryGetProperty("headers", out var hdrs) && hdrs.ValueKind == JsonValueKind.Object)
                foreach (var h in hdrs.EnumerateObject())
                    headers[h.Name] = h.Value.GetString() ?? "";
            else if (existing.TryGetProperty("Headers", out var ehdrs) && ehdrs.ValueKind == JsonValueKind.Object)
                foreach (var h in ehdrs.EnumerateObject())
                    headers[h.Name] = h.Value.GetString() ?? "";

            Directory.CreateDirectory(envDir);
            var envModel = new { ConnectionString = newCs, ServerName = serverName, Headers = headers };
            await File.WriteAllTextAsync(envSettingsPath,
                JsonSerializer.Serialize(envModel, new JsonSerializerOptions { WriteIndented = true }));
            var lastMod = new DateTimeOffset(File.GetLastWriteTimeUtc(envSettingsPath), TimeSpan.Zero).ToUnixTimeSeconds();
            return Results.Ok(new { ok = true, last_modified = lastMod });
        }).ExcludeFromDescription();

        app.MapMethods("/ui/api/environments/{name}", ["PATCH"], async (string name, HttpContext context) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$"))
                return Results.Json(new { error = "Invalid environment name" }, statusCode: 400);

            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            var newName = body.TryGetProperty("new_name", out var nn) ? nn.GetString()?.Trim() ?? "" : "";
            if (!System.Text.RegularExpressions.Regex.IsMatch(newName, @"^[a-zA-Z0-9_-]+$"))
                return Results.Json(new { error = "Invalid new name" }, statusCode: 400);

            var envSettings = app.Services.GetRequiredService<EnvironmentSettings>();
            var globalPath  = Path.Combine(Directory.GetCurrentDirectory(), "environments", "settings.json");
            var oldDir      = Path.Combine(Directory.GetCurrentDirectory(), "environments", name);
            var newDir      = Path.Combine(Directory.GetCurrentDirectory(), "environments", newName);

            if (!envSettings.AllowedEnvironments.Contains(name, StringComparer.OrdinalIgnoreCase))
                return Results.Json(new { error = "Environment not found" }, statusCode: 404);
            if (envSettings.AllowedEnvironments.Contains(newName, StringComparer.OrdinalIgnoreCase))
                return Results.Json(new { error = "An environment with that name already exists" }, statusCode: 409);

            if (Directory.Exists(oldDir)) Directory.Move(oldDir, newDir);

            var newAllowed  = envSettings.AllowedEnvironments
                .Select(e => e.Equals(name, StringComparison.OrdinalIgnoreCase) ? newName : e).ToList();
            var globalModel = new { Environment = new { ServerName = envSettings.ServerName, AllowedEnvironments = newAllowed } };
            await File.WriteAllTextAsync(globalPath, JsonSerializer.Serialize(globalModel, new JsonSerializerOptions { WriteIndented = true }));
            envSettings.Reload();

            return Results.Ok(new { ok = true, name = newName });
        }).ExcludeFromDescription();

        app.MapDelete("/ui/api/environments/{name}", async (string name, HttpRequest request) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$"))
                return Results.Json(new { error = "Invalid environment name" }, statusCode: 400);

            var envSettings  = app.Services.GetRequiredService<EnvironmentSettings>();
            var globalPath   = Path.Combine(Directory.GetCurrentDirectory(), "environments", "settings.json");
            var envDir       = Path.Combine(Directory.GetCurrentDirectory(), "environments", name);
            var deleteFiles  = request.Query["delete_files"] == "true";
            var newAllowed   = envSettings.AllowedEnvironments
                .Where(e => !e.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            var globalModel  = new { Environment = new { ServerName = envSettings.ServerName, AllowedEnvironments = newAllowed } };
            await File.WriteAllTextAsync(globalPath,
                JsonSerializer.Serialize(globalModel, new JsonSerializerOptions { WriteIndented = true }));
            envSettings.Reload();
            if (deleteFiles && Directory.Exists(envDir))
                Directory.Delete(envDir, true);
            return Results.Ok(new { ok = true });
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

        // Token management endpoints
        app.MapGet("/ui/api/tokens", async (HttpRequest request, TokenService tokenService) =>
        {
            var includeRevoked = request.Query["include_revoked"] == "true";
            var tokens = includeRevoked
                ? await tokenService.GetAllTokensAsync()
                : await tokenService.GetActiveTokensAsync();
            return Results.Json(tokens.Select(t => new
            {
                id                   = t.Id,
                username             = t.Username,
                description          = t.Description,
                created_at           = t.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                expires_at           = t.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                revoked_at           = t.RevokedAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                allowed_scopes       = t.AllowedScopes,
                allowed_environments = t.AllowedEnvironments,
                is_active            = t.RevokedAt == null && (t.ExpiresAt == null || t.ExpiresAt > DateTime.UtcNow)
            }));
        }).ExcludeFromDescription();

        app.MapPost("/ui/api/tokens", async (HttpContext context, TokenService tokenService) =>
        {
            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            var username     = (body.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "").Trim();
            var description  = body.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
            var scopes       = body.TryGetProperty("allowed_scopes", out var s) ? s.GetString() ?? "*" : "*";
            var environments = body.TryGetProperty("allowed_environments", out var e) ? e.GetString() ?? "*" : "*";
            int? expiresInDays = body.TryGetProperty("expires_in_days", out var exp) && exp.ValueKind == JsonValueKind.Number
                ? exp.GetInt32() : null;
            if (string.IsNullOrWhiteSpace(username))
                return Results.Json(new { error = "username is required" }, statusCode: 400);
            var active = await tokenService.GetActiveTokensAsync();
            if (active.Any(t => string.Equals(t.Username.Trim(), username, StringComparison.OrdinalIgnoreCase)))
                return Results.Json(new { error = $"A token named '{username}' already exists" }, statusCode: 409);
            var token = await tokenService.GenerateTokenAsync(username, scopes, environments, description, expiresInDays);
            return Results.Json(new { ok = true, token });
        }).ExcludeFromDescription();

        app.MapPut("/ui/api/tokens/{id:int}", async (int id, HttpContext context, TokenService tokenService) =>
        {
            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            if (body.TryGetProperty("allowed_scopes", out var scopes) && scopes.ValueKind == JsonValueKind.String)
                await tokenService.UpdateTokenScopesAsync(id, scopes.GetString() ?? "*");
            if (body.TryGetProperty("allowed_environments", out var envs) && envs.ValueKind == JsonValueKind.String)
                await tokenService.UpdateTokenEnvironmentsAsync(id, envs.GetString() ?? "*");
            if (body.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String)
                await tokenService.UpdateTokenDescriptionAsync(id, desc.GetString() ?? "");
            if (body.TryGetProperty("expires_at", out var expiresAt) &&
                expiresAt.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(expiresAt.GetString(), out var dt))
                await tokenService.SetTokenExpirationAsync(id, dt.ToUniversalTime());
            return Results.Ok(new { ok = true });
        }).ExcludeFromDescription();

        app.MapDelete("/ui/api/tokens/{id:int}", async (int id, TokenService tokenService) =>
        {
            var blockReason = await tokenService.GetRevokeBlockReasonAsync(id);
            if (blockReason != null)
                return Results.Json(new { error = blockReason }, statusCode: 409);
            var ok = await tokenService.RevokeTokenAsync(id);
            return ok ? Results.Ok(new { ok = true }) : Results.Json(new { error = "Token not found" }, statusCode: 404);
        }).ExcludeFromDescription();

        app.MapPost("/ui/api/tokens/{id:int}/unarchive", async (int id, TokenService tokenService) =>
        {
            var ok = await tokenService.UnarchiveTokenAsync(id);
            return ok ? Results.Ok(new { ok = true }) : Results.Json(new { error = "Token not found or not archived" }, statusCode: 404);
        }).ExcludeFromDescription();

        app.MapPost("/ui/api/tokens/{id:int}/rotate", async (int id, TokenService tokenService) =>
        {
            var existing = (await tokenService.GetAllTokensAsync()).FirstOrDefault(t => t.Id == id);
            if (existing == null)
                return Results.Json(new { error = "Token not found" }, statusCode: 404);
            if (existing.RevokedAt != null)
                return Results.Json(new { error = "Cannot rotate an archived token" }, statusCode: 400);

            int? expiresInDays = null;
            if (existing.ExpiresAt.HasValue)
            {
                var remaining = (int)Math.Ceiling((existing.ExpiresAt.Value - DateTime.UtcNow).TotalDays);
                expiresInDays = Math.Max(1, remaining);
            }

            // Create the replacement token first so the last-token guard won't block the revoke.
            var newToken = await tokenService.GenerateTokenAsync(
                existing.Username,
                existing.AllowedScopes,
                existing.AllowedEnvironments,
                existing.Description,
                expiresInDays);

            await tokenService.RevokeTokenAsync(id);

            return Results.Json(new
            {
                ok                   = true,
                token                = newToken,
                username             = existing.Username,
                allowed_scopes       = existing.AllowedScopes,
                allowed_environments = existing.AllowedEnvironments,
                expires_in_days      = expiresInDays
            });
        }).ExcludeFromDescription();

        app.MapGet("/ui/api/tokens/{id:int}/audit", async (int id, TokenService tokenService) =>
        {
            var entries = await tokenService.GetAuditLogAsync(tokenId: id, maxRecords: 50);
            return Results.Json(entries.Select(e => new
            {
                operation  = e.Operation,
                timestamp  = e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                details    = e.Details,
                ip_address = e.IpAddress,
                user_agent = e.UserAgent
            }));
        }).ExcludeFromDescription();

        // Server-Sent Events stream
        app.MapGet("/ui/api/events", async (HttpContext context) =>
        {
            var response = context.Response;
            response.Headers.ContentType  = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            response.Headers.Append("X-Accel-Buffering", "no"); // disable nginx buffering

            var broadcaster  = app.Services.GetRequiredService<PortwayApi.Services.SseBroadcaster>();
            var healthService = app.Services.GetRequiredService<PortwayApi.Services.HealthCheckService>();
            var ct = context.RequestAborted;

            // Push current health state immediately so the client doesn't wait for the next scheduled refresh.
            try
            {
                var report = await healthService.CheckHealthAsync(ct);
                await response.WriteAsync($"event: health\ndata: {{\"status\":\"{report.Status}\"}}\n\n", ct);
                await response.Body.FlushAsync(ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Log.Warning(ex, "Failed to push initial health state to SSE client"); }

            try
            {
                await foreach (var msg in broadcaster.SubscribeAsync(ct))
                {
                    await response.WriteAsync(msg, ct);
                    await response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { /* client disconnected, expected */ }
        }).ExcludeFromDescription();

        // Endpoint CRUD

        // PATCH /ui/api/endpoints/{type}/{**name}, rename (move directory)
        app.MapMethods("/ui/api/endpoints/{type}/{**name}", ["PATCH"], async (string type, string name, HttpContext context) =>
        {
            var (filePath, err) = ResolveEndpointPath(type, name);
            if (err != null) return Results.Json(new { error = err }, statusCode: 400);
            if (!File.Exists(filePath!))
                return Results.Json(new { error = "Endpoint not found" }, statusCode: 404);
            if (type.ToLowerInvariant() == "webhook")
                return Results.Json(new { error = "Webhook endpoint cannot be renamed" }, statusCode: 400);

            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            var newName = body.TryGetProperty("new_name", out var nn) ? nn.GetString()?.Trim() ?? "" : "";
            if (!System.Text.RegularExpressions.Regex.IsMatch(newName, @"^[a-zA-Z0-9_-]+(/[a-zA-Z0-9_-]+)*$"))
                return Results.Json(new { error = "Invalid name" }, statusCode: 400);

            var (newFilePath, newErr) = ResolveEndpointPath(type, newName);
            if (newErr != null) return Results.Json(new { error = newErr }, statusCode: 400);
            if (File.Exists(newFilePath!))
                return Results.Json(new { error = "An endpoint with that name already exists" }, statusCode: 409);

            var oldDir        = Path.GetDirectoryName(filePath!)!;
            var oldFolderName = Path.GetFileName(oldDir);
            var newDir        = Path.GetDirectoryName(newFilePath!)!;
            Directory.CreateDirectory(Path.GetDirectoryName(newDir)!);
            Directory.Move(oldDir, newDir);

            // If the entity.json Namespace equals the old folder name (doubled-key pattern, e.g. Accounts/Accounts),
            // clear it so the routing key after rename is simply newName instead of "Accounts/newName".
            var actualName = newName;
            try
            {
                var movedJson = await File.ReadAllTextAsync(newFilePath!);
                using var doc = JsonDocument.Parse(movedJson);
                if (doc.RootElement.TryGetProperty("Namespace", out var nsEl)
                    && nsEl.ValueKind == JsonValueKind.String
                    && string.Equals(nsEl.GetString(), oldFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(movedJson)!;
                    dict.Remove("Namespace");
                    await File.WriteAllTextAsync(newFilePath!,
                        JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Renamed endpoint to {NewName} but could not clear doubled Namespace from entity.json", newName); }

            var epType = TypeStringToEndpointType(type);
            if (epType.HasValue) EndpointHandler.ReloadEndpointType(epType.Value);

            return Results.Ok(new { ok = true, name = actualName });
        }).ExcludeFromDescription();

        // GET /ui/api/endpoints/{type}/{**name}?raw=true , fetch a single endpoint file (structured or raw JSON)
        app.MapGet("/ui/api/endpoints/{type}/{**name}", (string type, string name, HttpRequest request) =>
        {
            var (filePath, err) = ResolveEndpointPath(type, name);
            if (err != null) return Results.Json(new { error = err }, statusCode: 400);

            if (!File.Exists(filePath!))
                return Results.Json(new { error = "Endpoint not found" }, statusCode: 404);

            try
            {
                var raw     = File.ReadAllText(filePath!);
                var lastMod = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath!), TimeSpan.Zero).ToUnixTimeSeconds();

                if (request.Query["raw"] == "true")
                    return Results.Json(new { content = raw, last_modified = lastMod });

                var content = JsonDocument.Parse(raw).RootElement.Clone();
                return Results.Json(new { name, type, content, last_modified = lastMod, raw });
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 500);
            }
        }).ExcludeFromDescription();

        // PUT /ui/api/endpoints/{type}/{**name}?raw=true , overwrite a single endpoint file
        app.MapPut("/ui/api/endpoints/{type}/{**name}", async (string type, string name, HttpContext context) =>
        {
            var (filePath, err) = ResolveEndpointPath(type, name);
            if (err != null) return Results.Json(new { error = err }, statusCode: 400);

            var body = await context.Request.ReadFromJsonAsync<JsonElement>();

            string rawContent;
            if (context.Request.Query["raw"] == "true")
            {
                if (!body.TryGetProperty("content", out var contentEl) || contentEl.ValueKind != JsonValueKind.String)
                    return Results.Json(new { error = "content field required" }, statusCode: 400);
                rawContent = contentEl.GetString() ?? "";
            }
            else
            {
                if (!body.TryGetProperty("content", out var contentEl) || contentEl.ValueKind == JsonValueKind.Undefined)
                    return Results.Json(new { error = "content field required" }, statusCode: 400);
                rawContent = contentEl.GetRawText();
            }

            // Validate JSON and re-serialize prettified
            try
            {
                using var doc = JsonDocument.Parse(rawContent);
                rawContent = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (JsonException ex) { return Results.Json(new { error = $"Invalid JSON: {ex.Message}" }, statusCode: 400); }

            Directory.CreateDirectory(Path.GetDirectoryName(filePath!)!);
            await File.WriteAllTextAsync(filePath!, rawContent);
            var lastMod = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath!), TimeSpan.Zero).ToUnixTimeSeconds();

            var epType = TypeStringToEndpointType(type);
            if (epType.HasValue) EndpointHandler.ReloadEndpointType(epType.Value);

            return Results.Ok(new { ok = true, last_modified = lastMod });
        }).ExcludeFromDescription();

        // POST /ui/api/endpoints/{type} , create a new endpoint
        app.MapPost("/ui/api/endpoints/{type}", async (string type, HttpContext context) =>
        {
            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            var name = body.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";

            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+(/[a-zA-Z0-9_-]+)*$"))
                return Results.Json(new { error = "Invalid endpoint name. Use letters, numbers, hyphens, underscores, and forward slashes as separators." }, statusCode: 400);

            var (filePath, err) = ResolveEndpointPath(type, name);
            if (err != null) return Results.Json(new { error = err }, statusCode: 400);

            if (File.Exists(filePath!))
                return Results.Json(new { error = "Endpoint already exists" }, statusCode: 409);

            string rawContent;
            if (body.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
                rawContent = contentEl.GetString() ?? "{}";
            else if (body.TryGetProperty("content", out var contentObj) && contentObj.ValueKind == JsonValueKind.Object)
                rawContent = contentObj.GetRawText();
            else
                rawContent = "{}";

            try { using var _ = JsonDocument.Parse(rawContent); }
            catch (JsonException ex) { return Results.Json(new { error = $"Invalid JSON: {ex.Message}" }, statusCode: 400); }

            Directory.CreateDirectory(Path.GetDirectoryName(filePath!)!);
            await File.WriteAllTextAsync(filePath!, rawContent);

            var epType = TypeStringToEndpointType(type);
            if (epType.HasValue) EndpointHandler.ReloadEndpointType(epType.Value);

            return Results.Json(new { ok = true, name }, statusCode: 201);
        }).ExcludeFromDescription();

        // DELETE /ui/api/endpoints/{type}/{**name} , remove an endpoint
        app.MapDelete("/ui/api/endpoints/{type}/{**name}", (string type, string name) =>
        {
            var (filePath, err) = ResolveEndpointPath(type, name);
            if (err != null) return Results.Json(new { error = err }, statusCode: 400);

            if (!File.Exists(filePath!))
                return Results.Json(new { error = "Endpoint not found" }, statusCode: 404);

            var typeNorm = type.ToLowerInvariant();
            if (typeNorm == "webhook")
                return Results.Json(new { error = "Webhook endpoint cannot be deleted (shared entity.json)" }, statusCode: 400);

            var dir = Path.GetDirectoryName(filePath!);
            if (dir != null && Directory.Exists(dir))
                Directory.Delete(dir, true);
            else
                File.Delete(filePath!);

            var epType = TypeStringToEndpointType(type);
            if (epType.HasValue) EndpointHandler.ReloadEndpointType(epType.Value);

            return Results.Ok(new { ok = true });
        }).ExcludeFromDescription();

        app.MapGet("/ui/api/logs", async (HttpRequest request) =>
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
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .Take(3)
                    .ToList();

                if (logFiles.Count == 0)
                    return Results.Json(new { file = "", lines = Array.Empty<object>(), total = 0, has_more = false });

                // Serilog default file output template:
                // {Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}
                var logPattern = new System.Text.RegularExpressions.Regex(
                    @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[(\w{3,})\] (.*)$");

                var allEntries = new List<(string Timestamp, string Level, string Message)>();
                string? latestFile = null;

                foreach (var file in logFiles)
                {
                    try
                    {
                        string content;
                        // FileShare.ReadWrite so reads succeed while Serilog has the file open (buffered sink)
                        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var sr = new StreamReader(stream))
                            content = await sr.ReadToEndAsync();

                        var rawLines = content.Split('\n');
                        string? ts = null, lvl = null;
                        var msgParts = new List<string>();

                        foreach (var rawLine in rawLines)
                        {
                            var m = logPattern.Match(rawLine);
                            if (m.Success)
                            {
                                if (ts != null)
                                    allEntries.Add((ts, lvl ?? "INF", string.Join("\n", msgParts).TrimEnd()));
                                ts       = m.Groups[1].Value;
                                lvl      = m.Groups[2].Value.ToUpperInvariant();
                                msgParts = [m.Groups[3].Value];
                            }
                            else if (ts != null && !string.IsNullOrWhiteSpace(rawLine))
                            {
                                msgParts.Add(rawLine.TrimEnd());
                            }
                        }
                        if (ts != null)
                            allEntries.Add((ts, lvl ?? "INF", string.Join("\n", msgParts).TrimEnd()));

                        latestFile ??= file;
                        if (allEntries.Count >= limit * 5) break;
                    }
                    catch (Exception ex) { Log.Debug(ex, "Skipping inaccessible log file: {File}", file); }
                }

                // Newest first, sort by timestamp string (ISO format is lexicographically comparable)
                allEntries.Sort((a, b) => string.CompareOrdinal(b.Timestamp, a.Timestamp));

                var filtered = string.IsNullOrEmpty(filterLevel) || filterLevel == "ALL"
                    ? allEntries
                    : allEntries.Where(e => e.Level.StartsWith(
                        filterLevel[..Math.Min(3, filterLevel.Length)],
                        StringComparison.OrdinalIgnoreCase)).ToList();

                var hasMore = offset + limit < filtered.Count;
                var paged   = filtered.Skip(offset).Take(limit)
                    .Select(e => new { timestamp = e.Timestamp, level = e.Level, message = e.Message });

                return Results.Json(new { file = latestFile != null ? Path.GetFileName(latestFile) : "", lines = paged, total = filtered.Count, has_more = hasMore });
            }
            catch (Exception ex)
            {
                return Results.Json(new
                {
                    file     = "",
                    lines    = new[] { new { timestamp = "", level = "ERR", message = ex.Message } },
                    total    = 0,
                    has_more = false
                });
            }
        }).ExcludeFromDescription();

        app.MapGet("/ui/api/metrics", (HttpRequest request) =>
        {
            var period   = request.Query["period"].ToString();
            if (period != "7d" && period != "30d") period = "24h";
            var metrics  = app.Services.GetRequiredService<PortwayApi.Services.MetricsService>();
            var snapshot   = metrics.GetSnapshot(period);
            var cacheTotal = snapshot.CacheHits + snapshot.CacheMisses;
            static object BucketDto(TrafficBucket b) => new { label = b.Label, timestamp = b.Timestamp, count = b.Count };
            return Results.Json(new
            {
                period              = snapshot.Period,
                api_traffic         = snapshot.ApiTraffic.Select(BucketDto),
                ui_traffic          = snapshot.UiTraffic.Select(BucketDto),
                errors              = snapshot.Errors,
                total               = snapshot.Total,
                error_rate          = snapshot.ErrorRate,
                collecting_for_secs = snapshot.CollectingForSeconds,
                api_requests        = snapshot.ApiRequests,
                ui_requests         = snapshot.UiRequests,
                top_endpoints       = snapshot.TopEndpoints.Select(e => new { name = e.Name, count = e.Count }),
                cache = new
                {
                    hits     = snapshot.CacheHits,
                    misses   = snapshot.CacheMisses,
                    hit_rate = cacheTotal > 0 ? Math.Round((double)snapshot.CacheHits / cacheTotal, 4) : (double?)null
                }
            });
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

    private static (string? filePath, string? error) ResolveEndpointPath(string type, string name)
    {
        // Allow namespace/name paths
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+(/[a-zA-Z0-9_-]+)*$"))
            return (null, "Invalid endpoint name");

        var baseDir = Directory.GetCurrentDirectory();
        var (typeDir, isFixed) = type.ToLowerInvariant() switch
        {
            "sql"       => ("endpoints/SQL",      false),
            "proxy"     => ("endpoints/Proxy",    false),
            "composite" => ("endpoints/Proxy",    false),
            "file"      => ("endpoints/Files",    false),
            "static"    => ("endpoints/Static",   false),
            "webhook"   => ("endpoints/Webhooks", true),
            _           => ((string?)null,        false)
        };

        if (typeDir == null) return (null, $"Unknown endpoint type: {type}");

        var filePath = isFixed
            ? Path.GetFullPath(Path.Combine(baseDir, typeDir, "entity.json"))
            : Path.GetFullPath(Path.Combine(baseDir, typeDir, name, "entity.json"));

        var allowedBase = Path.GetFullPath(Path.Combine(baseDir, typeDir));
        if (!filePath.StartsWith(allowedBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !filePath.Equals(allowedBase, StringComparison.OrdinalIgnoreCase))
            return (null, "Invalid path");

        // Fallback: if path doesn't exist and name is namespaced (e.g. NS/Name), retry with just the
        // leaf folder — handles the doubled-key case where Namespace == folder name (e.g. Production/Production → Production/)
        // and the explicit-namespace case (e.g. Catalog/Products → Products/).
        if (!isFixed && !File.Exists(filePath) && name.Contains('/'))
        {
            var leafName     = name.Split('/')[^1];
            var fallbackPath = Path.GetFullPath(Path.Combine(baseDir, typeDir, leafName, "entity.json"));
            if (fallbackPath.StartsWith(allowedBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && File.Exists(fallbackPath))
                filePath = fallbackPath;
        }

        return (filePath, null);
    }

    private static EndpointType? TypeStringToEndpointType(string type) => type.ToLowerInvariant() switch
    {
        "sql"       => EndpointType.SQL,
        "proxy"     => EndpointType.Proxy,
        "composite" => EndpointType.Composite,
        "file"      => EndpointType.Files,
        "static"    => EndpointType.Static,
        "webhook"   => EndpointType.Webhook,
        _           => null
    };

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

    private static IResult ServeHtml(string filePath, PathString pathBase, string version, IConfiguration? config = null)
    {
        if (!File.Exists(filePath)) return Results.NotFound();
        var pb = pathBase.Value ?? "";
        var v  = Uri.EscapeDataString(version);
        var html = File.ReadAllText(filePath);
        html = html.Replace("<head>", $"<head>\n  <base href=\"{pb}/\">\n  <script>window.PortwayBase=\"{pb}\";</script>");
        
        // Inject Login Footer if this is the login page
        if (filePath.EndsWith("login.html") && config != null)
        {
            var footerMd = config.GetValue<string>("WebUi:Customization:LoginFooter");
            if (!string.IsNullOrEmpty(footerMd))
            {
                var footerHtml = ParseMarkdownToHtml(footerMd);
                html = html.Replace("<!-- LOGIN_FOOTER_PLACEHOLDER -->", 
                    $"<div class=\"auth-footer\">{footerHtml}</div>");
            }
        }

        // Cache-bust local CSS and JS so browsers always fetch the latest version
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"(href|src)=""(?!https?://)([^""]+\.(css|js))""",
            m => $"{m.Groups[1]}=\"{m.Groups[2]}?v={v}\"");
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static string ParseMarkdownToHtml(string md)
    {
        if (string.IsNullOrEmpty(md)) return "";
        
        // Simple server-side markdown parser (same logic as client-side)
        var html = System.Net.WebUtility.HtmlEncode(md);
        
        // Bold
        html = Regex.Replace(html, @"\*\*(.*?)\*\*", "<strong>$1</strong>");
        html = Regex.Replace(html, @"__(.*?)__", "<strong>$1</strong>");
        
        // Italic
        html = Regex.Replace(html, @"\*(.*?)\*", "<em>$1</em>");
        html = Regex.Replace(html, @"_(.*?)_", "<em>$1</em>");
        
        // Links: [text](url)
        html = Regex.Replace(html, @"\[(.*?)\]\((.*?)\)", "<a href=\"$2\" target=\"_blank\" rel=\"noopener noreferrer\">$1</a>");
        
        return html;
    }
}
