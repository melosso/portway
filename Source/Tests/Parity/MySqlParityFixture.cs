namespace PortwayApi.Tests.Parity;

using PortwayApi.Services.Providers;
using Testcontainers.MySql;

public sealed class MySqlParityFixture : ParityDatabaseFixture
{
    private readonly MySqlContainer _container = new MySqlBuilder("mysql:8.0").Build();

    public override SqlProviderType ProviderType => SqlProviderType.MySql;
    // MySQL schemas are databases; the container's default database plays that role
    public override string QualifiedProductsTable => "test.Products";
    public override string ProcedureSchema => "test";
    public override string ProcedureName => "GetProductsByPrice";
    public override string WriteProcedureName => "ManageProduct";
    public override string DeleteProductByIdSql => "DELETE FROM test.Products WHERE Id = @Id";

    protected override ISqlProvider CreateProvider() => new MySqlProvider();

    protected override async Task<string> StartContainerAsync()
    {
        await _container.StartAsync();
        return _container.GetConnectionString();
    }

    protected override Task StopContainerAsync() => _container.DisposeAsync().AsTask();

    protected override IEnumerable<string> SchemaStatements =>
    [
        "CREATE TABLE test.Products (Id INT PRIMARY KEY, Name VARCHAR(100) NOT NULL, Price DECIMAL(10,2) NOT NULL, ReleasedAt DATE NULL)",
        $"INSERT INTO test.Products (Id, Name, Price, ReleasedAt) VALUES {SeedValues}",
        "CREATE PROCEDURE test.GetProductsByPrice(IN MinPrice DECIMAL(10,2)) SELECT Id, Name, Price FROM test.Products WHERE Price >= MinPrice",
        """
        CREATE PROCEDURE test.ManageProduct(IN Method VARCHAR(10), IN Id INT, IN Name VARCHAR(100), IN Price DECIMAL(10,2))
        BEGIN
            INSERT INTO test.Products (Id, Name, Price) VALUES (Id, Name, Price);
            SELECT p.Id, p.Name, p.Price FROM test.Products p WHERE p.Id = Id;
        END
        """,
    ];
}
