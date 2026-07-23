namespace PortwayApi.Tests.Parity;

using PortwayApi.Services.Providers;
using Testcontainers.PostgreSql;

public sealed class PostgreSqlParityFixture : ParityDatabaseFixture
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public override SqlProviderType ProviderType => SqlProviderType.PostgreSql;
    public override string QualifiedProductsTable => "public.Products";
    public override string ProcedureSchema => "public";
    public override string ProcedureName => "get_products_by_price";
    // SETOF composite return, the RETURNS TABLE shape gets its own test below
    public override string TvfName => "get_products_by_price";
    public override int TvfColumnCount => 4;
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
        """CREATE TABLE public."Products" ("Id" INT PRIMARY KEY, "Name" VARCHAR(100) NOT NULL, "Price" DECIMAL(10,2) NOT NULL, "ReleasedAt" DATE NULL)""",
        $"""INSERT INTO public."Products" ("Id", "Name", "Price", "ReleasedAt") VALUES {SeedValues}""",
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
