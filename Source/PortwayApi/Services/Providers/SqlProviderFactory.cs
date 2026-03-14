using PortwayApi.Classes.Providers;

namespace PortwayApi.Services.Providers;

public interface ISqlProviderFactory
{
    ISqlProvider GetProvider(string connectionString);
    ISqlProvider GetProvider(SqlProviderType type);
}

public class SqlProviderFactory : ISqlProviderFactory
{
    private readonly IReadOnlyDictionary<SqlProviderType, ISqlProvider> _providers;

    public SqlProviderFactory(IEnumerable<ISqlProvider> providers)
        => _providers = providers.ToDictionary(p => p.ProviderType);

    public ISqlProvider GetProvider(string connectionString)
        => _providers[SqlProviderDetector.Detect(connectionString)];

    public ISqlProvider GetProvider(SqlProviderType type)
        => _providers[type];
}
