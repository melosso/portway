using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using System.Data.Common;
using PortwayApi.Services.Providers;

using Dapper;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Xml.Linq;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Interfaces;
using PortwayApi.Services;
using PortwayApi.Services.Files;
using Serilog;
using System.Runtime.CompilerServices;

namespace PortwayApi.Api;

public partial class EndpointController
{
    /// <summary>Handles webhook requests</summary>
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
                return PortwayResults.BadRequest(this, "Environment is not configured properly.");
            }

            // Load webhook endpoint configuration using the namespace-aware key
            if (TryResolveEndpoint(EndpointType.Webhook, webhookEndpointKey, null, out var endpointConfig,
                $"Endpoint '{webhookEndpointKey}' is not configured properly.") is { } resolveError)
            {
                return resolveError;
            }

            // Get table name and schema from the configuration
            var tableName = endpointConfig.DatabaseObjectName ?? "WebhookData";
            var schema = endpointConfig.DatabaseSchema ?? "dbo";

            // Validate webhook ID against allowed columns
            var allowedColumns = endpointConfig.AllowedColumns ?? new List<string>();
            if (allowedColumns.Any() &&
                !allowedColumns.Contains(webhookId, StringComparer.OrdinalIgnoreCase))
            {
                return PortwayResults.NotFound(this, $"Webhook ID '{webhookId}' is not configured.");
            }

            // Insert webhook data
            await using var connection = _connectionPoolService.CreateConnection(connectionString);
            await connection.OpenAsync();

            var insertQuery = $@"
                INSERT INTO [{schema}].[{tableName}] (WebhookId, Payload, ReceivedAt)
                OUTPUT INSERTED.Id
                VALUES (@WebhookId, @Payload, @ReceivedAt)";

            var insertedId = await connection.ExecuteScalarAsync<int>(insertQuery, new
            {
                WebhookId = webhookId,
                Payload = payload.ToString(),
                ReceivedAt = DateTime.UtcNow
            });

            Log.Debug("Webhook processed successfully: {WebhookId} (ID: {InsertedId})", 
                webhookId, insertedId);

            // Return 201 Created with location header for consistency
            var locationUrl = $"/api/{env}/{webhookEndpointKey}/{webhookId}/{insertedId}";
            return PortwayResults.Create(this, locationUrl, "Webhook processed successfully.", id: insertedId);
        }
        catch (Exception ex)
        {
            return HandleUnexpectedError(ex, "webhook", webhookId, "Error processing webhook. Please check the logs for more details.");
        }
    }

}
