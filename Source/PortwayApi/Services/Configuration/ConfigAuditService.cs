using Microsoft.Data.Sqlite;
using Serilog;

namespace PortwayApi.Services.Configuration;

/// <summary>Persists an audit trail of configuration changes made through the Web UI</summary>
public class ConfigAuditService
{
    private readonly string _connectionString;
    private readonly object _initLock = new();
    private bool _initialized;

    public ConfigAuditService()
    {
        var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "auth.db");
        _connectionString = $"Data Source={dbPath}";
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS ConfigAudits (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    ClientIp TEXT,
                    Action TEXT NOT NULL,
                    TargetType TEXT NOT NULL,
                    Target TEXT NOT NULL,
                    Details TEXT,
                    BackupPath TEXT
                );
                CREATE INDEX IF NOT EXISTS IX_ConfigAudits_Timestamp ON ConfigAudits (Timestamp DESC);
                """;
            cmd.ExecuteNonQuery();
            _initialized = true;
        }
    }

    /// <summary>Records a configuration change; never throws so mutations are not disrupted</summary>
    public void Record(string action, string targetType, string target, string? clientIp = null, string? details = null, string? backupPath = null)
    {
        try
        {
            EnsureInitialized();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ConfigAudits (Timestamp, ClientIp, Action, TargetType, Target, Details, BackupPath)
                VALUES ($ts, $ip, $action, $type, $target, $details, $backup)
                """;
            cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$ip", (object?)clientIp ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$action", action);
            cmd.Parameters.AddWithValue("$type", targetType);
            cmd.Parameters.AddWithValue("$target", target);
            cmd.Parameters.AddWithValue("$details", (object?)details ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$backup", (object?)backupPath ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to record config audit for {Action} on {Target}", action, target);
        }
    }

    /// <summary>Returns the most recent audit entries, newest first</summary>
    public List<ConfigAuditEntry> GetRecent(int limit = 50)
    {
        var entries = new List<ConfigAuditEntry>();
        try
        {
            EnsureInitialized();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Timestamp, ClientIp, Action, TargetType, Target, Details, BackupPath FROM ConfigAudits ORDER BY Id DESC LIMIT $limit";
            cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                entries.Add(new ConfigAuditEntry(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to read config audit entries");
        }
        return entries;
    }

    /// <summary>Returns a single audit entry by id, or null</summary>
    public ConfigAuditEntry? GetById(long id)
    {
        try
        {
            EnsureInitialized();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Timestamp, ClientIp, Action, TargetType, Target, Details, BackupPath FROM ConfigAudits WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new ConfigAuditEntry(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to read config audit entry {Id}", id);
        }
        return null;
    }
}
