using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using PortwayApi.Classes;
using PortwayApi.Services;
using PortwayApi.Services.Providers;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// Column metadata follows AllowedColumns after an endpoint file changes
/// </summary>
[Collection("Integration")]
public sealed class SqlMetadataRefreshTests : IDisposable
{
    private static readonly string Dir = Path.Combine("endpoints", "SQL", "MetaRefresh", "Items");
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"portway_meta_{Guid.NewGuid():N}.db");

    public SqlMetadataRefreshTests()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Execute("CREATE TABLE Items (Id INTEGER PRIMARY KEY, Name TEXT, Secret TEXT)");
    }

    public void Dispose()
    {
        Directory.Delete(Path.GetDirectoryName(Dir)!, recursive: true);
        EndpointHandler.ReloadAllEndpoints();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private static void WriteEndpoint(params string[] columns)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, "entity.json"), JsonSerializer.Serialize(new
        {
            DatabaseObjectName = "Items",
            DatabaseObjectType = "Table",
            AllowedEnvironments = new[] { "prod" },
            AllowedColumns = columns,
        }));
        EndpointHandler.ReloadEndpointType(EndpointType.SQL);
    }

    [Fact]
    public async Task RefreshAfterNarrowingColumns_DropsRemovedColumn()
    {
        WriteEndpoint("Id", "Name", "Secret");
        var factory = new SqlProviderFactory([new SqliteProvider()]);
        var service = new SqlMetadataService(new SqlConnectionPoolService(new SqlPoolingOptions(0, 10, 15, true, "test"), factory), factory);
        var key = EndpointHandler.GetSqlEndpoints().Keys.Single(k => k.EndsWith("Items") && k.Contains("MetaRefresh"));
        await service.InitializeAsync(
            new() { [key] = EndpointHandler.GetSqlEndpoints()[key] },
            new EnvironmentSettings(),
            _ => Task.FromResult($"Data Source={_dbPath}"),
            TestContext.Current.CancellationToken);
        Assert.Contains(service.GetObjectMetadata(key)!, c => c.ColumnName == "Secret");

        WriteEndpoint("Id", "Name");
        await service.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(service.GetObjectMetadata(key)!, c => c.ColumnName == "Secret");
    }
}
