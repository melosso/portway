using Dapper;
using Microsoft.Data.Sqlite;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Services.Providers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

/// <summary>
/// Tenant predicates executed against SQLite
/// </summary>
public sealed class TenantSqlTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ODataToSqlConverter _converter = new([new SqliteProvider()]);
    private static readonly TenantPredicate[] Acme = [new("CompanyId", "ACME")];

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();
        await _connection.ExecuteAsync("""
            CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CompanyId TEXT, Branch INTEGER, Total INTEGER);
            INSERT INTO Orders VALUES (1, 'ACME', 7, 10), (2, 'ACME', 8, 20), (3, 'GLOBEX', 7, 30), (4, 'GLOBEX', 9, 40);
            """);
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private async Task<List<long>> Ids(Dictionary<string, string> odata, IReadOnlyList<TenantPredicate> tenants)
    {
        var (sql, parameters) = _converter.ConvertToSQL("Orders", odata, SqlProviderType.Sqlite, null, tenants);
        return (await _connection.QueryAsync<long>(sql.Replace("SELECT *", "SELECT Id"), parameters)).Order().ToList();
    }

    [Fact]
    public async Task ListIsConfined() =>
        Assert.Equal([1L, 2L], await Ids(new() { ["select"] = "Id" }, Acme));

    [Theory]
    [InlineData("CompanyId eq 'GLOBEX' or Id gt 0")]
    [InlineData("Id gt 0 or CompanyId eq 'GLOBEX'")]
    [InlineData("(CompanyId eq 'GLOBEX') or (Total gt 0)")]
    [InlineData("not (CompanyId eq 'ACME')")]
    public async Task FilterCannotWiden(string filter)
    {
        var ids = await Ids(new() { ["select"] = "Id", ["filter"] = filter }, Acme);

        Assert.DoesNotContain(3L, ids);
        Assert.DoesNotContain(4L, ids);
    }

    [Fact]
    public async Task CountIsConfined()
    {
        var (sql, parameters) = _converter.ConvertToCountSQL("Orders", new() { ["filter"] = "Total gt 0 or Id gt 0" }, SqlProviderType.Sqlite, Acme);

        Assert.Equal(2L, await _connection.ExecuteScalarAsync<long>(sql, parameters));
    }

    [Fact]
    public async Task IntegerColumnMatchesLiteral() =>
        Assert.Equal([1L, 3L], await Ids(new() { ["select"] = "Id" }, [new("Branch", "7")]));

    [Fact]
    public async Task TwoPredicatesBothApply() =>
        Assert.Equal([3L], await Ids(new() { ["select"] = "Id" }, [new("CompanyId", "GLOBEX"), new("Branch", "7")]));

    [Fact]
    public void LiteralRefusesQuote() =>
        Assert.Throws<ArgumentException>(() => TenantSql.Literal("A' OR '1'='1"));

    [Fact]
    public void PredicatesFollowTenancyMap()
    {
        var endpoint = new EndpointDefinition
        {
            Type = EndpointType.SQL,
            Tenancy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Company-Id"] = "CompanyId" },
        };

        var predicate = Assert.Single(TenantSql.Predicates(endpoint, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["x-company-id"] = "ACME" }));
        Assert.Equal(new TenantPredicate("CompanyId", "ACME"), predicate);
    }
}
