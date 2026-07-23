using Microsoft.Data.Sqlite;
using PortwayApi.Services.Providers;
using Xunit;

namespace PortwayApi.Tests.Providers;

public class SqlProviderTests
{
    private static readonly List<ISqlProvider> Providers =
        [new MsSqlProvider(), new PostgreSqlProvider(), new MySqlProvider(), new SqliteProvider()];

    [Fact]
    public void Factory_RoutesEveryProviderType()
    {
        var factory = new SqlProviderFactory(Providers);

        foreach (var type in Enum.GetValues<SqlProviderType>())
            Assert.Equal(type, factory.GetProvider(type).ProviderType);
    }

    [Theory]
    [InlineData("Server=.;Database=x;Trusted_Connection=True", SqlProviderType.SqlServer)]
    [InlineData("Host=localhost;Database=x;Username=u;Password=p", SqlProviderType.PostgreSql)]
    [InlineData("Server=localhost;Database=x;Uid=u;Pwd=p;SslMode=none", SqlProviderType.MySql)]
    [InlineData("Data Source=data/demo.db;", SqlProviderType.Sqlite)]
    public void Factory_DetectsProviderFromConnectionString(string connectionString, SqlProviderType expected)
    {
        var factory = new SqlProviderFactory(Providers);

        Assert.Equal(expected, factory.GetProvider(connectionString).ProviderType);
    }

    [Fact]
    public void Providers_ReportExpectedCapabilities()
    {
        var byType = Providers.ToDictionary(p => p.ProviderType);

        Assert.True(byType[SqlProviderType.SqlServer].SupportsTvf);
        Assert.True(byType[SqlProviderType.PostgreSql].SupportsTvf);
        Assert.False(byType[SqlProviderType.MySql].SupportsTvf);
        Assert.False(byType[SqlProviderType.Sqlite].SupportsTvf);

        Assert.False(byType[SqlProviderType.Sqlite].SupportsProcedures);
        Assert.False(byType[SqlProviderType.Sqlite].SupportsSchemas);
    }

    [Theory]
    [InlineData(SqlProviderType.SqlServer, "nvarchar", "System.String")]
    [InlineData(SqlProviderType.SqlServer, "uniqueidentifier", "System.Guid")]
    [InlineData(SqlProviderType.SqlServer, "rowversion", "System.Byte[]")]
    [InlineData(SqlProviderType.SqlServer, "timestamp", "System.Byte[]")]
    [InlineData(SqlProviderType.PostgreSql, "jsonb", "System.String")]
    [InlineData(SqlProviderType.PostgreSql, "timestamptz", "System.DateTimeOffset")]
    [InlineData(SqlProviderType.PostgreSql, "timestamp", "System.DateTime")]
    [InlineData(SqlProviderType.PostgreSql, "inet", "System.String")]
    [InlineData(SqlProviderType.PostgreSql, "smallserial", "System.Int16")]
    [InlineData(SqlProviderType.MySql, "longtext", "System.String")]
    [InlineData(SqlProviderType.MySql, "tinyint(1)", "System.Boolean")]
    [InlineData(SqlProviderType.MySql, "year", "System.Int32")]
    [InlineData(SqlProviderType.MySql, "bit", "System.UInt64")]
    [InlineData(SqlProviderType.MySql, "int unsigned", "System.UInt32")]
    [InlineData(SqlProviderType.MySql, "tinytext", "System.String")]
    [InlineData(SqlProviderType.MySql, "geometry", "System.Byte[]")]
    [InlineData(SqlProviderType.MySql, "timestamp", "System.DateTime")]
    [InlineData(SqlProviderType.Sqlite, "INTEGER", "System.Int64")]
    [InlineData(SqlProviderType.Sqlite, "INT", "System.Int32")]
    [InlineData(SqlProviderType.Sqlite, "GUID", "System.Guid")]
    [InlineData(SqlProviderType.Sqlite, "NVARCHAR", "System.String")]
    public void MapSqlTypeToClr_MapsDialectTypes(SqlProviderType type, string nativeType, string expectedClr)
    {
        var provider = Providers.Single(p => p.ProviderType == type);

        Assert.Equal(expectedClr, provider.MapSqlTypeToClr(nativeType));
    }

    [Fact]
    public void Providers_UseDistinctCompilers()
    {
        var compilerTypes = Providers.Select(p => p.GetCompiler().GetType()).ToList();

        Assert.Equal(compilerTypes.Count, compilerTypes.Distinct().Count());
    }

    [Fact]
    public async Task UnsupportedMetadata_ReturnsEmptyInsteadOfThrowing()
    {
        // Sqlite inherits both base fallbacks, no connection is opened on that path
        var sqlite = new SqliteProvider();
        await using var conn = new SqliteConnection("Data Source=:memory:");

        var columns = await sqlite.GetTvfColumnsAsync(conn, "main", "fn", CancellationToken.None);
        var parameters = await sqlite.GetProcedureParametersAsync(conn, "main", "proc", CancellationToken.None);

        Assert.Empty(columns);
        Assert.Empty(parameters);
    }
}
