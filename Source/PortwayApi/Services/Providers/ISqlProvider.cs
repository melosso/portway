using PortwayApi.Services.Database;
using System.Data.Common;
using SqlKata.Compilers;

namespace PortwayApi.Services.Providers;

public interface ISqlProvider
{
    SqlProviderType ProviderType { get; }

    DbConnection CreateConnection(string connectionString);
    string OptimizeConnectionString(string connectionString, SqlPoolingOptions options);

    Compiler GetCompiler();

    bool SupportsTvf { get; }
    bool SupportsProcedures { get; }
    bool SupportsSchemas { get; }

    string MapSqlTypeToClr(string nativeType);

    string HealthCheckQuery { get; }

    Task<List<Services.Database.ColumnMetadata>> GetTvfColumnsAsync(
        DbConnection connection, string schema, string functionName, CancellationToken cancellationToken);

    Task<List<Services.Database.ParameterMetadata>> GetProcedureParametersAsync(
        DbConnection connection, string schema, string procedureName, CancellationToken cancellationToken);
}
