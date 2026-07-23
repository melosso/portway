namespace PortwayApi.Helpers;

using System.Data.Common;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

/// <summary>Provider-neutral classification of database exceptions for masking and user-error passthrough</summary>
public static class SqlErrorClassifier
{
    /// <summary>True when the database raised a deliberate user-facing error: RAISERROR 50000, RAISE EXCEPTION P0001 or SIGNAL SQLSTATE 45000</summary>
    public static bool IsIntentionalUserError(DbException ex) => ex switch
    {
        SqlException sql => sql.Number == 50000,
        PostgresException pg => pg.SqlState == "P0001",
        MySqlException my => my.SqlState == "45000",
        _ => false
    };

    /// <summary>Message safe to return for an intentional user error, without provider prefixes</summary>
    public static string GetUserMessage(DbException ex) => ex switch
    {
        PostgresException pg => pg.MessageText,
        _ => ex.Message
    };

    /// <summary>Compact provider detail for internal logs, never sent to clients</summary>
    public static string DescribeForLog(DbException ex) => ex switch
    {
        SqlException sql => $"SqlServer #{sql.Number} severity {sql.Class} state {sql.State}",
        PostgresException pg => $"PostgreSql SQLSTATE {pg.SqlState}",
        MySqlException my => $"MySql #{my.Number} SQLSTATE {my.SqlState}",
        _ => ex.GetType().Name
    };
}
