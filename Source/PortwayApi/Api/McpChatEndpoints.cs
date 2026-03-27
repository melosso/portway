namespace PortwayApi.Api;

using System.Text.Json.Nodes;
using PortwayApi.Services.Mcp;
using Serilog;

public static class McpChatEndpoints
{
    public static IEndpointRouteBuilder MapMcpChatEndpoints(this IEndpointRouteBuilder app)
    {
        // List available MCP tools as JSON (used by chat.html and explorer.html on load).
        // missingMetadata: tool keys whose SQL field metadata could not be resolved — used by the UI
        // to surface a health warning without additional server-side log flooding.
        app.MapGet("/ui/api/mcp/tools", (McpChatService chat) =>
        {
            var tools   = chat.GetToolDefinitions();
            var missing = chat.GetMissingMetadataEndpoints();
            return Results.Json(new
            {
                count           = tools.Count,
                tools           = tools.Select(t => new { t.Name, t.Description, t.DisplayDescription }),
                missingMetadata = missing
            });
        }).ExcludeFromDescription();

        // Streaming chat endpoint — SSE
        app.MapPost("/ui/api/mcp/chat", async (HttpContext context, McpChatService chat) =>
        {
            JsonNode? body = null;
            try { body = await JsonNode.ParseAsync(context.Request.Body); }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to parse MCP chat request body");
                context.Response.StatusCode = 400;
                return;
            }

            var message     = body?["message"]?.GetValue<string>() ?? string.Empty;
            var environment = body?["environment"]?.GetValue<string>() ?? string.Empty;
            var locale      = body?["locale"]?.GetValue<string>();
            var historyArr  = body?["history"]?.AsArray();

            if (string.IsNullOrWhiteSpace(message))
            {
                context.Response.StatusCode = 400;
                return;
            }

            var history = new List<Services.Mcp.ChatMessage>();
            if (historyArr is not null)
            {
                foreach (var item in historyArr)
                {
                    var role    = item?["role"]?.GetValue<string>();
                    var content = item?["content"]?.GetValue<string>();
                    if (role is not null && content is not null)
                        history.Add(new Services.Mcp.ChatMessage(role, content));
                }
            }
            history.Add(new Services.Mcp.ChatMessage("user", message));

            // Resolve base URL for internal tool calls.
            // Use localhost bound to the server's actual port rather than the untrusted Host header
            // to prevent Host-header injection from redirecting tool calls to an arbitrary host (SSRF).
            var req = context.Request;
            var serverPort = req.Host.Port ?? (req.IsHttps ? 443 : 80);
            var baseUrl = $"{req.Scheme}://localhost:{serverPort}";

            // Forward the caller's Bearer token so tool execution can authenticate against the API.
            // Falls back to Chat:InternalApiToken in appsettings if no token is present on this request.
            var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
            string? bearerToken = null;
            if (authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
                bearerToken = authHeader["Bearer ".Length..].Trim();

            // Disable buffering so SSE chunks are flushed to the client immediately
            context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();

            context.Response.ContentType                    = "text/event-stream; charset=utf-8";
            context.Response.Headers.CacheControl           = "no-cache";
            context.Response.Headers.Connection             = "keep-alive";
            context.Response.Headers["X-Accel-Buffering"]  = "no"; // prevent nginx from buffering

            await using var writer = new StreamWriter(context.Response.Body, leaveOpen: true);

            await chat.StreamAsync(history, environment, writer, baseUrl, bearerToken, locale, context.RequestAborted);
        }).ExcludeFromDescription();

        return app;
    }
}
