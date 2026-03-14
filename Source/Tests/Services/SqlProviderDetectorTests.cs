using PortwayApi.Classes.Providers;
using Xunit;

namespace PortwayApi.Tests.Services;

public class SqlProviderDetectorTests
{
    [Theory]
    [InlineData("Server=localhost;Database=MyDb;User Id=sa;Password=pass;", SqlProviderType.SqlServer)]
    [InlineData("Server=.;Database=AdventureWorks;Trusted_Connection=True;", SqlProviderType.SqlServer)]
    [InlineData("Data Source=.;Initial Catalog=NorthWind;Integrated Security=True;", SqlProviderType.SqlServer)]
    [InlineData("Server=myserver;Database=mydb;TrustServerCertificate=True;Encrypt=True;User Id=sa;Password=pass;", SqlProviderType.SqlServer)]
    [InlineData("Server=myserver;Database=mydb;Uid=sa;Password=pass;TrustServerCertificate=True;", SqlProviderType.SqlServer)]
    [InlineData("Server=myserver;Database=mydb;MultipleActiveResultSets=True;Trusted_Connection=True;", SqlProviderType.SqlServer)]
    [InlineData("Server=myserver;Database=mydb;ApplicationIntent=ReadOnly;Integrated Security=True;", SqlProviderType.SqlServer)]
    [InlineData("", SqlProviderType.SqlServer)]
    [InlineData(null, SqlProviderType.SqlServer)]
    public void Detect_SqlServerConnectionStrings_ReturnsSqlServer(string? connectionString, SqlProviderType expected)
    {
        var result = SqlProviderDetector.Detect(connectionString!);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("postgres://user:pass@localhost:5432/mydb", SqlProviderType.PostgreSql)]
    [InlineData("postgresql://user:pass@localhost/mydb", SqlProviderType.PostgreSql)]
    [InlineData("Host=localhost;Database=mydb;Username=user;Password=pass;", SqlProviderType.PostgreSql)]
    [InlineData("Host=127.0.0.1;Port=5432;Database=test;Username=postgres;", SqlProviderType.PostgreSql)]
    public void Detect_PostgreSqlConnectionStrings_ReturnsPostgreSql(string connectionString, SqlProviderType expected)
    {
        var result = SqlProviderDetector.Detect(connectionString);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("mysql://user:pass@localhost/mydb", SqlProviderType.MySql)]
    // Uid= alone is ambiguous (SQL Server also accepts it); use AllowUserVariables= for reliable MySQL detection
    [InlineData("Server=localhost;Database=test;Uid=admin;Password=secret;AllowUserVariables=true;", SqlProviderType.MySql)]
    [InlineData("Server=localhost;Database=test;User=admin;Password=secret;AllowPublicKeyRetrieval=true;", SqlProviderType.MySql)]
    public void Detect_MySqlConnectionStrings_ReturnsMySql(string connectionString, SqlProviderType expected)
    {
        var result = SqlProviderDetector.Detect(connectionString);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Data Source=mydb.db;", SqlProviderType.Sqlite)]
    [InlineData("Data Source=app.sqlite;", SqlProviderType.Sqlite)]
    [InlineData("Data Source=store.sqlite3;", SqlProviderType.Sqlite)]
    [InlineData("Data Source=:memory:;", SqlProviderType.Sqlite)]
    [InlineData("Mode=Memory;Cache=Shared;", SqlProviderType.Sqlite)]
    public void Detect_SqliteConnectionStrings_ReturnsSqlite(string connectionString, SqlProviderType expected)
    {
        var result = SqlProviderDetector.Detect(connectionString);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Detect_SqlServerWithDataSource_NotSqlite()
    {
        // SQL Server named instance uses "Data Source=" but with server path, not a .db file
        var cs = "Data Source=SERVER\\INSTANCE;Initial Catalog=MyDb;Trusted_Connection=True;";
        var result = SqlProviderDetector.Detect(cs);
        Assert.Equal(SqlProviderType.SqlServer, result);
    }
}
