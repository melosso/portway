using Microsoft.AspNetCore.Mvc;

using Dapper;
using System.Text.Json;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using Serilog;

namespace PortwayApi.Api;

public partial class EndpointController
{
    /// <summary>
    /// Handles webhook requests
    /// </summary>
    private async Task<IActionResult> HandleWebhookRequest(
        string env,
        string webhookEndpointKey,
        string webhookId,
        JsonElement payload)
    {
        var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}";
        Log.Debug("Webhook received: {Method} {Url}", Request.Method, requestUrl);

        try
        {
            // Validate environment and get connection string
            var (connectionString, serverName, _) = await _environmentSettingsProvider.LoadEnvironmentOrThrowAsync(env);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return PortwayResults.BadRequest("Environment is not configured properly.");
            }

            // Load webhook endpoint configuration using the namespace-aware key
            if (TryResolveEndpoint(EndpointType.Webhook, webhookEndpointKey, null, out var endpointConfig,
                $"Endpoint '{webhookEndpointKey}' is not configured properly.") is { } resolveError)
            {
                return resolveError;
            }

            var allowedColumns = endpointConfig.AllowedColumns ?? new List<string>();
            if (allowedColumns.Any() &&
                !allowedColumns.Contains(webhookId, StringComparer.OrdinalIgnoreCase))
            {
                return PortwayResults.NotFound($"Webhook ID '{webhookId}' is not configured.");
            }

            await using var connection = _connectionPoolService.CreateConnection(connectionString);
            await connection.OpenAsync();

            var provider = _providerFactory.GetProvider(connectionString);
            var schema = SqlSchemaResolver.Resolve(endpointConfig.DatabaseSchema ?? "dbo", provider, connection.Database);
            var tableName = endpointConfig.DatabaseObjectName ?? "WebhookData";
            var insert = SqlTableWriteBuilder.BuildInsertReturningId(provider, schema.Length > 0 ? $"{schema}.{tableName}" : tableName,
                new Dictionary<string, object?>
                {
                    ["WebhookId"] = webhookId,
                    ["Payload"] = payload.ToString(),
                    ["ReceivedAt"] = DateTime.UtcNow
                });
            var insertedId = Convert.ToInt64(await connection.ExecuteScalarAsync(insert.Sql, insert.Parameters));

            Log.Debug("Webhook processed successfully: {WebhookId} (ID: {InsertedId})",
                webhookId, insertedId);

            // Return 201 Created with location header for consistency
            var locationUrl = $"/api/{env}/{EndpointVersion.RoutePath(webhookEndpointKey)}/{webhookId}/{insertedId}";
            return PortwayResults.Create(locationUrl, "Webhook processed successfully.", id: insertedId);
        }
        catch (Exception ex)
        {
            return HandleUnexpectedError(ex, "webhook", webhookId, "Error processing webhook. Please check the logs for more details.");
        }
    }

}
