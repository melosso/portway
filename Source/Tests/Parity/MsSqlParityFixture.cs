namespace PortwayApi.Tests.Parity;

using PortwayApi.Services.Providers;
using Testcontainers.MsSql;

public sealed class MsSqlParityFixture : ParityDatabaseFixture
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();

    public override SqlProviderType ProviderType => SqlProviderType.SqlServer;
    public override string QualifiedProductsTable => "dbo.Products";
    public override string ProcedureSchema => "dbo";
    public override string ProcedureName => "GetProductsByPrice";
    public override string TvfName => "ProductsAbove";
    public override int TvfColumnCount => 3;
    public override string WriteProcedureName => "ManageProduct";
    public override string DeleteProductByIdSql => "DELETE FROM dbo.Products WHERE Id = @Id";

    protected override ISqlProvider CreateProvider() => new MsSqlProvider();

    protected override async Task<string> StartContainerAsync()
    {
        await _container.StartAsync();
        return _container.GetConnectionString();
    }

    protected override Task StopContainerAsync() => _container.DisposeAsync().AsTask();

    protected override IEnumerable<string> SchemaStatements =>
    [
        "CREATE TABLE dbo.Products (Id INT PRIMARY KEY, Name NVARCHAR(100) NOT NULL, Price DECIMAL(10,2) NOT NULL, ReleasedAt DATE NULL)",
        $"INSERT INTO dbo.Products (Id, Name, Price, ReleasedAt) VALUES {SeedValues}",
        "CREATE PROCEDURE dbo.GetProductsByPrice @MinPrice DECIMAL(10,2) AS SELECT Id, Name, Price FROM dbo.Products WHERE Price >= @MinPrice",
        "CREATE FUNCTION dbo.ProductsAbove (@MinPrice DECIMAL(10,2)) RETURNS TABLE AS RETURN (SELECT Id, Name, Price FROM dbo.Products WHERE Price >= @MinPrice)",
        """
        CREATE PROCEDURE dbo.ManageProduct @Method NVARCHAR(10), @Id INT, @Name NVARCHAR(100), @Price DECIMAL(10,2) AS
        BEGIN
            INSERT INTO dbo.Products (Id, Name, Price) VALUES (@Id, @Name, @Price);
            SELECT Id, Name, Price FROM dbo.Products WHERE Id = @Id;
        END
        """,
    ];
}
