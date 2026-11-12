namespace PortwayApi.Helpers;

using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

/// <summary>
/// Provider-neutral classification of database exceptions for masking and user-error passthrough
/// </summary>
public static class SqlErrorClassifier
{
    /// <summary>
    /// True when the database raised a deliberate user-facing error: RAISERROR 50000, RAISE EXCEPTION P0001 or SIGNAL SQLSTATE 45000
    /// </summary>
    public static bool IsIntentionalUserError(DbException ex) => ex switch
    {
        SqlException sql => sql.Number == 50000,
        PostgresException pg => pg.SqlState == "P0001",
        MySqlException my => my.SqlState == "45000",
        _ => false
    };

    /// <summary>
    /// True when the database cannot convert a client value to the column type. Applies to record keys and table write values only: these codes also cover other query errors
    /// </summary>
    public static bool IsTypeMismatch(DbException ex) => ex switch
    {
        SqlException sql => sql.Number is 206 or 245 or 8114 or 8169,
        PostgresException pg => pg.SqlState is "42883" or "22P02" or "42804",
        MySqlException my => my.Number is 1292 or 1366,
        SqliteException sqlite => sqlite.SqliteErrorCode == 20,
        _ => false
    };

    /// <summary>
    /// True when a write violates a primary key or unique constraint
    /// </summary>
    public static bool IsDuplicateKey(DbException ex) => ex switch
    {
        SqlException sql => sql.Number is 2601 or 2627,
        PostgresException pg => pg.SqlState == "23505",
        MySqlException my => my.Number == 1062,
        SqliteException sqlite => sqlite.SqliteExtendedErrorCode is 1555 or 2067,
        _ => false
    };

    /// <summary>
    /// Message safe to return for an intentional user error, without provider prefixes
    /// </summary>
    public static string GetUserMessage(DbException ex) => ex switch
    {
        PostgresException pg => pg.MessageText,
        _ => ex.Message
    };

    /// <summary>
    /// True when the server is unreachable at connect time: refused, unresolved or timed out
    /// </summary>
    public static bool IsConnectionFailure(DbException ex)
    {
        for (Exception? inner = ex; inner is not null; inner = inner.InnerException)
        {
            if (inner is System.Net.Sockets.SocketException or TimeoutException)
                return true;
        }
        return ex is MySqlException { ErrorCode: MySqlErrorCode.UnableToConnectToHost };
    }

    /// <summary>
    /// Compact provider detail for internal logs, never sent to clients
    /// </summary>
    public static string DescribeForLog(DbException ex) => ex switch
    {
        SqlException sql => $"SqlServer #{sql.Number} severity {sql.Class} state {sql.State}",
        PostgresException pg => $"PostgreSql SQLSTATE {pg.SqlState}",
        MySqlException my => $"MySql #{my.Number} SQLSTATE {my.SqlState}",
        _ => ex.GetType().Name
    };
}
