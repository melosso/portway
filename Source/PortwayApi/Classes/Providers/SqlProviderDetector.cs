namespace PortwayApi.Classes.Providers;

/// <summary>
/// Detects the SQL provider type from a connection string without allocating
/// any intermediate strings. All comparisons use OrdinalIgnoreCase.
///
/// Detection priority (first match wins):
///   1. SQL Server positive keywords, unambiguous, checked before everything else
///   2. SQL Server OLE DB provider names  (Provider=SQLOLEDB / MSOLEDBSQL / SQLNCLI*)
///   3. SQL Server ODBC driver names      (Driver={SQL Server} / {SQL Native Client})
///   4. PostgreSQL URI prefix             (postgres:// / postgresql://)
///   5. MySQL URI prefix                  (mysql://)
///   6. Npgsql key-value                  (Host= without Server= / Data Source=)
///   7. MySQL-unambiguous keywords        (AllowUserVariables= / SslMode=)
///   8. SQLite Data Source value          (*.db / *.sqlite / :memory:)
///   9. Default → SQL Server
/// </summary>
public static class SqlProviderDetector
{
    /// <summary>
    /// Keywords that appear exclusively in SQL Server connection strings.
    /// Any single hit is conclusive, SQL Server doesn't share these with other providers.
    /// Source: https://www.connectionstrings.com/sql-server/
    /// </summary>
    private static readonly string[] SqlServerKeywords =
    [
        // SqlClient encryption / auth
        "trustservercertificate=",
        "integrated security=",
        "trusted_connection=",
        "encrypt=",                      // SqlClient only; MySQL uses SslMode=, Npgsql uses SSL*=

        // MARS
        "multipleactiveresultsets=",
        "mars connection=",              // OLE DB / ODBC variant
        "mars_connection=",              // ODBC underscore variant

        // High availability / routing
        "multisubnetfailover=",
        "applicationintent=",
        "failover partner=",
        "failover_partner=",             // ODBC variant (underscore separator)

        // Catalog / schema
        "initial catalog=",

        // File attach
        "attachdbfilename=",
        "|datadirectory|",               // SQL Server DataDirectory token in AttachDbFilename

        // Connection tuning
        "workstation id=",
        "workstationid=",
        "packet size=",
        "asynchronous processing=",
        "network library=",
        "network address=",

        // Always Encrypted
        "column encryption setting=",
        "enclave attestation url=",

        // SQL Server Express / LocalDB
        "user instance=",

        // CLR context connection (SQL Server in-proc only)
        "context connection=",

        // OLE DB specifics
        "datatypecompatibility=",
        "ole db services=",
    ];

    public static SqlProviderType Detect(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return SqlProviderType.SqlServer;

        // 1. SQL Server positive keywords
        foreach (var keyword in SqlServerKeywords)
        {
            if (connectionString.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return SqlProviderType.SqlServer;
        }

        // 2. SQL Server OLE DB providers
        // Matches SQLOLEDB, MSOLEDBSQL, SQLNCLI / SQLNCLI10 / SQLNCLI11, SQLXMLOLEDB
        if (connectionString.Contains("provider=sqloledb",   StringComparison.OrdinalIgnoreCase)
         || connectionString.Contains("provider=msoledbsql", StringComparison.OrdinalIgnoreCase)
         || connectionString.Contains("provider=sqlncli",    StringComparison.OrdinalIgnoreCase)
         || connectionString.Contains("provider=sqlxmloledb",StringComparison.OrdinalIgnoreCase))
            return SqlProviderType.SqlServer;

        // 3. SQL Server ODBC drivers
        // "Driver={SQL Server}", "Driver={SQL Native Client …}", "Driver={SQL Server Native Client …}"
        // "Driver={ODBC Driver 17/13/11 for SQL Server}", needs "sql server" in driver name
        if (connectionString.Contains("driver={sql", StringComparison.OrdinalIgnoreCase))
            return SqlProviderType.SqlServer;

        if (connectionString.Contains("driver={odbc driver", StringComparison.OrdinalIgnoreCase)
         && connectionString.Contains("sql server",          StringComparison.OrdinalIgnoreCase))
            return SqlProviderType.SqlServer;

        // 4. PostgreSQL URI
        if (connectionString.StartsWith("postgres://",   StringComparison.OrdinalIgnoreCase)
         || connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return SqlProviderType.PostgreSql;

        // 5. MySQL URI
        if (connectionString.StartsWith("mysql://", StringComparison.OrdinalIgnoreCase))
            return SqlProviderType.MySql;

        // 6. Npgsql key-value: Host=
        // SQL Server uses Server= or Data Source=, never Host= for the server address.
        if (connectionString.Contains("Host=",        StringComparison.OrdinalIgnoreCase)
         && !connectionString.Contains("Server=",     StringComparison.OrdinalIgnoreCase)
         && !connectionString.Contains("Data Source=",StringComparison.OrdinalIgnoreCase))
            return SqlProviderType.PostgreSql;

        // 7. MySQL-unambiguous keywords
        // AllowUserVariables and AllowPublicKeyRetrieval are MySqlConnector-only.
        // SslMode= is used by MySqlConnector (not Npgsql, which uses SSL*= keywords).
        if (connectionString.Contains("AllowUserVariables=",    StringComparison.OrdinalIgnoreCase)
         || connectionString.Contains("AllowPublicKeyRetrieval=",StringComparison.OrdinalIgnoreCase)
         || connectionString.Contains("SslMode=",               StringComparison.OrdinalIgnoreCase))
            return SqlProviderType.MySql;

        // 8. SQLite
        // Parse "Data Source=<value>" without allocating; check file extension or :memory:.
        if (IsSqliteDataSource(connectionString.AsSpan())
         || connectionString.Contains("Mode=Memory", StringComparison.OrdinalIgnoreCase)
         || connectionString.Contains(":memory:",   StringComparison.OrdinalIgnoreCase))
            return SqlProviderType.Sqlite;

        // 9. Default
        // Ambiguous strings (e.g. Server=x;Database=y;User Id=z;Password=w) fall through
        // to SQL Server, the safest default since Portway originated as a SQL Server gateway.
        return SqlProviderType.SqlServer;
    }

    /// <summary>
    /// Span-based, zero-allocation parser that checks whether the "Data Source" value
    /// in a connection string points to an SQLite file or in-memory database.
    /// </summary>
    private static bool IsSqliteDataSource(ReadOnlySpan<char> connectionString)
    {
        var remaining = connectionString;

        while (!remaining.IsEmpty)
        {
            int semi = remaining.IndexOf(';');
            var segment = (semi >= 0 ? remaining[..semi] : remaining).Trim();

            if (segment.StartsWith("Data Source", StringComparison.OrdinalIgnoreCase)
             || segment.StartsWith("DataSource",  StringComparison.OrdinalIgnoreCase))
            {
                int eq = segment.IndexOf('=');
                if (eq >= 0)
                {
                    var value = segment[(eq + 1)..].Trim();
                    if (value.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
                     || value.EndsWith(".db",      StringComparison.OrdinalIgnoreCase)
                     || value.EndsWith(".sqlite",  StringComparison.OrdinalIgnoreCase)
                     || value.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            if (semi < 0) break;
            remaining = remaining[(semi + 1)..];
        }

        return false;
    }
}
