using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Serilog;
using TokenGenerator.Classes;

namespace TokenGenerator.Classes;

public class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

    public DbSet<AuthToken> Tokens { get; set; }
    public DbSet<AuthTokenAudit> TokenAudits { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            // Add connection resilience for SQLite
            optionsBuilder.UseSqlite(options =>
            {
                options.CommandTimeout(30);
            });
        }
        
        // Disable all database logging to prevent SQL queries appearing in console
        optionsBuilder.EnableSensitiveDataLogging(false);
        optionsBuilder.EnableServiceProviderCaching();
        optionsBuilder.EnableDetailedErrors(false);
        
        // Supress the EF Core logging
        optionsBuilder.UseLoggerFactory(Microsoft.Extensions.Logging.LoggerFactory.Create(builder => 
        {
            // Create a logger factory that discards all messages
            builder.AddFilter((category, level) => false);
        }));
        
        // Additional safeguards for .NET 9.0
        optionsBuilder.ConfigureWarnings(warnings => warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted));
        optionsBuilder.ConfigureWarnings(warnings => warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ContextInitialized));
    }

    public async Task<bool> EnsureTablesCreatedAsync()
    {
        try
        {
            // Use async methods to avoid thread blocking issues
            await Database.OpenConnectionAsync();
            
            // Check if the tables exist
            bool tokensTableExists = await CheckTableExistsAsync("Tokens");
            bool auditsTableExists = await CheckTableExistsAsync("TokenAudits");
            
            if (tokensTableExists && auditsTableExists)
            {
                // Verify schema and update if needed
                bool hasCorrectSchema = await CheckSchemaAsync();
                if (!hasCorrectSchema)
                {
                    await UpdateSchemaAsync();
                }
                
                Log.Debug("All tables verified with correct schema");
                return true;
            }
            
            // Create the tables with the complete schema
            await CreateTableAsync();
            Log.Information("Created new tables with complete schema");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error ensuring tables are created");
            return false;
        }
        finally
        {
            await Database.CloseConnectionAsync();
        }
    }

    private async Task<bool> CheckTableExistsAsync(string tableName)
    {
        try
        {
            using var command = Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'";
            
            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result) > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking if {TableName} table exists", tableName);
            return false;
        }
    }

    private async Task<bool> CheckSchemaAsync()
    {
        try
        {
            using var command = Database.GetDbConnection().CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*) FROM pragma_table_info('Tokens') 
                WHERE name IN ('AllowedScopes', 'AllowedEnvironments', 'ExpiresAt', 'Description')";
            
            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result) >= 4; // Should have at least 4 required columns
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking Tokens table schema");
            return false;
        }
    }

    private async Task UpdateSchemaAsync()
    {
        try
        {
            Log.Information("Updating database schema...");
            
            // Update Tokens table if needed
            var tokensCommands = new[]
            {
                "ALTER TABLE Tokens ADD COLUMN AllowedScopes TEXT NOT NULL DEFAULT '*'",
                "ALTER TABLE Tokens ADD COLUMN AllowedEnvironments TEXT NOT NULL DEFAULT '*'",
                "ALTER TABLE Tokens ADD COLUMN ExpiresAt DATETIME NULL",
                "ALTER TABLE Tokens ADD COLUMN Description TEXT NOT NULL DEFAULT ''"
            };

            foreach (var sql in tokensCommands)
            {
                try
                {
                    await Database.ExecuteSqlRawAsync(sql);
                }
                catch (Exception ex) when (ex.Message.Contains("duplicate column"))
                {
                    // Column already exists, continue
                    Log.Debug("Column already exists, skipping: {Sql}", sql);
                }
            }
            
            // Create TokenAudits table if it doesn't exist
            bool auditsTableExists = await CheckTableExistsAsync("TokenAudits");
            if (!auditsTableExists)
            {
                await Database.ExecuteSqlRawAsync(@"
                    CREATE TABLE TokenAudits (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TokenId INTEGER NULL,
                        Username TEXT NOT NULL DEFAULT '',
                        Operation TEXT NOT NULL DEFAULT '',
                        OldTokenHash TEXT NULL,
                        NewTokenHash TEXT NULL,
                        Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                        Details TEXT NOT NULL DEFAULT '',
                        Source TEXT NOT NULL DEFAULT 'TokenGenerator',
                        IpAddress TEXT NULL,
                        UserAgent TEXT NULL
                    )");
                
                Log.Information("Created TokenAudits table");
            }
            
            Log.Information("Successfully updated database schema");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating database schema");
            throw;
        }
    }

    private async Task CreateTableAsync()
    {
        try
        {
            await Database.ExecuteSqlRawAsync(@"
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
            
            // Create the TokenAudits table
            await Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS TokenAudits (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TokenId INTEGER NULL,
                    Username TEXT NOT NULL DEFAULT '',
                    Operation TEXT NOT NULL DEFAULT '',
                    OldTokenHash TEXT NULL,
                    NewTokenHash TEXT NULL,
                    Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                    Details TEXT NOT NULL DEFAULT '',
                    Source TEXT NOT NULL DEFAULT 'TokenGenerator',
                    IpAddress TEXT NULL,
                    UserAgent TEXT NULL
                )");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating Tokens table");
            throw;
        }
    }
    
    public async Task<bool> IsValidDatabaseAsync()
    {
        try
        {
            return await EnsureTablesCreatedAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Database validation failed");
            return false;
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
            
            // Add index for performance
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
            entity.Property(e => e.Source).HasDefaultValue("TokenGenerator").HasMaxLength(100);
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