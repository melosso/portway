namespace PortwayApi.Tests.Parity;

using Dapper;
using PortwayApi.Services.Providers;
using Xunit;

/// <summary>Starts one database container, creates the shared parity schema and seeds five products</summary>
public abstract class ParityDatabaseFixture : IAsyncLifetime
{
    public abstract SqlProviderType ProviderType { get; }

    /// <summary>Schema-qualified entity name as a client would address it in OData</summary>
    public abstract string QualifiedProductsTable { get; }

    /// <summary>Schema passed to GetProcedureParametersAsync</summary>
    public abstract string ProcedureSchema { get; }
    public abstract string ProcedureName { get; }

    /// <summary>TVF used for column discovery, empty when the provider has no TVF support</summary>
    public virtual string TvfName => string.Empty;

    /// <summary>Column count the TVF exposes, differs per fixture shape</summary>
    public virtual int TvfColumnCount => 0;

    /// <summary>Rowset-returning write routine following the Portway procedure contract</summary>
    public abstract string WriteProcedureName { get; }

    /// <summary>Dialect-quoted cleanup so write scenarios leave the seed data untouched</summary>
    public abstract string DeleteProductByIdSql { get; }

    public string ConnectionString { get; private set; } = string.Empty;
    public ISqlProvider Provider { get; private set; } = null!;

    protected abstract Task<string> StartContainerAsync();
    protected abstract Task StopContainerAsync();
    protected abstract ISqlProvider CreateProvider();
    protected abstract IEnumerable<string> SchemaStatements { get; }

    public async Task InitializeAsync()
    {
        if (!DockerProbe.IsAvailable)
            return; // Tests carry [DockerFact] and skip; the fixture must not fail the collection

        Provider = CreateProvider();
        ConnectionString = await StartContainerAsync();

        await using var connection = Provider.CreateConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var statement in SchemaStatements)
            await connection.ExecuteAsync(statement);
    }

    public async Task DisposeAsync()
    {
        if (DockerProbe.IsAvailable)
            await StopContainerAsync();
    }

    /// <summary>Values shared by every dialect, fixtures wrap them in their own identifier quoting</summary>
    protected const string SeedValues = """
        (1, 'Anvil', 25.00, '2023-03-01'),
        (2, 'Rocket Skates', 79.99, '2024-01-15'),
        (3, 'Bird Seed', 5.25, '2023-11-30'),
        (4, 'Giant Magnet', 149.50, '2024-06-01'),
        (5, 'Tornado Kit', 33.10, '2024-09-09')
        """;
}
