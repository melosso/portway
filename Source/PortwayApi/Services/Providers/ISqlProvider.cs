using PortwayApi.Services.Database;
using System.Data;
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

    /// <summary>Schema assumed when an endpoint omits DatabaseSchema, empty means use the connection's database</summary>
    string DefaultSchema { get; }

    string MapSqlTypeToClr(string nativeType);

    /// <summary>Builds the dialect-correct invocation for a rowset-returning procedure or function</summary>
    (string CommandText, CommandType CommandType) BuildProcedureInvocation(string schema, string procedureName, IReadOnlyCollection<string> parameterNames);

    string HealthCheckQuery { get; }

    Task<List<Services.Database.ColumnMetadata>> GetTvfColumnsAsync(
        DbConnection connection, string schema, string functionName, CancellationToken cancellationToken);

    Task<List<Services.Database.ParameterMetadata>> GetProcedureParametersAsync(
        DbConnection connection, string schema, string procedureName, CancellationToken cancellationToken);
}
