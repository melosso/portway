namespace PortwayApi.Middleware;

using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using PortwayApi.Auth;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Interfaces;
using PortwayApi.Services;
using Serilog;

public class TokenAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string? _metricsPath;

    public TokenAuthMiddleware(RequestDelegate next, Services.Telemetry.TelemetryOptions telemetry)
    {
        _next = next;

        // Prometheus scrape endpoint is open by design; operators restrict it at the network level
        _metricsPath = telemetry.ActiveMetricsPath;
    }

    public async Task InvokeAsync(
        HttpContext context,
        AuthDbContext dbContext,
        TokenService tokenService,
        IEnvironmentSettingsProvider environmentProvider,
        EnvironmentAuthService environmentAuthService)
    {
        var pathBase = context.Request.PathBase.Value ?? "";
        string env = ExtractEnvironmentFromPath(context.Request.Path);

        // Skip token validation for specific routes
        if (context.Request.Path.StartsWithSegments("/docs") ||
            context.Request.Path.StartsWithSegments("/health/live") ||
            context.Request.Path.StartsWithSegments(pathBase + "/health/live") ||
            context.Request.Path == "/health" ||
            context.Request.Path == pathBase + "/health" ||
            context.Request.Path.StartsWithSegments("/ui") ||
            context.Request.Path.StartsWithSegments("/sm") ||
            context.Request.Path == "/" ||
            context.Request.Path == "/index.html" ||
            context.Request.Path.StartsWithSegments("/favicon.ico") ||
            (_metricsPath is not null && context.Request.Path.StartsWithSegments(_metricsPath)))
        {
            Log.Debug("Skipping token authentication for {Path} (basePath: {pathBase})", context.Request.Path, pathBase);
            await _next(context);
            return;
        }

        // --- Environment-Specific Authentication ---
        bool isApiOrWebhook = context.Request.Path.StartsWithSegments("/api") ||
                             context.Request.Path.StartsWithSegments("/webhook");

        if (isApiOrWebhook && !string.IsNullOrEmpty(env))
        {
            var config = await environmentProvider.GetEnvironmentConfigAsync(env);
            if (config?.Authentication != null && config.Authentication.Enabled)
            {
                bool isEnvAuthenticated = await environmentAuthService.ValidateAsync(context, config.Authentication);

                if (isEnvAuthenticated)
                {
                    Log.Debug("Authorized for environment '{Env}' via custom authentication", env);
                    await _next(context);
                    return;
                }

                if (config.Authentication.OverrideGlobalToken)
                {
                    Log.Warning("Custom environment authentication failed for '{Env}' and OverrideGlobalToken is enabled", env);
                    await WriteErrorAsync(context, 401, "Environment authentication failed", EnvironmentChallenges(config.Authentication.Methods));
                    return;
                }

                Log.Debug("Custom environment authentication failed for '{Env}', falling back to global token", env);
            }
        }
        // -------------------------------------------

        // Continue with global authentication logic
        if (!context.Request.Headers.TryGetValue("Authorization", out var providedToken))
        {
            Log.Debug("Authorization header missing for {Path}", context.Request.Path);

            await WriteErrorAsync(context, 401, "Authentication required", BearerChallenge);
            return;
        }

        string tokenString = providedToken.ToString();

        // Extract the token from "Bearer token"
        if (tokenString.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            tokenString = tokenString["Bearer ".Length..].Trim();
        }

        // Extract endpoint name from request path
        string? endpointName = ExtractEndpointName(context.Request.Path);

        // Verify token and retrieve details in a single cache-backed call
        var tokenDetails = await tokenService.GetTokenDetailsByTokenAsync(tokenString);
        if (tokenDetails is null)
        {
            Log.Warning("Invalid or expired token used for {Path} from {RemoteIP}",
                context.Request.Path,
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

            // Log failed authentication attempt in audit trail
            await LogFailedAuthAttemptAsync(dbContext, tokenString, context);

            await WriteErrorAsync(context, 401, "Invalid or expired token", InvalidTokenChallenge);
            return;
        }

        // Check environment access
        if (!string.IsNullOrEmpty(env))
        {
            bool hasEnvironmentAccess = tokenDetails.HasAccessToEnvironment(env);

            if (!hasEnvironmentAccess)
            {
                Log.Warning("Token lacks permission for environment {Environment}. Available environments: {Environments}",
                    env, tokenDetails.AllowedEnvironments);

                // Log authorization failure in audit trail
                await LogAuthorizationFailureAsync(dbContext, tokenDetails, context, "Environment", env);

                await WriteErrorAsync(context, 403, "Access denied to environment");
                return;
            }
        }

        // Check endpoint permissions if endpoint name was successfully extracted
        if (!string.IsNullOrEmpty(endpointName))
        {
            // legacy files scope grants every file endpoint
            bool hasEndpointAccess = tokenDetails.HasAccessToEndpoint(endpointName)
                || (endpointName.StartsWith(FileScopePrefix, StringComparison.OrdinalIgnoreCase) && tokenDetails.HasAccessToEndpoint("files"));

            if (!hasEndpointAccess)
            {
                Log.Warning("Token lacks permission for endpoint {Endpoint}. Available scopes: {Scopes}",
                    endpointName, tokenDetails.AllowedScopes);

                // Log authorization failure in audit trail
                await LogAuthorizationFailureAsync(dbContext, tokenDetails, context, "Endpoint", endpointName);

                // constant message so an out of scope endpoint reads like a missing one
                await WriteErrorAsync(context, 403, "Access denied to endpoint");
                return;
            }
        }

        // Token is valid, has proper scopes, and access to the environment - proceed
        Log.Debug("Authorized {User} (Token ID: {TokenId}) for {Method} {Path}",
            tokenDetails.Username, tokenDetails.Id, context.Request.Method, context.Request.Path);
        context.Features.Set(tokenDetails);
        await _next(context);
    }

    private const string FileScopePrefix = "files/";
    private const string BearerChallenge = "Bearer realm=\"portway\"";
    private const string InvalidTokenChallenge = "Bearer realm=\"portway\", error=\"invalid_token\"";

    private static Task WriteErrorAsync(HttpContext context, int status, string error, StringValues challenge = default)
    {
        if (!StringValues.IsNullOrEmpty(challenge))
        {
            context.Response.Headers.WWWAuthenticate = challenge;
        }

        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(ErrorResponse.Of(error));
    }

    /// <summary>
    /// Basic when the environment accepts it, Bearer for every other method
    /// </summary>
    internal static StringValues EnvironmentChallenges(IEnumerable<AuthenticationMethod>? methods)
    {
        var basic = methods?.Any(m => m.Type.Equals("basic", StringComparison.OrdinalIgnoreCase)) == true;
        var other = methods?.Any(m => !m.Type.Equals("basic", StringComparison.OrdinalIgnoreCase)) != false;
        return (basic, other) switch
        {
            (true, true) => new StringValues(["Basic realm=\"portway\"", BearerChallenge]),
            (true, false) => "Basic realm=\"portway\"",
            _ => BearerChallenge
        };
    }

    /// <summary>
    /// Scope identity of a file route, files/{name} with @v{n} when the route includes a version
    /// </summary>
    internal static string? FileEndpointIdentity(string[] segments)
    {
        var start = segments.Length > 2 && EndpointVersion.TryParse(segments[0], out _) ? 1 : 0;
        if (segments.Length < start + 2 || !segments[start].Equals("files", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        EndpointVersion.TryParse(segments[0], out var version);
        return FileScopePrefix + segments[start + 1] + EndpointVersion.Suffix(start == 1 ? version : null);
    }

    /// <summary>
    /// Extract the endpoint name from the request path
    /// </summary>
    private string? ExtractEndpointName(PathString path)
    {
        // Parses /api/{env}/[{namespace}/]{name}[/{id}], /api/{env}/composite/{name} and /webhook/{env}/{id}

        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments == null || segments.Length < 3)
            return null;

        if (segments[0].Equals("api", StringComparison.OrdinalIgnoreCase))
        {
            // For composite endpoints: /api/{env}/composite/{endpointName}
            if (segments.Length >= 4 && segments[2].Equals("composite", StringComparison.OrdinalIgnoreCase))
            {
                return $"composite/{segments[3]}";
            }

            if (FileEndpointIdentity(segments[2..]) is { } file)
            {
                return file;
            }

            // Same longest match resolver as the controller; keeps scope checks and dispatch aligned.
            if (Api.EndpointController.ResolveEndpointIdentity(segments[2..]) is { } resolved)
            {
                return resolved.Namespace.Length > 0 ? $"{resolved.Namespace}/{resolved.Name}" : resolved.Name;
            }

            // Fall back to non-namespaced format: /api/{env}/{endpointName}
            if (segments.Length >= 3)
            {
                return segments[2];
            }
        }
        else if (segments[0].Equals("webhook", StringComparison.OrdinalIgnoreCase))
        {
            // For webhook endpoints: /webhook/{env}/{webhookName}
            if (segments.Length >= 3)
            {
                return $"webhook/{segments[2]}";
            }
        }

        return null;
    }

    /// <summary>
    /// Extract the environment from the request path
    /// </summary>
    private string ExtractEnvironmentFromPath(PathString path)
    {
        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments == null || segments.Length < 2)
            return string.Empty;

        // For paths like /api/{env}/...
        if (segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) && segments.Length >= 2)
            return segments[1];

        // For paths like /webhook/{env}/...
        if (segments[0].Equals("webhook", StringComparison.OrdinalIgnoreCase) && segments.Length >= 2)
            return segments[1];

        return string.Empty;
    }

    /// <summary>
    /// Log failed authentication attempts for security auditing
    /// </summary>
    private static async Task LogFailedAuthAttemptAsync(AuthDbContext dbContext, string tokenString, HttpContext context)
    {
        try
        {
            var auditEntry = new AuthTokenAudit
            {
                TokenId = null, // No valid token found
                Username = "Unknown",
                Operation = "FailedAuth",
                OldTokenHash = null,
                NewTokenHash = null,
                Timestamp = DateTime.UtcNow,
                Details = JsonSerializer.Serialize(new
                {
                    RequestPath = context.Request.Path.Value,
                    Method = context.Request.Method,
                    TokenPrefix = tokenString.Length > 10 ? tokenString[..10] : tokenString,
                    UserAgent = context.Request.Headers.UserAgent.ToString(),
                    Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                }),
                Source = "PortwayApi.Middleware",
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                UserAgent = context.Request.Headers.UserAgent.ToString()
            };

            await dbContext.TokenAudits.AddAsync(auditEntry, context.RequestAborted);
            await dbContext.SaveChangesAsync(context.RequestAborted);
        }
        catch (Exception ex)
        {
            // Don't throw exceptions from audit logging
            Log.Error(ex, "Failed to log authentication attempt");
        }
    }

    /// <summary>
    /// Log authorization failures for security auditing
    /// </summary>
    private static async Task LogAuthorizationFailureAsync(AuthDbContext dbContext, AuthToken tokenDetails,
        HttpContext context, string resourceType, string resourceName)
    {
        try
        {
            var auditEntry = new AuthTokenAudit
            {
                TokenId = tokenDetails.Id,
                Username = tokenDetails.Username,
                Operation = "AuthorizationFailed",
                OldTokenHash = null,
                NewTokenHash = null,
                Timestamp = DateTime.UtcNow,
                Details = JsonSerializer.Serialize(new
                {
                    ResourceType = resourceType, // "Environment" or "Endpoint"
                    ResourceName = resourceName,
                    RequestPath = context.Request.Path.Value,
                    Method = context.Request.Method,
                    AvailableScopes = tokenDetails.AllowedScopes,
                    AvailableEnvironments = tokenDetails.AllowedEnvironments,
                    Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                }),
                Source = "PortwayApi.Middleware",
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                UserAgent = context.Request.Headers.UserAgent.ToString()
            };

            await dbContext.TokenAudits.AddAsync(auditEntry, context.RequestAborted);
            await dbContext.SaveChangesAsync(context.RequestAborted);
        }
        catch (Exception ex)
        {
            // Don't throw exceptions from audit logging
            Log.Error(ex, "Failed to log authorization failure");
        }
    }
}

// Extension method to make it easier to add the middleware to the pipeline
public static class TokenAuthMiddlewareExtensions
{
    public static IApplicationBuilder UseTokenAuthentication(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<TokenAuthMiddleware>();
    }
}