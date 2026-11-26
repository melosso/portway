namespace PortwayApi.Endpoints;

using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using PortwayApi.Auth;
using PortwayApi.Classes.WebUi;
using PortwayApi.Helpers;


public static partial class WebUiEndpointExtensions
{
    private static void MapTokenRoutes(WebApplication app, PortwayApi.Services.Configuration.ConfigAuditService configAudit)
    {
        void Audit(HttpContext ctx, string action, string target, string? details = null)
            => configAudit.Record(action, "token", target, ctx.Connection.RemoteIpAddress?.ToString(), details, null);

        static async Task<string> NameOf(TokenService tokens, int id)
            => (await tokens.GetAllTokensAsync()).FirstOrDefault(t => t.Id == id)?.Username ?? $"#{id}";

        app.MapGet("/_/api/tokens", async (HttpRequest request, TokenService tokenService) =>
        {
            var includeRevoked = request.Query["include_revoked"] == "true";
            var tokens = includeRevoked
                ? await tokenService.GetAllTokensAsync()
                : await tokenService.GetActiveTokensAsync();
            return Results.Json(tokens.Select(t => new
            {
                id = t.Id,
                username = t.Username,
                description = t.Description,
                created_at = t.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                expires_at = t.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                revoked_at = t.RevokedAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                last_used_at = t.LastUsedAt?.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                allowed_scopes = t.AllowedScopes,
                allowed_environments = t.AllowedEnvironments,
                allowed_tenants = t.Tenants.ToDictionary(g => g.Key, g => g.Value.Order(StringComparer.Ordinal).ToArray()),
                rate_limit_requests = t.RateLimitRequests,
                rate_limit_window_seconds = t.RateLimitWindowSeconds,
                is_active = t.IsActive
            }));
        }).ExcludeFromDescription();

        app.MapPost("/_/api/tokens", async (HttpContext context, TokenService tokenService) =>
        {
            var request = await context.Request.ReadFromJsonAsync<TokenCreateRequest>();
            if (request is null)
                return PortwayResults.MinimalError("username is required", 400);
            var error = await UiValidationHelper.FirstErrorAsync(request, context.RequestServices, context.RequestAborted);
            if (error != null)
                return PortwayResults.MinimalError(error, error.EndsWith("already exists") ? 409 : 400);
            var token = await tokenService.GenerateTokenAsync(
                request.Username.Trim(), request.AllowedScopes, request.AllowedEnvironments, request.Description,
                request.ExpiresInDays, request.RateLimitRequests, request.RateLimitWindowSeconds,
                TenantGrants.TryCreate(request.AllowedTenants, out var grants, out _) ? TenantGrants.Serialize(grants) : TenantGrants.Empty);
            Audit(context, "create", request.Username.Trim());
            return Results.Json(new { ok = true, token });
        }).ExcludeFromDescription();

        app.MapPut("/_/api/tokens/{id:int}", async (int id, HttpContext context, TokenService tokenService) =>
        {
            var body = await context.Request.ReadFromJsonAsync<JsonElement>();

            if (body.TryGetProperty("allowed_scopes", out var scopes) && scopes.ValueKind == JsonValueKind.String)
            {
                if (TokenScopeValidator.Validate(scopes.GetString()) is { } scopeError)
                    return PortwayResults.MinimalError(scopeError, 400);

                if (!await tokenService.UpdateTokenScopesAsync(id, scopes.GetString() ?? "*"))
                    return PortwayResults.MinimalError("Cannot narrow the last full-access token", 409);
            }

            if (body.TryGetProperty("allowed_environments", out var envs) && envs.ValueKind == JsonValueKind.String)
            {
                if (!await tokenService.UpdateTokenEnvironmentsAsync(id, envs.GetString() ?? "*"))
                    return PortwayResults.MinimalError("Cannot narrow the last full-access token", 409);
            }

            if (body.TryGetProperty("allowed_tenants", out var tenants))
            {
                Dictionary<string, List<string>>? raw;
                try { raw = tenants.ValueKind == JsonValueKind.Null ? null : tenants.Deserialize<Dictionary<string, List<string>>>(); }
                catch (JsonException) { return PortwayResults.MinimalError("allowed_tenants must be an object of header names to lists of values", 400); }
                if (!TenantGrants.TryCreate(raw, out var grants, out var tenantError))
                    return PortwayResults.MinimalError(tenantError, 400);
                if (!await tokenService.UpdateTokenTenantsAsync(id, grants))
                    return PortwayResults.MinimalError("Token not found", 404);
            }

            if (body.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String)
                await tokenService.UpdateTokenDescriptionAsync(id, desc.GetString() ?? "");

            if (body.TryGetProperty("expires_at", out var expiresAt) &&
                expiresAt.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(expiresAt.GetString(), out var dt))
                await tokenService.SetTokenExpirationAsync(id, dt.ToUniversalTime());

            // Rate limit override, explicit null reverts to the global default
            if (body.TryGetProperty("rate_limit_requests", out var rlRequests))
            {
                int? requests = rlRequests.ValueKind == JsonValueKind.Number ? rlRequests.GetInt32() : null;
                int? window = body.TryGetProperty("rate_limit_window_seconds", out var rlWindow) &&
                              rlWindow.ValueKind == JsonValueKind.Number ? rlWindow.GetInt32() : null;

                if (requests is < 1 || window is < 1 or > 86400)
                    return PortwayResults.MinimalError("Rate limit values are out of range", 400);

                await tokenService.UpdateTokenRateLimitAsync(id, requests, requests.HasValue ? window ?? 60 : null);
            }

            var changed = new[] { "allowed_scopes", "allowed_environments", "allowed_tenants", "description", "expires_at", "rate_limit_requests" }
                .Where(key => body.TryGetProperty(key, out _));
            Audit(context, "update", await NameOf(tokenService, id), string.Join(", ", changed));
            return Results.Ok(new { ok = true });
        }).ExcludeFromDescription();

        app.MapDelete("/_/api/tokens/{id:int}", async (int id, HttpContext context, TokenService tokenService) =>
        {
            var blockReason = await tokenService.GetRevokeBlockReasonAsync(id);

            if (blockReason != null)
                return PortwayResults.MinimalError(blockReason, 409);

            var ok = await tokenService.RevokeTokenAsync(id);
            if (ok) Audit(context, "archive", await NameOf(tokenService, id));

            return ok ? Results.Ok(new { ok = true }) : PortwayResults.MinimalError("Token not found", 404);
        }).ExcludeFromDescription();

        app.MapPost("/_/api/tokens/{id:int}/unarchive", async (int id, HttpContext context, TokenService tokenService) =>
        {
            var ok = await tokenService.UnarchiveTokenAsync(id);
            if (ok) Audit(context, "restore", await NameOf(tokenService, id));
            return ok ? Results.Ok(new { ok = true }) : PortwayResults.MinimalError("Token not found or not archived", 404);
        }).ExcludeFromDescription();

        app.MapPost("/_/api/tokens/{id:int}/rotate", async (int id, HttpContext context, TokenService tokenService) =>
        {
            var existing = (await tokenService.GetAllTokensAsync()).FirstOrDefault(t => t.Id == id);

            if (existing == null)
                return PortwayResults.MinimalError("Token not found", 404);

            if (existing.RevokedAt != null)
                return PortwayResults.MinimalError("Cannot rotate an archived token", 400);

            int? expiresInDays = null;

            if (existing.ExpiresAt.HasValue)
            {
                var remaining = (int)Math.Ceiling((existing.ExpiresAt.Value - DateTime.UtcNow).TotalDays);
                expiresInDays = Math.Max(1, remaining);
            }

            // Create the replacement token first so the last-token guard won't block the revoke
            var newToken = await tokenService.GenerateTokenAsync(
                existing.Username,
                existing.AllowedScopes,
                existing.AllowedEnvironments,
                existing.Description,
                expiresInDays,
                existing.RateLimitRequests,
                existing.RateLimitWindowSeconds,
                existing.AllowedTenants);

            await tokenService.RevokeTokenAsync(id);
            Audit(context, "rotate", existing.Username, $"replaced token {id}");

            return Results.Json(new
            {
                ok = true,
                token = newToken,
                username = existing.Username,
                allowed_scopes = existing.AllowedScopes,
                allowed_environments = existing.AllowedEnvironments,
                allowed_tenants = existing.Tenants.ToDictionary(g => g.Key, g => g.Value.Order(StringComparer.Ordinal).ToArray()),
                expires_in_days = expiresInDays
            });
        }).ExcludeFromDescription();

        // history follows the token name so rotated and archived predecessors stay visible
        app.MapGet("/_/api/tokens/{id:int}/audit", async (int id, TokenService tokenService) =>
        {
            var token = (await tokenService.GetAllTokensAsync()).FirstOrDefault(t => t.Id == id);
            if (token is null)
                return PortwayResults.MinimalError("Token not found", 404);
            var entries = await tokenService.GetAuditLogAsync(username: token.Username, maxRecords: 50);
            return Results.Json(entries.Select(e => new
            {
                operation = e.Operation,
                timestamp = e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                details = e.Details,
                ip_address = e.IpAddress,
                user_agent = e.UserAgent
            }));
        }).ExcludeFromDescription();

        // Server-Sent Events stream
    }
}
