namespace PortwayApi.Tests.Parity;

using PortwayApi.Services.Providers;
using Testcontainers.MsSql;

public sealed class MsSqlParityFixture : ParityDatabaseFixture
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();

    public override SqlProviderType ProviderType => SqlProviderType.SqlServer;
    public override string QualifiedProductsTable => "dbo.Products";
    public override string QualifiedWebhookTable => "dbo.WebhookData";
    public override string QualifiedCategoriesTable => "dbo.Categories";
    public override string QualifiedGuidKeysTable => "dbo.GuidKeys";
    public override string QualifiedTextKeysTable => "dbo.TextKeys";
    public override string GuidKeyProcedureName => "FindGuidKey";
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
        "CREATE TABLE dbo.WebhookData (Id INT IDENTITY(1,1) PRIMARY KEY, WebhookId NVARCHAR(100) NOT NULL, Payload NVARCHAR(MAX) NOT NULL, ReceivedAt DATETIME2 NOT NULL, DeliveryId NVARCHAR(255) NULL)",
        "CREATE UNIQUE INDEX UX_WebhookData_Delivery ON dbo.WebhookData (WebhookId, DeliveryId) WHERE DeliveryId IS NOT NULL",
        "CREATE TABLE dbo.Products (Id INT PRIMARY KEY, Name NVARCHAR(100) NOT NULL, Price DECIMAL(10,2) NOT NULL, ReleasedAt DATE NULL, CategoryId INT NULL)",
        $"INSERT INTO dbo.Products (Id, Name, Price, ReleasedAt) VALUES {SeedValues}",
        "CREATE TABLE dbo.Categories (CategoryId INT PRIMARY KEY, CategoryName NVARCHAR(100) NOT NULL)",
        $"INSERT INTO dbo.Categories (CategoryId, CategoryName) VALUES {CategorySeedValues}",
        "UPDATE dbo.Products SET CategoryId = 10 WHERE Id IN (1, 2)",
        "UPDATE dbo.Products SET CategoryId = 20 WHERE Id = 4",
        "CREATE TABLE dbo.GuidKeys (Id UNIQUEIDENTIFIER PRIMARY KEY, Name NVARCHAR(100) NOT NULL)",
        $"INSERT INTO dbo.GuidKeys (Id, Name) VALUES {GuidKeySeedValues}",
        "CREATE TABLE dbo.TextKeys (Id NVARCHAR(50) PRIMARY KEY, Name NVARCHAR(100) NOT NULL)",
        $"INSERT INTO dbo.TextKeys (Id, Name) VALUES {TextKeySeedValues}",
        "CREATE PROCEDURE dbo.FindGuidKey @Method NVARCHAR(10), @Id UNIQUEIDENTIFIER, @Name NVARCHAR(100) = NULL AS SELECT Name FROM dbo.GuidKeys WHERE Id = @Id",
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
