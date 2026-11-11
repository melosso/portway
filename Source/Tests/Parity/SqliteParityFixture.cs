namespace PortwayApi.Tests.Parity;

using PortwayApi.Services.Providers;

public sealed class SqliteParityFixture : ParityDatabaseFixture
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"portway_parity_{Guid.NewGuid():N}.db");

    public override SqlProviderType ProviderType => SqlProviderType.Sqlite;
    public override string QualifiedProductsTable => "main.Products";
    public override string QualifiedWebhookTable => "main.WebhookData";
    public override string QualifiedCategoriesTable => "main.Categories";
    public override string QualifiedGuidKeysTable => "main.GuidKeys";
    public override string QualifiedTextKeysTable => "main.TextKeys";
    public override string GuidKeyProcedureName => string.Empty;
    public override string ProcedureSchema => string.Empty;
    public override string ProcedureName => string.Empty;
    public override string WriteProcedureName => string.Empty;
    public override string DeleteProductByIdSql => "DELETE FROM Products WHERE Id = @Id";

    protected override ISqlProvider CreateProvider() => new SqliteProvider();

    protected override Task<string> StartContainerAsync() => Task.FromResult($"Data Source={_path}");

    protected override Task StopContainerAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_path);
        return Task.CompletedTask;
    }

    protected override IEnumerable<string> SchemaStatements =>
    [
        "CREATE TABLE WebhookData (Id INTEGER PRIMARY KEY AUTOINCREMENT, WebhookId TEXT NOT NULL, Payload TEXT NOT NULL, ReceivedAt TEXT NOT NULL, DeliveryId TEXT NULL, ProcessedAt TEXT NULL)",
        "CREATE UNIQUE INDEX UX_WebhookData_Delivery ON WebhookData (WebhookId, DeliveryId)",
        "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Price NUMERIC NOT NULL, ReleasedAt TEXT NULL, CategoryId INTEGER NULL)",
        $"INSERT INTO Products (Id, Name, Price, ReleasedAt) VALUES {SeedValues}",
        "CREATE TABLE Categories (CategoryId INTEGER PRIMARY KEY, CategoryName TEXT NOT NULL)",
        $"INSERT INTO Categories (CategoryId, CategoryName) VALUES {CategorySeedValues}",
        "UPDATE Products SET CategoryId = 10 WHERE Id IN (1, 2)",
        "UPDATE Products SET CategoryId = 20 WHERE Id = 4",
        "CREATE TABLE GuidKeys (Id TEXT PRIMARY KEY, Name TEXT NOT NULL)",
        $"INSERT INTO GuidKeys (Id, Name) VALUES {GuidKeySeedValues}",
        "CREATE TABLE TextKeys (Id TEXT PRIMARY KEY, Name TEXT NOT NULL)",
        $"INSERT INTO TextKeys (Id, Name) VALUES {TextKeySeedValues}"
    ];
}

public sealed class SqliteProviderParityTests(SqliteParityFixture fixture)
    : ProviderParityTests<SqliteParityFixture>(fixture);

public sealed class SqliteProviderHttpParityTests(SqliteParityFixture fixture)
    : ProviderHttpParityTests<SqliteParityFixture>(fixture)
{
    // text keys accept any value
    protected override System.Net.HttpStatusCode NonGuidKeyOnGuidColumnStatus => System.Net.HttpStatusCode.NotFound;
    protected override bool KeyMismatchIsAnError => false;
}
