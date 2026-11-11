namespace PortwayApi.Tests.Parity;

using System.Net;
using PortwayApi.Services.Providers;
using Testcontainers.MariaDb;

public sealed class MariaDbParityFixture : ParityDatabaseFixture
{
    private readonly MariaDbContainer _container = new MariaDbBuilder("mariadb:11.4").WithDatabase("test").Build();

    public override SqlProviderType ProviderType => SqlProviderType.MySql;
    public override string QualifiedProductsTable => "test.Products";
    public override string QualifiedWebhookTable => "test.WebhookData";
    public override string QualifiedCategoriesTable => "test.Categories";
    public override string QualifiedGuidKeysTable => "test.GuidKeys";
    public override string QualifiedTextKeysTable => "test.TextKeys";
    public override string GuidKeyProcedureName => "FindGuidKey";
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

    protected override IEnumerable<string> SchemaStatements => MySqlParityFixture.Schema;
}

public sealed class MariaDbProviderParityTests(MariaDbParityFixture fixture)
    : ProviderParityTests<MariaDbParityFixture>(fixture);

public sealed class MariaDbProviderHttpParityTests(MariaDbParityFixture fixture)
    : ProviderHttpParityTests<MariaDbParityFixture>(fixture)
{
    // char(36) accepts any short text
    protected override HttpStatusCode NonGuidKeyOnGuidColumnStatus => HttpStatusCode.NotFound;

    // To-do: F2 decides whether a non-numeric key on a numeric column answers 400 on MariaDB
    protected override bool KeyedWriteMismatchIsAnError => false;
}
