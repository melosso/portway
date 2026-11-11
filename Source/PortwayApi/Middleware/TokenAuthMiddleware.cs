namespace PortwayApi.Middleware;

using System.Text.Json;
using Microsoft.Extensions.Primitives;
using PortwayApi.Auth;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Interfaces;
using Serilog;

public sealed class TokenAuthMiddleware
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
        AuthFailureAuditQueue auditQueue,
        TokenService tokenService,
        IEnvironmentSettingsProvider environmentProvider,
        EnvironmentAuthService environmentAuthService,
        EnvironmentSettings environmentSettings,
        TimeProvider timeProvider)
    {
        var pathBase = context.Request.PathBase.Value ?? "";
        string env = ExtractEnvironmentFromPath(context.Request.Path);

        // Skip token validation for specific routes
        if (context.Request.Path.StartsWithSegments("/docs") ||
            context.Request.Path.StartsWithSegments("/health/live") ||
            context.Request.Path.StartsWithSegments(pathBase + "/health/live") ||
            context.Request.Path == "/health" ||
            context.Request.Path == pathBase + "/health" ||
            context.Request.Path.StartsWithSegments("/_") ||
            context.Request.Path.StartsWithSegments("/sm") ||
            (context.Request.Path.StartsWithSegments("/js/vendor") && context.Request.Path.Value!.EndsWith(".map", StringComparison.OrdinalIgnoreCase)) ||
            context.Request.Path == "/" ||
            context.Request.Path == "/index.html" ||
            context.Request.Path.StartsWithSegments("/favicon.ico") ||
            (_metricsPath is not null && context.Request.Path.StartsWithSegments(_metricsPath)))
        {
            Log.Debug("Skipping token authentication for {Path} (basePath: {pathBase})", context.Request.Path, pathBase);
            await _next(context);
            return;
        }

        if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path.StartsWithSegments("/api") && !string.IsNullOrEmpty(env)
            && FindVerifiedWebhook(context, env) is { } webhook)
        {
            var (route, endpoint, rule) = webhook;
            var verified = await VerifyWebhookAsync(context, route, endpoint, rule, environmentProvider, environmentSettings, timeProvider);
            if (!verified)
            {
                await WriteErrorAsync(context, 401, "Authentication required", BearerChallenge);
                return;
            }

            context.Items[VerifiedWebhookKey] = route;
            await _next(context);
            return;
        }

        // --- Environment-Specific Authentication ---
        bool isApiOrWebhook = context.Request.Path.StartsWithSegments("/api") ||
                             context.Request.Path.StartsWithSegments("/webhook");

        if (isApiOrWebhook && !string.IsNullOrEmpty(env))
        {
            EnvironmentConfig? config;
            bool isEnvAuthenticated = false;
            try
            {
                config = await environmentProvider.GetEnvironmentConfigAsync(env);
                if (config?.Authentication is { Enabled: true })
                    isEnvAuthenticated = await environmentAuthService.ValidateAsync(context, config.Authentication,
                        template => environmentProvider.ResolveOrThrowAsync(env, template, SecretReference.ResolveExact, context.RequestAborted));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // fail closed, no token fallback
                Log.Debug(ex, "Environment configuration for {Environment} could not be loaded", env);
                await WriteErrorAsync(context, 500, "Internal server error");
                return;
            }

            if (config?.Authentication != null && config.Authentication.Enabled)
            {

                if (isEnvAuthenticated)
                {
                    if (context.Items[EnvironmentAuthService.MatchedMethodKey] is AuthenticationMethod { AllowedEndpoints: { } allowed }
                        && ExtractEndpointName(context) is { Length: > 0 } scopedName
                        && !HasEndpointAccess(name => ScopeMatcher.Matches(allowed, name), scopedName))
                    {
                        Log.Warning("Environment authentication for {Env} lacks permission for endpoint {Endpoint}", env, scopedName);
                        await WriteErrorAsync(context, 403, "Access denied to endpoint");
                        return;
                    }

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
        string? endpointName = ExtractEndpointName(context);

        // Verify token and retrieve details in a single cache-backed call
        var tokenDetails = await tokenService.GetTokenDetailsByTokenAsync(tokenString);
        if (tokenDetails is null)
        {
            Log.Warning("Invalid or expired token used for {Path} from {RemoteIP}",
                context.Request.Path,
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

            // Log failed authentication attempt in audit trail
            auditQueue.Enqueue(FailedAuthAudit(tokenString, context));

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
                auditQueue.Enqueue(AuthorizationFailureAudit(tokenDetails, context, "Environment", env));

                await WriteErrorAsync(context, 403, "Access denied to environment");
                return;
            }
        }

        // Check endpoint permissions if endpoint name was successfully extracted
        if (!string.IsNullOrEmpty(endpointName))
        {
            bool hasEndpointAccess = HasEndpointAccess(tokenDetails.HasAccessToEndpoint, endpointName);

            if (!hasEndpointAccess)
            {
                Log.Warning("Token lacks permission for endpoint {Endpoint}. Available scopes: {Scopes}",
                    endpointName, tokenDetails.AllowedScopes);

                // Log authorization failure in audit trail
                auditQueue.Enqueue(AuthorizationFailureAudit(tokenDetails, context, "Endpoint", endpointName));

                // constant message so an out of scope endpoint reads like a missing one
                await WriteErrorAsync(context, 403, "Access denied to endpoint");
                return;
            }
        }

        // Token is valid, has proper scopes, and access to the environment - proceed
        Log.Debug("Authorized {User} (Token ID: {TokenId}) for {Method} {Path}",
            tokenDetails.Username, tokenDetails.Id, context.Request.Method, context.Request.Path);
        context.Features.Set(tokenDetails);
        context.Items[AuthorizationAcceptedKey] = true;
        await _next(context);
    }

    public const string AuthorizationAcceptedKey = "Portway.AuthorizationAccepted";
    public const string VerifiedWebhookKey = "Portway.VerifiedWebhook";

    /// <summary>
    /// The webhook route, endpoint and rule when the request targets an id with Verification
    /// </summary>
    internal static (Services.WebhookRoute Route, EndpointDefinition Endpoint, Services.WebhookRule Rule)? FindVerifiedWebhook(HttpContext context, string env)
    {
        var segments = context.Request.Path.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries)[2..];
        if (!Services.WebhookRoute.TryParse(env, segments, context, out var route)
            || !EndpointHandler.GetSqlWebhookEndpoints().TryGetValue(route.EndpointKey, out var endpoint)
            || endpoint.Verification is null)
            return null;

        foreach (var (id, rule) in endpoint.Verification)
        {
            if (id.Equals(route.Id, StringComparison.OrdinalIgnoreCase))
                return (route with { Id = id }, endpoint, rule);
        }
        return null;
    }

    private static async Task<bool> VerifyWebhookAsync(
        HttpContext context,
        Services.WebhookRoute route,
        EndpointDefinition endpoint,
        Services.WebhookRule rule,
        IEnvironmentSettingsProvider environmentProvider,
        EnvironmentSettings environmentSettings,
        TimeProvider timeProvider)
    {
        string? reason = null;
        if (rule.Error is not null)
            reason = "configuration error";
        else if (!environmentSettings.IsEnvironmentAllowed(route.Env) || !endpoint.AllowsEnvironment(route.Env))
            reason = "environment not allowed";
        else if (!context.Request.Headers.ContainsKey(rule.RequiredHeader))
            reason = "signature or credential header missing";

        if (reason is null)
        {
            context.Request.EnableBuffering();
            using var buffer = new MemoryStream();
            await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);
            context.Request.Body.Position = 0;

            var secrets = new List<string>(2);
            foreach (var name in new[] { rule.SecretName, rule.PreviousSecretName })
            {
                if (name is not null && await environmentProvider.GetSecretAsync(route.Env, name, context.RequestAborted) is { } value)
                    secrets.Add(value);
            }

            if (secrets.Count == 0)
                reason = "secret missing";
            else if (Services.WebhookVerifier.Verify(rule, context.Request.Headers, buffer.GetBuffer().AsSpan(0, (int)buffer.Length), secrets, timeProvider.GetUtcNow()) is { Verified: false } failed)
                reason = failed.Reason;
        }

        if (reason is null)
            return true;

        Log.Warning("Webhook verification failed for {Endpoint}/{Id} ({Scheme}): {Reason}", route.EndpointKey, route.Id, rule.Scheme, reason);
        context.RequestServices.GetService<Services.Telemetry.PortwayMetrics>()?.WebhookVerificationFailed(route.EndpointKey, rule.Scheme);
        return false;
    }

    // legacy files scope grants every file endpoint
    private static bool HasEndpointAccess(Func<string, bool> hasAccess, string endpointName) =>
        hasAccess(endpointName) || (endpointName.StartsWith(FileScopePrefix, StringComparison.OrdinalIgnoreCase) && hasAccess("files"));
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
    private string? ExtractEndpointName(HttpContext context)
    {
        var path = context.Request.Path;
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
            if (Api.EndpointController.ResolveEndpointIdentity(context, segments[2..]) is { } resolved)
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
    /// Audit entry for a failed authentication attempt
    /// </summary>
    private static AuthTokenAudit FailedAuthAudit(string tokenString, HttpContext context)
    {
        return new AuthTokenAudit
        {
            TokenId = null, // No valid token found
            Username = "Unknown",
            Operation = AuthFailureAuditQueue.FailedAuth,
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
    }

    /// <summary>
    /// Audit entry for an authorization failure
    /// </summary>
    private static AuthTokenAudit AuthorizationFailureAudit(AuthToken tokenDetails,
        HttpContext context, string resourceType, string resourceName)
    {
        return new AuthTokenAudit
        {
            TokenId = tokenDetails.Id,
            Username = tokenDetails.Username,
            Operation = AuthFailureAuditQueue.AuthorizationFailed,
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