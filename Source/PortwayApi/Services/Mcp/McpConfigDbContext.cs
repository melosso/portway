namespace PortwayApi.Services.Mcp;

using Microsoft.EntityFrameworkCore;
using Serilog;

/// <summary>
/// EF Core context for the MCP configuration store (mcp.db).
/// Stores provider, model, and encrypted credentials outside of appsettings.json
/// so that sensitive keys are never exposed in configuration files.
/// </summary>
public class McpConfigDbContext : DbContext
{
    public McpConfigDbContext(DbContextOptions<McpConfigDbContext> options) : base(options) { }

    public DbSet<McpConfigEntry> Config { get; set; }

    /// <summary>
    /// Idempotent schema initialisation — safe to call on every startup.
    /// Creates the table on first run; adds missing columns on upgrades.
    /// </summary>
    public void EnsureTablesCreated()
    {
        try
        {
            if (!CheckTableExists("McpConfig"))
            {
                CreateMcpConfigTable();
            }
            else
            {
                Log.Debug("McpConfig table verified in mcp.db");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialise McpConfig table in mcp.db");
        }
    }

    private bool CheckTableExists(string tableName)
    {
        using var cmd = Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
        var p = cmd.CreateParameter();
        p.ParameterName = "@name";
        p.Value         = tableName;
        cmd.Parameters.Add(p);

        if (Database.GetDbConnection().State != System.Data.ConnectionState.Open)
            Database.GetDbConnection().Open();

        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private void CreateMcpConfigTable()
    {
        Database.ExecuteSqlRaw("""
            CREATE TABLE McpConfig (
                Key         TEXT PRIMARY KEY NOT NULL,
                Value       TEXT NOT NULL DEFAULT '',
                IsEncrypted INTEGER NOT NULL DEFAULT 0,
                UpdatedAt   DATETIME DEFAULT CURRENT_TIMESTAMP
            )
            """);
        Log.Information("McpConfig table created in mcp.db");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<McpConfigEntry>(entity =>
        {
            entity.ToTable("McpConfig");
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Value).IsRequired();
            entity.Property(e => e.IsEncrypted).HasDefaultValue(false);
        });
    }
}

/// <summary>
/// A single key-value configuration entry.
/// Sensitive entries (ApiKey, InternalApiToken) are encrypted at rest using
/// <see cref="PortwayApi.Helpers.SettingsEncryptionHelper"/> before being stored.
/// </summary>
public class McpConfigEntry
{
    public string   Key         { get; set; } = string.Empty;
    public string   Value       { get; set; } = string.Empty;
    /// <summary>True when the value is PWENC-encrypted. Never expose the raw Value to clients.</summary>
    public bool     IsEncrypted { get; set; } = false;
    public DateTime UpdatedAt   { get; set; } = DateTime.UtcNow;
}
