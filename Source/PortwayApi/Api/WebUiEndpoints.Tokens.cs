namespace PortwayApi.Endpoints;

using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using Serilog;
using System.Text;
using System.Text.Encodings.Web;
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


public static partial class WebUiEndpointExtensions
{
    private static void MapTokenRoutes(WebApplication app)
    {
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

            // Create the replacement token first so the last-token guard won't block the revoke
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
    }
}
