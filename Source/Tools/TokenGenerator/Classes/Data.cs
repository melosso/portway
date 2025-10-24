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

namespace TokenGenerator.Classes;

/// <summary>
/// Database context for authentication and management operations
/// </summary>
public class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

    public DbSet<AuthToken> Tokens { get; set; }
    public DbSet<AuthTokenAudit> TokenAudits { get; set; }
    public DbSet<Management> Management { get; set; }

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
        
        // Suppress the EF Core logging
        optionsBuilder.UseLoggerFactory(Microsoft.Extensions.Logging.LoggerFactory.Create(builder => 
        {
            // Create a logger factory that discards all messages
            builder.AddFilter((category, level) => false);
        }));
        
        // Additional safeguards for .NET 9.0
        optionsBuilder.ConfigureWarnings(warnings => warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted));
        optionsBuilder.ConfigureWarnings(warnings => warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ContextInitialized));
    }

    /// <summary>
    /// Ensures all required tables exist in the database
    /// </summary>
    public async Task<bool> EnsureTablesCreatedAsync()
    {
        try
        {
            // Use async methods to avoid thread blocking issues
            await Database.OpenConnectionAsync();
            
            // Check if the tables exist
            bool tokensTableExists = await CheckTableExistsAsync("Tokens");
            bool auditsTableExists = await CheckTableExistsAsync("TokenAudits");
            bool managementTableExists = await CheckTableExistsAsync("Management");
            
            // If all tables exist, verify schema
            if (tokensTableExists && auditsTableExists && managementTableExists)
            {
                Log.Debug("All tables already exist, verifying schema...");
                
                // Check and add missing columns if needed
                if (tokensTableExists)
                {
                    await EnsureTokensColumnsExistAsync();
                }
                
                Log.Debug("All tables verified with correct schema");
                return true;
            }
            
            // Create missing tables
            if (!tokensTableExists)
            {
                await CreateTokensTableAsync();
            }
            
            if (!auditsTableExists)
            {
                await CreateTokenAuditsTableAsync();
            }
            
            if (!managementTableExists)
            {
                await CreateManagementTableAsync();
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error ensuring tables are created");
            return false;
        }
    }

    /// <summary>
    /// Check if a table exists in the database
    /// </summary>
    private async Task<bool> CheckTableExistsAsync(string tableName)
    {
        try
        {
            using var cmd = Database.GetDbConnection().CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'";
            
            if (Database.GetDbConnection().State != System.Data.ConnectionState.Open)
                await Database.GetDbConnection().OpenAsync();
                
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result) > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking if {TableName} table exists", tableName);
            return false;
        }
    }

    /// <summary>
    /// Ensure Tokens table has all required columns
    /// </summary>
    private async Task EnsureTokensColumnsExistAsync()
    {
        try
        {
            // Check if AllowedEnvironments column exists
            bool hasEnvironmentColumn = false;
            using (var cmd = Database.GetDbConnection().CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Tokens') WHERE name='AllowedEnvironments'";
                
                if (Database.GetDbConnection().State != System.Data.ConnectionState.Open)
                    await Database.GetDbConnection().OpenAsync();
                    
                var result = await cmd.ExecuteScalarAsync();
                hasEnvironmentColumn = Convert.ToInt32(result) > 0;
            }
            
            // Add the column if it doesn't exist
            if (!hasEnvironmentColumn)
            {
                await Database.ExecuteSqlRawAsync("ALTER TABLE Tokens ADD COLUMN AllowedEnvironments TEXT NOT NULL DEFAULT '*'");
                Log.Information("Added AllowedEnvironments column to Tokens table");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error ensuring Tokens columns exist");
        }
    }

    /// <summary>
    /// Create the Tokens table
    /// </summary>
    private async Task CreateTokensTableAsync()
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
            
            Log.Debug("Created new Tokens table");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating Tokens table");
            throw;
        }
    }

    /// <summary>
    /// Create the TokenAudits table
    /// </summary>
    private async Task CreateTokenAuditsTableAsync()
    {
        try
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
            
            Log.Debug("Created new TokenAudits table");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating TokenAudits table");
            throw;
        }
    }

    /// <summary>
    /// Create the Management table for passphrase protection
    /// </summary>
    private async Task CreateManagementTableAsync()
    {
        try
        {
            await Database.ExecuteSqlRawAsync(@"
                CREATE TABLE Management (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PassphraseHash TEXT NOT NULL DEFAULT '',
                    PassphraseSalt TEXT NOT NULL DEFAULT '',
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt DATETIME NULL,
                    FailedAttempts INTEGER NOT NULL DEFAULT 0,
                    LastFailedAttempt DATETIME NULL,
                    LockedUntil DATETIME NULL,
                    Settings TEXT NOT NULL DEFAULT '{{}}'
                )");
            
            Log.Information("Created new Management table for passphrase protection");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating Management table");
            throw;
        }
    }
    
    /// <summary>
    /// Validate that the database is accessible and properly configured
    /// </summary>
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

    /// <summary>
    /// Configure entity models
    /// </summary>
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
            entity.Property(e => e.Source).HasDefaultValue("TokenGenerator").HasMaxLength(100);
            entity.Property(e => e.IpAddress).IsRequired(false).HasMaxLength(45);
            entity.Property(e => e.UserAgent).IsRequired(false).HasMaxLength(500);
            
            // Add indexes for performance
            entity.HasIndex(e => e.TokenId);
            entity.HasIndex(e => e.Username);
            entity.HasIndex(e => e.Operation);
            entity.HasIndex(e => e.Timestamp);
        });
        
        // Configure the Management table
        modelBuilder.Entity<Management>(entity =>
        {
            entity.ToTable("Management");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PassphraseHash).IsRequired().HasMaxLength(500);
            entity.Property(e => e.PassphraseSalt).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).IsRequired(false);
            entity.Property(e => e.FailedAttempts).HasDefaultValue(0);
            entity.Property(e => e.LastFailedAttempt).IsRequired(false);
            entity.Property(e => e.LockedUntil).IsRequired(false);
            entity.Property(e => e.Settings).HasDefaultValue("{}").HasMaxLength(2000);
        });
    }
}