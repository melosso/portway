using System.Collections.Concurrent;
using PortwayApi.Classes;
using PortwayApi.Services.Providers;
using Xunit;

namespace PortwayApi.Tests.Concurrency;

public sealed class ODataConverterConcurrencyTests
{
    [Fact]
    public async Task ParallelConversions_MatchSequentialOnes()
    {
        var converter = new ODataToSqlConverter([new MsSqlProvider(), new PostgreSqlProvider(), new MySqlProvider(), new SqliteProvider()]);
        var providers = new[] { SqlProviderType.SqlServer, SqlProviderType.PostgreSql, SqlProviderType.MySql, SqlProviderType.Sqlite };
        var cases = Enumerable.Range(0, 40).Select(i => (
            Provider: providers[i % providers.Length],
            Options: new Dictionary<string, string>
            {
                ["filter"] = $"Price gt {i} and Name eq 'n{i}'",
                ["select"] = i % 2 == 0 ? "Id,Name" : "Id,Price",
                ["orderby"] = "Id",
                ["top"] = (i + 1).ToString()
            })).ToArray();

        string Convert(int i)
        {
            var (sql, parameters) = converter.ConvertToSQL("dbo.Items", cases[i].Options, cases[i].Provider);
            return sql + "|" + string.Join(",", parameters.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));
        }

        var expected = Enumerable.Range(0, cases.Length).Select(Convert).ToArray();
        var mismatches = new ConcurrentBag<int>();

        await Parallel.ForAsync(0, cases.Length * 50, new ParallelOptions { MaxDegreeOfParallelism = 16 }, (n, _) =>
        {
            var i = n % cases.Length;
            if (Convert(i) != expected[i])
                mismatches.Add(i);
            return ValueTask.CompletedTask;
        });

        Assert.Empty(mismatches);
    }
}
