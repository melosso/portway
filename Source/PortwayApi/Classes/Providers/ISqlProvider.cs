using System.Data.Common;
using SqlKata.Compilers;

namespace PortwayApi.Classes.Providers;

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

    Task<List<Classes.ColumnMetadata>> GetTvfColumnsAsync(
        DbConnection connection, string schema, string functionName, CancellationToken cancellationToken);

    Task<List<Classes.ParameterMetadata>> GetProcedureParametersAsync(
        DbConnection connection, string schema, string procedureName, CancellationToken cancellationToken);
}
