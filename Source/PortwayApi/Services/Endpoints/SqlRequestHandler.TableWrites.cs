namespace PortwayApi.Services;

using System.Data.Common;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using Serilog;

/// <summary>Direct table writes for WriteMode: Table endpoints, guarded by allowlist + primary key predicates</summary>
public sealed partial class SqlRequestHandler
{
    private enum TableWriteKind { Insert, Update, Delete }

    /// <summary>Fail-closed gate shared by all verbs, returns an error result when the config is unsafe</summary>
    private static IActionResult? GuardTableWriteConfig(EndpointDefinition endpoint, string endpointName)
    {
        var configError = TableWriteBuilder.ValidateConfig(endpoint);
        if (configError == null) return null;

        // Configuration problem, not a caller problem; log loudly and refuse
        Log.Error("Table write rejected for {Endpoint}: {Error}", endpointName, configError);
        return PortwayResults.BadRequest("This endpoint's write configuration is invalid; see server logs");
    }

    private async Task<IActionResult> ExecuteTableWriteAsync(
        DbConnection connection,
        string connectionString,
        EndpointDefinition endpoint,
        string endpointName,
        TableWriteKind kind,
        JsonElement? data,
        string? routeId)
    {
        var provider = _providerFactory.GetProvider(connectionString);
        var schema = SqlSchemaResolver.Resolve(endpoint.DatabaseSchema, provider, connection.Database);
        var table = schema.Length > 0 ? $"{schema}.{endpoint.DatabaseObjectName}" : endpoint.DatabaseObjectName!;
        var pkColumn = TableWriteBuilder.ResolvePrimaryKeyColumn(endpoint);

        // Payload maps through the allowlist only; unknown fields reject the whole request
        var columns = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (data is { } json)
        {
            var payload = new Dictionary<string, object?>();
            foreach (var property in json.EnumerateObject())
                payload[property.Name] = GetParameterValue(property.Value);

            if (!TableWriteBuilder.TryResolveColumns(endpoint, payload, out columns, out var columnError))
                return PortwayResults.BadRequest(columnError!);
        }

        // Primary key value comes from the route (DELETE) or the payload (PUT/PATCH)
        object? pkValue = routeId;
        if (pkValue == null && columns.TryGetValue(pkColumn, out var fromPayload))
            pkValue = fromPayload;

        switch (kind)
        {
            case TableWriteKind.Insert:
            {
                var insert = TableWriteBuilder.BuildInsert(provider, table, columns);
                await connection.ExecuteAsync(insert.Sql, insert.Parameters);

                // Return the created row when the payload named its own key
                object? created = null;
                if (pkValue != null)
                {
                    var select = TableWriteBuilder.BuildSelectByKey(provider, table, pkColumn, pkValue);
                    created = (await connection.QueryAsync(select.Sql, select.Parameters)).FirstOrDefault();
                }
                Log.Debug("Table INSERT on {Endpoint} succeeded", endpointName);
                return PortwayResults.Create($"/api/{endpointName}", "Record created successfully", created);
            }

            case TableWriteKind.Update:
            {
                if (pkValue == null)
                    return PortwayResults.BadRequest($"Updates require the '{endpoint.PrimaryKey}' column");

                columns.Remove(pkColumn);
                if (columns.Count == 0)
                    return PortwayResults.BadRequest("Request contains no updatable columns");

                var update = TableWriteBuilder.BuildUpdate(provider, table, pkColumn, pkValue, columns);
                var affected = await connection.ExecuteAsync(update.Sql, update.Parameters);
                if (affected == 0)
                    return PortwayResults.NotFound("Record not found");

                var select = TableWriteBuilder.BuildSelectByKey(provider, table, pkColumn, pkValue);
                var updated = (await connection.QueryAsync(select.Sql, select.Parameters)).FirstOrDefault();
                Log.Debug("Table UPDATE on {Endpoint} affected {Rows} row(s)", endpointName, affected);
                return PortwayResults.Mutation("Record updated successfully", updated);
            }

            case TableWriteKind.Delete:
            {
                if (pkValue == null)
                    return PortwayResults.BadRequest("ID parameter is required for delete operations");

                var delete = TableWriteBuilder.BuildDelete(provider, table, pkColumn, pkValue);
                var affected = await connection.ExecuteAsync(delete.Sql, delete.Parameters);
                if (affected == 0)
                    return PortwayResults.NotFound("Record not found");

                Log.Debug("Table DELETE on {Endpoint} affected {Rows} row(s)", endpointName, affected);
                return PortwayResults.Mutation("Record deleted successfully", new { affected });
            }

            default:
                return PortwayResults.BadRequest("Unsupported table write operation");
        }
    }
}
