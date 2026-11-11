namespace PortwayApi.Services;

using Dapper;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Interfaces;
using PortwayApi.Services.Caching;
using PortwayApi.Services.Providers;
using Serilog;

public enum WebhookOutcome
{
    Stored,
    UnknownId,
    NotConfigured,
    InvalidDeliveryId,
    Duplicate
}

public readonly record struct WebhookResult(WebhookOutcome Outcome, long Id = 0);

/// <summary>
/// Stores a webhook delivery in the endpoint table under the configured id casing
/// </summary>
public sealed class WebhookRequestHandler(
    IEnvironmentSettingsProvider environmentSettingsProvider,
    SqlConnectionPoolService connectionPoolService,
    ISqlProviderFactory providerFactory,
    CacheManager cacheManager)
{
    /// <summary>
    /// Inserts <paramref name="rawBody"/> as sent. The caller has validated it as JSON.
    /// </summary>
    public async Task<WebhookResult> StoreAsync(WebhookRoute route, EndpointDefinition endpoint, string rawBody, IHeaderDictionary headers, CancellationToken cancellationToken)
    {
        var webhookId = endpoint.AllowedColumns.FirstOrDefault(c => c.Equals(route.Id, StringComparison.OrdinalIgnoreCase));
        if (webhookId is null)
            return new WebhookResult(WebhookOutcome.UnknownId);

        string? deliveryId = null;
        if (endpoint.Verification?.GetValueOrDefault(webhookId) is { Deduplicate: true } rule)
        {
            deliveryId = DeliveryId(rule, rawBody, headers);
            if (deliveryId is not { Length: > 0 and <= MaxDeliveryIdLength })
                return new WebhookResult(WebhookOutcome.InvalidDeliveryId);
        }

        var (connectionString, _, _) = await environmentSettingsProvider.LoadEnvironmentOrThrowAsync(route.Env);
        if (string.IsNullOrWhiteSpace(connectionString))
            return new WebhookResult(WebhookOutcome.NotConfigured);

        await using var connection = connectionPoolService.CreateConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var provider = providerFactory.GetProvider(connectionString);
        var schema = SqlSchemaResolver.Resolve(endpoint.DatabaseSchema ?? "dbo", provider, connection.Database);
        var tableName = endpoint.DatabaseObjectName ?? "WebhookData";
        var values = new Dictionary<string, object?>
        {
            ["WebhookId"] = webhookId,
            ["Payload"] = rawBody,
            ["ReceivedAt"] = DateTime.UtcNow
        };
        if (deliveryId is not null)
            values["DeliveryId"] = deliveryId;
        var insert = SqlTableWriteBuilder.BuildInsertReturningId(provider, schema.Length > 0 ? $"{schema}.{tableName}" : tableName, values);

        long insertedId;
        try
        {
            insertedId = Convert.ToInt64(await connection.ExecuteScalarAsync(new CommandDefinition(insert.Sql, insert.Parameters, cancellationToken: cancellationToken)));
        }
        catch (System.Data.Common.DbException ex) when (deliveryId is not null && SqlErrorClassifier.IsDuplicateKey(ex))
        {
            Log.Debug("Duplicate webhook delivery ignored: {WebhookId}", webhookId);
            return new WebhookResult(WebhookOutcome.Duplicate);
        }
        finally
        {
            await cacheManager.BumpGenerationAsync(CacheGenerationKeys.SqlTable(route.Env, endpoint.DatabaseSchema, tableName));
        }

        Log.Debug("Webhook stored: {WebhookId} (ID: {InsertedId})", webhookId, insertedId);
        return new WebhookResult(WebhookOutcome.Stored, insertedId);
    }

    public const int MaxDeliveryIdLength = 255;

    /// <summary>
    /// The delivery id: the lowercase hex SHA-256 of the body, a header, or a top-level string or number field. Null when absent
    /// </summary>
    internal static string? DeliveryId(WebhookRule rule, string rawBody, IHeaderDictionary headers)
    {
        if (rule.BodyHashKey)
            return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawBody)));

        if (rule.DeliveryIdHeader is not null)
            return headers.TryGetValue(rule.DeliveryIdHeader, out var values) && values.Count == 1 ? values[0] : null;

        if (rule.DeliveryIdField is null || !JsonBody.TryParse(rawBody, out var body) || body.ValueKind != System.Text.Json.JsonValueKind.Object
            || !body.TryGetProperty(rule.DeliveryIdField, out var field))
            return null;

        return field.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => field.GetString(),
            System.Text.Json.JsonValueKind.Number => field.GetRawText(),
            _ => null
        };
    }
}
