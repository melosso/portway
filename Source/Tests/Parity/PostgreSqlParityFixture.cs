namespace PortwayApi.Tests.Parity;

using PortwayApi.Services.Providers;
using Testcontainers.PostgreSql;

public sealed class PostgreSqlParityFixture : ParityDatabaseFixture
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public override SqlProviderType ProviderType => SqlProviderType.PostgreSql;
    public override string QualifiedProductsTable => "public.Products";
    public override string QualifiedWebhookTable => "public.WebhookData";
    public override string QualifiedCategoriesTable => "public.Categories";
    public override string QualifiedGuidKeysTable => "public.GuidKeys";
    public override string QualifiedTextKeysTable => "public.TextKeys";
    public override string GuidKeyProcedureName => "find_guid_key";
    public override string ProcedureSchema => "public";
    public override string ProcedureName => "get_products_by_price";
    // Tests SETOF composite types. See below for RETURNS TABLE test.
    public override string TvfName => "get_products_by_price";
    public override int TvfColumnCount => 5;
    public override string WriteProcedureName => "manage_product";
    public override string DeleteProductByIdSql => """DELETE FROM public."Products" WHERE "Id" = @Id""";

    protected override ISqlProvider CreateProvider() => new PostgreSqlProvider();

    protected override async Task<string> StartContainerAsync()
    {
        await _container.StartAsync();
        return _container.GetConnectionString();
    }

    protected override Task StopContainerAsync() => _container.DisposeAsync().AsTask();

    // Quoted identifiers keep the casing the OData compiler emits
    protected override IEnumerable<string> SchemaStatements =>
    [
        """CREATE TABLE public."WebhookData" ("Id" SERIAL PRIMARY KEY, "WebhookId" VARCHAR(100) NOT NULL, "Payload" TEXT NOT NULL, "ReceivedAt" TIMESTAMPTZ NOT NULL, "DeliveryId" VARCHAR(255) NULL, "ProcessedAt" TIMESTAMPTZ NULL)""",
        """CREATE UNIQUE INDEX "UX_WebhookData_Delivery" ON public."WebhookData" ("WebhookId", "DeliveryId")""",
        """CREATE TABLE public."Products" ("Id" INT PRIMARY KEY, "Name" VARCHAR(100) NOT NULL, "Price" DECIMAL(10,2) NOT NULL, "ReleasedAt" DATE NULL, "CategoryId" INT NULL)""",
        $"""INSERT INTO public."Products" ("Id", "Name", "Price", "ReleasedAt") VALUES {SeedValues}""",
        """CREATE TABLE public."Categories" ("CategoryId" INT PRIMARY KEY, "CategoryName" VARCHAR(100) NOT NULL)""",
        $"""INSERT INTO public."Categories" ("CategoryId", "CategoryName") VALUES {CategorySeedValues}""",
        """UPDATE public."Products" SET "CategoryId" = 10 WHERE "Id" IN (1, 2)""",
        """UPDATE public."Products" SET "CategoryId" = 20 WHERE "Id" = 4""",
        """CREATE TABLE public."GuidKeys" ("Id" UUID PRIMARY KEY, "Name" VARCHAR(100) NOT NULL)""",
        $"""INSERT INTO public."GuidKeys" ("Id", "Name") VALUES {GuidKeySeedValues}""",
        """CREATE TABLE public."TextKeys" ("Id" VARCHAR(50) PRIMARY KEY, "Name" VARCHAR(100) NOT NULL)""",
        $"""INSERT INTO public."TextKeys" ("Id", "Name") VALUES {TextKeySeedValues}""",
        """
        CREATE FUNCTION public.find_guid_key(method TEXT, id UUID, name VARCHAR DEFAULT NULL) RETURNS TABLE("Name" VARCHAR) AS $$
            SELECT k."Name" FROM public."GuidKeys" k WHERE k."Id" = find_guid_key.id
        $$ LANGUAGE sql
        """,
        """
        CREATE FUNCTION public.get_products_by_price(min_price NUMERIC)
        RETURNS SETOF public."Products" AS $$
            SELECT * FROM public."Products" WHERE "Price" >= min_price
        $$ LANGUAGE sql
        """,
        """
        CREATE FUNCTION public.products_summary(min_price NUMERIC)
        RETURNS TABLE("Name" VARCHAR, "Price" NUMERIC) AS $$
            SELECT "Name", "Price" FROM public."Products" WHERE "Price" >= min_price
        $$ LANGUAGE sql
        """,
        """
        CREATE FUNCTION public.manage_product(method TEXT, id INT, name VARCHAR, price NUMERIC)
        RETURNS SETOF public."Products" AS $$
            INSERT INTO public."Products" ("Id", "Name", "Price") VALUES (id, name, price) RETURNING *;
        $$ LANGUAGE sql
        """,
    ];
}
