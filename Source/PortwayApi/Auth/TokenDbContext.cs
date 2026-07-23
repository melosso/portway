namespace PortwayApi.Auth;

using Dapper;
using Microsoft.EntityFrameworkCore;
using Serilog;

public class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }
    public DbSet<AuthToken> Tokens { get; set; }
    public DbSet<AuthTokenAudit> TokenAudits { get; set; }

    public void EnsureTablesCreated()
    {
        try
        {
            // Check if the Tokens table exists
            bool tokensTableExists = CheckTableExists("Tokens");
            bool auditsTableExists = CheckTableExists("TokenAudits");
            
            if (tokensTableExists && auditsTableExists)
            {
                // Check if AllowedEnvironments column exists in Tokens table
                bool hasEnvironmentColumn = false;
                try
                {
                    hasEnvironmentColumn = OpenConnection().ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM pragma_table_info('Tokens') WHERE name=@name",
                        new { name = "AllowedEnvironments" }) > 0;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error checking if AllowedEnvironments column exists");
                }

                // Add the column if it doesn't exist
                if (!hasEnvironmentColumn)
                {
                    try
                    {
                        OpenConnection().Execute("ALTER TABLE Tokens ADD COLUMN AllowedEnvironments TEXT NOT NULL DEFAULT '*'");
                        Log.Information("Added AllowedEnvironments column to Tokens table");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error adding AllowedEnvironments column");
                    }
                }

                EnsureRateLimitColumns();

                // Ensure the active-filter index exists on pre-existing databases
                try
                {
                    OpenConnection().Execute(@"
                        CREATE INDEX IF NOT EXISTS IX_AuthTokens_ActiveFilter
                            ON Tokens (RevokedAt, ExpiresAt)");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not create IX_AuthTokens_ActiveFilter index");
                }

                Log.Debug("All tables verified with correct schema");
                return;
            }
            
            // Create missing tables
            if (!tokensTableExists)
            {
                CreateTokensTable();
            }
            
            if (!auditsTableExists)
            {
                CreateTokenAuditsTable();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error ensuring tables are created");
        }
    }

    // Adds the nullable per-token rate limit columns to pre-existing databases
    private void EnsureRateLimitColumns()
    {
        var migrations = new (string Column, string AlterSql)[]
        {
            ("RateLimitRequests", "ALTER TABLE Tokens ADD COLUMN RateLimitRequests INTEGER NULL"),
            ("RateLimitWindowSeconds", "ALTER TABLE Tokens ADD COLUMN RateLimitWindowSeconds INTEGER NULL"),
        };

        foreach (var (column, alterSql) in migrations)
        {
            try
            {
                var exists = OpenConnection().ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('Tokens') WHERE name=@name", new { name = column });

                if (exists == 0)
                {
                    OpenConnection().Execute(alterSql);
                    Log.Information("Added {Column} column to Tokens table", column);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error ensuring {Column} column exists", column);
            }
        }
    }

    // Opens the context connection once and reuses it for all Dapper calls
    private System.Data.Common.DbConnection OpenConnection()
    {
        var connection = Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            connection.Open();
        return connection;
    }

    private bool CheckTableExists(string tableName)
    {
        try
        {
            return OpenConnection().ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name", new { name = tableName }) > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking if {TableName} table exists", tableName);
            return false;
        }
    }

    private void CreateTokensTable()
    {
        try
        {
            OpenConnection().Execute(@"
                CREATE TABLE Tokens (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username TEXT NOT NULL DEFAULT 'legacy',
                    TokenHash TEXT NOT NULL DEFAULT '',
                    TokenSalt TEXT NOT NULL DEFAULT '',
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                    RevokedAt DATETIME NULL,
                    ExpiresAt DATETIME NULL,
                    AllowedScopes TEXT NOT NULL DEFAULT '*',
                    AllowedEnvironments TEXT NOT NULL DEFAULT '*',
                    Description TEXT NOT NULL DEFAULT '',
                    RateLimitRequests INTEGER NULL,
                    RateLimitWindowSeconds INTEGER NULL
                )");

            OpenConnection().Execute(@"
                CREATE INDEX IF NOT EXISTS IX_AuthTokens_ActiveFilter
                    ON Tokens (RevokedAt, ExpiresAt)");

            Log.Debug("Created new Tokens table");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating Tokens table: {Message}", ex.Message);
            throw;
        }
    }

    private void CreateTokenAuditsTable()
    {
        try
        {
            OpenConnection().Execute(@"
                CREATE TABLE TokenAudits (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TokenId INTEGER NULL,
                    Username TEXT NOT NULL DEFAULT '',
                    Operation TEXT NOT NULL DEFAULT '',
                    OldTokenHash TEXT NULL,
                    NewTokenHash TEXT NULL,
                    Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                    Details TEXT NOT NULL DEFAULT '',
                    Source TEXT NOT NULL DEFAULT 'PortwayApi',
                    IpAddress TEXT NULL,
                    UserAgent TEXT NULL
                )");
            
            Log.Debug("Migration completed: Created new TokenAudits table");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating TokenAudits table: {Message}", ex.Message);
            throw;
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Configure the Tokens table
        modelBuilder.Entity<AuthToken>(entity =>
        {
            entity.ToTable("Tokens");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
            entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(500);
            entity.Property(e => e.TokenSalt).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.RevokedAt).IsRequired(false);
            entity.Property(e => e.ExpiresAt).IsRequired(false);
            entity.Property(e => e.AllowedScopes).HasDefaultValue("*").HasMaxLength(1000);
            entity.Property(e => e.AllowedEnvironments).HasDefaultValue("*").HasMaxLength(1000);
            entity.Property(e => e.Description).HasDefaultValue("").HasMaxLength(500);
            entity.Property(e => e.RateLimitRequests).IsRequired(false);
            entity.Property(e => e.RateLimitWindowSeconds).IsRequired(false);
            
            // Add indexes for performance
            entity.HasIndex(e => e.Username).IsUnique(false);
            entity.HasIndex(e => e.CreatedAt);
            // Composite index on the two columns used in every active-token filter
            entity.HasIndex(e => new { e.RevokedAt, e.ExpiresAt })
                  .HasDatabaseName("IX_AuthTokens_ActiveFilter");
        });
        
        // Configure the TokenAudits table
        modelBuilder.Entity<AuthTokenAudit>(entity =>
        {
            entity.ToTable("TokenAudits");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TokenId).IsRequired(false);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Operation).IsRequired().HasMaxLength(50);
            entity.Property(e => e.OldTokenHash).IsRequired(false).HasMaxLength(500);
            entity.Property(e => e.NewTokenHash).IsRequired(false).HasMaxLength(500);
            entity.Property(e => e.Timestamp).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.Details).HasDefaultValue("").HasMaxLength(2000);
            entity.Property(e => e.Source).HasDefaultValue("PortwayApi").HasMaxLength(100);
            entity.Property(e => e.IpAddress).IsRequired(false).HasMaxLength(45);
            entity.Property(e => e.UserAgent).IsRequired(false).HasMaxLength(500);
            
            // Add indexes for performance
            entity.HasIndex(e => e.TokenId);
            entity.HasIndex(e => e.Username);
            entity.HasIndex(e => e.Operation);
            entity.HasIndex(e => e.Timestamp);
        });
    }
}