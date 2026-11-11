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
    NotConfigured
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
    public async Task<WebhookResult> StoreAsync(WebhookRoute route, EndpointDefinition endpoint, string rawBody, CancellationToken cancellationToken)
    {
        var webhookId = endpoint.AllowedColumns.FirstOrDefault(c => c.Equals(route.Id, StringComparison.OrdinalIgnoreCase));
        if (webhookId is null)
            return new WebhookResult(WebhookOutcome.UnknownId);

        var (connectionString, _, _) = await environmentSettingsProvider.LoadEnvironmentOrThrowAsync(route.Env);
        if (string.IsNullOrWhiteSpace(connectionString))
            return new WebhookResult(WebhookOutcome.NotConfigured);

        await using var connection = connectionPoolService.CreateConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var provider = providerFactory.GetProvider(connectionString);
        var schema = SqlSchemaResolver.Resolve(endpoint.DatabaseSchema ?? "dbo", provider, connection.Database);
        var tableName = endpoint.DatabaseObjectName ?? "WebhookData";
        var insert = SqlTableWriteBuilder.BuildInsertReturningId(provider, schema.Length > 0 ? $"{schema}.{tableName}" : tableName,
            new Dictionary<string, object?>
            {
                ["WebhookId"] = webhookId,
                ["Payload"] = rawBody,
                ["ReceivedAt"] = DateTime.UtcNow
            });

        long insertedId;
        try
        {
            insertedId = Convert.ToInt64(await connection.ExecuteScalarAsync(new CommandDefinition(insert.Sql, insert.Parameters, cancellationToken: cancellationToken)));
        }
        finally
        {
            await cacheManager.BumpGenerationAsync(CacheGenerationKeys.SqlTable(route.Env, endpoint.DatabaseSchema, tableName));
        }

        Log.Debug("Webhook stored: {WebhookId} (ID: {InsertedId})", webhookId, insertedId);
        return new WebhookResult(WebhookOutcome.Stored, insertedId);
    }
}
