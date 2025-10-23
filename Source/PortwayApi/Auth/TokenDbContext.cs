namespace PortwayApi.Auth;

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
                    using var cmd = Database.GetDbConnection().CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Tokens') WHERE name='AllowedEnvironments'";
                    
                    if (Database.GetDbConnection().State != System.Data.ConnectionState.Open)
                        Database.GetDbConnection().Open();
                        
                    var result = cmd.ExecuteScalar();
                    hasEnvironmentColumn = Convert.ToInt32(result) > 0;
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
                        Database.ExecuteSqlRaw("ALTER TABLE Tokens ADD COLUMN AllowedEnvironments TEXT NOT NULL DEFAULT '*'");
                        Log.Information("Added AllowedEnvironments column to Tokens table");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error adding AllowedEnvironments column");
                    }
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

    private bool CheckTableExists(string tableName)
    {
        try
        {
            using var cmd = Database.GetDbConnection().CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'";
            
            if (Database.GetDbConnection().State != System.Data.ConnectionState.Open)
                Database.GetDbConnection().Open();
                
            var result = cmd.ExecuteScalar();
            return Convert.ToInt32(result) > 0;
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
            Database.ExecuteSqlRaw(@"
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
                    Description TEXT NOT NULL DEFAULT ''
                )");
            
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
            Database.ExecuteSqlRaw(@"
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
            
            // Add indexes for performance
            entity.HasIndex(e => e.Username).IsUnique(false);
            entity.HasIndex(e => e.CreatedAt);
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