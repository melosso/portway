namespace PortwayApi.Api;

using System.Text.Json.Nodes;
using PortwayApi.Services.Mcp;
using Serilog;

public static class McpChatEndpoints
{
    /// <summary>
    /// Client history as user and assistant turns; system and tool turns come from the server alone
    /// </summary>
    internal static List<Services.Mcp.ChatMessage> ReadHistory(JsonArray? items)
    {
        var history = new List<Services.Mcp.ChatMessage>();
        foreach (var item in items ?? [])
        {
            var role = item?["role"] is JsonValue r && r.TryGetValue<string>(out var rv) ? rv : null;
            var content = item?["content"] is JsonValue c && c.TryGetValue<string>(out var cv) ? cv : null;
            if (role is Services.Mcp.ChatMessage.User or Services.Mcp.ChatMessage.Assistant && content is not null)
                history.Add(new Services.Mcp.ChatMessage(role, content));
        }

        return history;
    }

    public static IEndpointRouteBuilder MapMcpChatEndpoints(this IEndpointRouteBuilder app)
    {
        // MCP tools as JSON, with missingMetadata listing tools without SQL field metadata
        app.MapGet("/_/api/mcp/tools", (McpChatService chat) =>
        {
            var tools = chat.GetToolDefinitions();
            var missing = chat.GetMissingMetadataEndpoints();
            return Results.Json(new
            {
                count = tools.Count,
                tools = tools.Select(t => new { t.Name, t.Description, t.DisplayDescription }),
                missingMetadata = missing
            });
        }).ExcludeFromDescription();

        // Streaming chat endpoint; SSE
        app.MapPost("/_/api/mcp/chat", async (HttpContext context, McpChatService chat) =>
        {
            JsonNode? body = null;
            try { body = await JsonNode.ParseAsync(context.Request.Body); }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to parse MCP chat request body");
                context.Response.StatusCode = 400;
                return;
            }

            var message = body?["message"]?.GetValue<string>() ?? string.Empty;
            var environment = body?["environment"]?.GetValue<string>() ?? string.Empty;
            var locale = body?["locale"]?.GetValue<string>();
            var historyArr = body?["history"]?.AsArray();

            if (string.IsNullOrWhiteSpace(message))
            {
                context.Response.StatusCode = 400;
                return;
            }

            var history = ReadHistory(historyArr);
            history.Add(new Services.Mcp.ChatMessage("user", message));

            var baseUrl = McpLoopback.BaseUrl(context);

            // tool execution uses the caller's own bearer token, never an internal one
            var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
            string? bearerToken = null;
            if (authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
                bearerToken = authHeader["Bearer ".Length..].Trim();

            // Disable buffering so SSE chunks are flushed to the client immediately
            context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();

            context.Response.ContentType = "text/event-stream; charset=utf-8";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";
            context.Response.Headers["X-Accel-Buffering"] = "no"; // prevent nginx from buffering

            await using var writer = new StreamWriter(context.Response.Body, leaveOpen: true);

            await chat.StreamAsync(history, environment, writer, baseUrl, bearerToken, locale, context.RequestAborted);
        }).ExcludeFromDescription();

        // MCP health without enumerating tools
        app.MapGet("/_/api/mcp/health", (McpChatService chat) =>
        {
            var tools = chat.GetToolDefinitions();
            var missing = chat.GetMissingMetadataEndpoints();
            return Results.Json(new
            {
                enabled = chat.IsEnabled,
                toolCount = tools.Count,
                missingMetadata = missing.Count,
                missingMetadataKeys = missing,
                providerConfigured = chat.IsEnabled
            });
        }).ExcludeFromDescription();

        return app;
    }
}
