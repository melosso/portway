using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

/*
 * Enhanced TokenGenerator with Runtime Stability Improvements and Fixed Logging
 * ===========================================================================
 * 
 * This version includes fixes for .NET runtime issues, enhanced features, and proper logging:
 * 
 * 1. Runtime stability improvements for Windows CFG/shadow stack compatibility
 * 2. Better error handling and graceful degradation
 * 3. Command-line parameters for setting scopes and token expiration
 * 4. Menu options to update token scopes and expiration
 * 5. Improved token file format with better scope display and instructions
 * 6. Support for wildcard scopes and environments
 * 7. Enhanced logging and error reporting
 * 8. FIXED: Disabled SQL query logging to prevent console clutter
 * 
 * Usage examples:
 * - Generate token with specific scopes:
 *   TokenGenerator.exe admin -s "Products,Orders,Customers"
 *   
 * - Generate token with expiration:
 *   TokenGenerator.exe admin --expires 90
 *   
 * - Generate token with description:
 *   TokenGenerator.exe admin --description "API Access for Admin"
 */

namespace TokenGenerator;

public class AuthToken
{
    public int Id { get; set; }
    public required string Username { get; set; } = $"user_{Guid.NewGuid().ToString("N")[..8]}";
    public required string TokenHash { get; set; } = string.Empty;
    public required string TokenSalt { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; } = null;        
    public DateTime? ExpiresAt { get; set; } = null;
    public string AllowedScopes { get; set; } = "*"; // Default to full access
    public string AllowedEnvironments { get; set; } = "*"; // Default to full access
    public string Description { get; set; } = string.Empty;
    public bool IsActive => RevokedAt == null && (ExpiresAt == null || ExpiresAt > DateTime.UtcNow);
}

public class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

    public DbSet<AuthToken> Tokens { get; set; }

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
            
            // Check if the table exists
            bool tableExists = await CheckTableExistsAsync();
            
            if (tableExists)
            {
                // Verify schema and update if needed
                bool hasCorrectSchema = await CheckSchemaAsync();
                if (!hasCorrectSchema)
                {
                    await UpdateSchemaAsync();
                }
                
                Log.Debug("Tokens table verified with correct schema");
                return true;
            }
            
            // Create the table with the complete schema
            await CreateTableAsync();
            Log.Information("Created new Tokens table with complete schema");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error ensuring Tokens table is created");
            return false;
        }
        finally
        {
            await Database.CloseConnectionAsync();
        }
    }

    private async Task<bool> CheckTableExistsAsync()
    {
        try
        {
            using var command = Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Tokens'";
            
            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result) > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking if Tokens table exists");
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
            Log.Information("Updating Tokens table schema...");
            
            var commands = new[]
            {
                "ALTER TABLE Tokens ADD COLUMN AllowedScopes TEXT NOT NULL DEFAULT '*'",
                "ALTER TABLE Tokens ADD COLUMN AllowedEnvironments TEXT NOT NULL DEFAULT '*'",
                "ALTER TABLE Tokens ADD COLUMN ExpiresAt DATETIME NULL",
                "ALTER TABLE Tokens ADD COLUMN Description TEXT NOT NULL DEFAULT ''"
            };

            foreach (var sql in commands)
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
            
            Log.Information("Successfully updated Tokens table schema");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating Tokens table schema");
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
    }
}

public class CommandLineOptions
{
    public string? DatabasePath { get; set; }
    public string? TokensFolder { get; set; }
    public string? Username { get; set; }
    public string? Scopes { get; set; }
    public string? Environments { get; set; }
    public string? Description { get; set; }
    public int? ExpiresInDays { get; set; }
    public bool ShowHelp { get; set; }
    public bool Verbose { get; set; }
}

public class AppConfig
{
    public string DatabasePath { get; set; } = "auth.db";
    public string TokensFolder { get; set; } = "tokens";
    public bool EnableDetailedLogging { get; set; } = false;
}

public class TokenService
{
    private readonly AuthDbContext _dbContext;
    private readonly string _tokenFolderPath;
    private readonly ILogger<TokenService> _logger;

    public TokenService(AuthDbContext dbContext, AppConfig config, ILogger<TokenService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
        
        // Ensure tokens directory exists
        _tokenFolderPath = !Path.IsPathRooted(config.TokensFolder) 
            ? Path.GetFullPath(config.TokensFolder) 
            : config.TokensFolder;
        
        if (!Directory.Exists(_tokenFolderPath))
        {
            Directory.CreateDirectory(_tokenFolderPath);
            Log.Information("Created tokens directory at {Path}", _tokenFolderPath);
        }
    }
    
    public async Task<string> GenerateTokenAsync(
        string username, 
        string allowedScopes = "*",
        string allowedEnvironments = "*", 
        string description = "",
        int? expiresInDays = null)
    {
        try
        {
            // Check if a token for this username already exists
            string tokenFilePath = Path.Combine(_tokenFolderPath, $"{username}.txt");
            if (File.Exists(tokenFilePath))
            {
                Log.Warning("⚠️ A token file for user '{Username}' already exists at '{Path}'", username, tokenFilePath);
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"WARNING: A token file for user '{username}' already exists.");
                Console.WriteLine($"Location: {tokenFilePath}");
                Console.WriteLine("Do you want to generate a new token and overwrite the existing file? (y/n)");
                Console.ResetColor();
                
                string? response = Console.ReadLine()?.Trim().ToLower();
                if (response != "y" && response != "yes")
                {
                    Log.Information("Token generation canceled by user");
                    throw new OperationCanceledException("Token generation canceled by user");
                }
                
                Log.Information("User confirmed overwriting existing token file");
            }
            
            // Generate a secure random token
            string token = GenerateSecureToken();
            
            // Generate salt for hashing
            byte[] salt = GenerateSalt();
            string saltString = Convert.ToBase64String(salt);
            
            // Hash the token
            string hashedToken = HashToken(token, salt);

            // Calculate expiration if specified
            DateTime? expiresAt = expiresInDays.HasValue 
                ? DateTime.UtcNow.AddDays(expiresInDays.Value) 
                : null;
            
            // Create a new token entry
            var tokenEntry = new AuthToken
            {
                Username = username,
                TokenHash = hashedToken,
                TokenSalt = saltString,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt,
                AllowedScopes = allowedScopes,
                AllowedEnvironments = allowedEnvironments,
                Description = description
            };
            
            // Add to database using async methods
            await _dbContext.Tokens.AddAsync(tokenEntry);
            await _dbContext.SaveChangesAsync();
            
            // Save token to file
            await SaveTokenToFileAsync(username, token, allowedScopes, allowedEnvironments, expiresAt, description);
            
            return token;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating token for user {Username}", username);
            throw;
        }
    }

    private static string GenerateSecureToken()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        return RandomNumberGenerator.GetString(chars, 128);
    }

    public async Task<List<AuthToken>> GetActiveTokensAsync()
    {
        try
        {
            return await _dbContext.Tokens
                .Where(t => t.RevokedAt == null && (t.ExpiresAt == null || t.ExpiresAt > DateTime.UtcNow))
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving active tokens");
            return new List<AuthToken>();
        }
    }

    public async Task<List<AuthToken>> GetAllTokensAsync()
    {
        try
        {
            return await _dbContext.Tokens
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all tokens");
            return new List<AuthToken>();
        }
    }

    public async Task<AuthToken?> GetTokenByIdAsync(int id)
    {
        try
        {
            return await _dbContext.Tokens.FindAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving token with ID {Id}", id);
            return null;
        }
    }

    public async Task<bool> RevokeTokenAsync(int id)
    {
        try
        {
            var token = await _dbContext.Tokens.FindAsync(id);
            if (token == null)
                return false;
                
            // Update the RevokedAt timestamp instead of deleting
            token.RevokedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            
            // Delete the token file if it exists
            string filePath = Path.Combine(_tokenFolderPath, $"{token.Username}.txt");
            if (File.Exists(filePath))
            {
                try 
                {
                    File.Delete(filePath);
                    Log.Information("Deleted token file for {Username}", token.Username);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not delete token file for {Username}", token.Username);
                }
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking token with ID {Id}", id);
            return false;
        }
    }
    
    public async Task<bool> UpdateTokenScopesAsync(int id, string newScopes)
    {
        try
        {
            var token = await _dbContext.Tokens.FindAsync(id);
            if (token == null)
                return false;
                
            token.AllowedScopes = newScopes;
            await _dbContext.SaveChangesAsync();
            
            // Update token file if it exists
            await UpdateTokenFileAsync(token);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating token scopes for ID {Id}", id);
            return false;
        }
    }

    public async Task<bool> UpdateTokenEnvironmentsAsync(int id, string newEnvironments)
    {
        try
        {
            var token = await _dbContext.Tokens.FindAsync(id);
            if (token == null)
                return false;
                
            token.AllowedEnvironments = newEnvironments;
            await _dbContext.SaveChangesAsync();
            
            // Update token file if it exists
            await UpdateTokenFileAsync(token);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating token environments for ID {Id}", id);
            return false;
        }
    }

    public async Task<bool> UpdateTokenExpirationAsync(int id, int? daysValid)
    {
        try
        {
            var token = await _dbContext.Tokens.FindAsync(id);
            if (token == null)
                return false;
                
            // Calculate new expiration
            DateTime? expiresAt = daysValid.HasValue 
                ? DateTime.UtcNow.AddDays(daysValid.Value) 
                : null;
                
            token.ExpiresAt = expiresAt;
            await _dbContext.SaveChangesAsync();
            
            // Update token file if it exists
            await UpdateTokenFileAsync(token);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating token expiration for ID {Id}", id);
            return false;
        }
    }

    private async Task UpdateTokenFileAsync(AuthToken token)
    {
        string filePath = Path.Combine(_tokenFolderPath, $"{token.Username}.txt");
        if (File.Exists(filePath))
        {
            try
            {
                // Read the existing token file to preserve the token value
                string jsonContent = await File.ReadAllTextAsync(filePath);
                var tokenInfo = JsonSerializer.Deserialize<TokenFileInfo>(jsonContent);
                
                if (tokenInfo != null)
                {
                    await SaveTokenToFileAsync(
                        token.Username, 
                        tokenInfo.Token, 
                        token.AllowedScopes,
                        token.AllowedEnvironments,
                        token.ExpiresAt, 
                        token.Description);
                        
                    Log.Information("Updated token file for {Username}", token.Username);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not update token file for {Username}", token.Username);
            }
        }
    }
    
    public async Task<bool> VerifyTokenAsync(string token)
    {
        try
        {
            // Get active tokens
            var tokens = await _dbContext.Tokens
                .Where(t => t.RevokedAt == null && (t.ExpiresAt == null || t.ExpiresAt > DateTime.UtcNow))
                .ToListAsync();
            
            // Check each token
            foreach (var storedToken in tokens)
            {
                // Convert stored salt from string to bytes
                byte[] salt = Convert.FromBase64String(storedToken.TokenSalt);
                
                // Hash the provided token with the stored salt
                string hashedToken = HashToken(token, salt);
                
                // Compare hashed tokens
                if (hashedToken == storedToken.TokenHash)
                {
                    return true;
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying token");
            return false;
        }
    }
    
    // Helper class for token file serialization/deserialization
    private class TokenFileInfo
    {
        public string Username { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string AllowedScopes { get; set; } = "*";
        public string AllowedEnvironments { get; set; } = "*";
        public string ExpiresAt { get; set; } = "Never";
        public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        public string Description { get; set; } = string.Empty;
        public object? Remarks { get; set; }
        public string Usage { get; set; } = string.Empty;
    }
    
    private static string HashToken(string token, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(token, salt, 10000, HashAlgorithmName.SHA256);
        byte[] hash = pbkdf2.GetBytes(32); // 256 bits
        return Convert.ToBase64String(hash);
    }
    
    private static byte[] GenerateSalt()
    {
        byte[] salt = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(salt);
        return salt;
    }
    
    private async Task SaveTokenToFileAsync(
        string username, 
        string token, 
        string allowedScopes = "*", 
        string allowedEnvironments = "*",
        DateTime? expiresAt = null,
        string description = "")
    {
        try
        {
            // Ensure tokens directory exists
            if (!Directory.Exists(_tokenFolderPath))
            {
                Directory.CreateDirectory(_tokenFolderPath);
                Log.Information("Created tokens directory at {Path}", _tokenFolderPath);
            }
            
            string filePath = Path.Combine(_tokenFolderPath, $"{username}.txt");
            
            // Create a more informative token file with usage instructions
            var currentWindowsUser = Environment.UserName;
            var tokenInfo = new TokenFileInfo
            {
                Username = username,
                Token = token,
                AllowedScopes = allowedScopes,
                AllowedEnvironments = allowedEnvironments,
                ExpiresAt = expiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never",
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                Description = string.IsNullOrEmpty(description) 
                    ? $"Generated by {currentWindowsUser}"
                    : description,
                Usage = "Use this token in the Authorization header as: Bearer " + token,
                Remarks = new
                {
                    ScopeInformation = new
                    {
                        Format = "Comma-separated list of endpoint names, or * for all endpoints",
                        Examples = new[]
                        {
                            "* (access to all endpoints)",
                            "Products,Customers (access to only these endpoints)",
                            "Product* (access to all endpoints starting with Product)"
                        }
                    },
                    EnvironmentInformation = new
                    {
                        Format = "Comma-separated list of environment names, or * for all environments",
                        Examples = new[]
                        {
                            "* (access to all environments)",
                            "prod,dev,exactsynergy (access to only these environments)",
                            "p* (access to all environments starting with p ...)"
                        }
                    }
                }
            };
            
            var options = new JsonSerializerOptions { WriteIndented = true };
            string tokenJson = JsonSerializer.Serialize(tokenInfo, options);
            await File.WriteAllTextAsync(filePath, tokenJson);
            
            Log.Information("Token file saved to {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save token file for user {Username} at {Path}", username, _tokenFolderPath);
            throw;
        }
    }

    // Make the token folder path accessible
    public string GetTokenFolderPath()
    {
        return _tokenFolderPath;
    }
}

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Set up better exception handling for runtime issues
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            // Parse command-line arguments
            var options = ParseCommandLineArguments(args);
            
            // If help requested, show help and exit
            if (options.ShowHelp)
            {
                DisplayHelp();
                return 0;
            }

            // Configure logging with better error handling
            ConfigureLogging(options.Verbose);

            Log.Information("Portway Token Generator");
            Log.Information("=====================================");

            var (serviceProvider, config) = await ConfigureServicesAsync(options);
            
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            
            Log.Information("Using database at: {DbPath}", config.DatabasePath);
            
            if (!File.Exists(config.DatabasePath))
            {
                DisplayErrorAndExit($"Database not found at {config.DatabasePath}. Please run the main application first or configure the correct path using -d or --database parameter or in appsettings.json.");
                return 1;
            }
            
            if (!await dbContext.IsValidDatabaseAsync())
            {
                DisplayErrorAndExit($"Invalid database structure at {config.DatabasePath}. Please run the main application first.");
                return 1;
            }

            // If username was provided as a command-line argument, generate token for that user
            if (!string.IsNullOrWhiteSpace(options.Username) || args.Length > 0)
            {
                await GenerateTokenForUserAsync(
                    options.Username ?? "", 
                    options.Scopes ?? "*",
                    options.Environments ?? "*",
                    options.Description ?? "",
                    options.ExpiresInDays,
                    serviceProvider);
                return 0;
            }

            bool exitRequested = false;

            while (!exitRequested)
            {
                try
                {
                    DisplayMenu();
                    string choice = Console.ReadLine() ?? "";

                    switch (choice)
                    {
                        case "1":
                            await ListAllTokensAsync(serviceProvider);
                            break;
                        case "2":
                            await AddNewTokenAsync(serviceProvider);
                            break;
                        case "3":
                            await RevokeTokenAsync(serviceProvider);
                            break;
                        case "4":
                            await UpdateTokenScopesAsync(serviceProvider);
                            break;
                        case "5":
                            await UpdateTokenEnvironmentsAsync(serviceProvider);
                            break;
                        case "6":
                            await UpdateTokenExpirationAsync(serviceProvider);
                            break;
                        case "0":
                            exitRequested = true;
                            break;
                        default:
                            Console.WriteLine("Invalid option. Please try again.");
                            break;
                    }

                    if (!exitRequested)
                    {
                        Console.WriteLine("\nPress any key to return to menu...");
                        Console.ReadKey();
                        Console.Clear();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in menu operation");
                    Console.WriteLine($"An error occurred: {ex.Message}");
                    Console.WriteLine("Press any key to continue...");
                    Console.ReadKey();
                }
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            // Operation was canceled by user, exit gracefully
            Console.WriteLine("Operation canceled. Press any key to exit...");
            Console.ReadKey();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "An error occurred: {ErrorMessage}", ex.Message);
            DisplayErrorAndExit($"An error occurred: {ex.Message}");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception occurred");
        if (e.IsTerminating)
        {
            Log.CloseAndFlush();
        }
    }

    static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }

    static void ConfigureLogging(bool verbose = false)
    {
        var logConfig = new LoggerConfiguration()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .MinimumLevel.Information();

        if (verbose)
        {
            logConfig.MinimumLevel.Debug();
        }

        Log.Logger = logConfig.CreateLogger();
    }

    static void DisplayHelp()
    {
        Console.WriteLine("PortwayApi Token Generator v2.0");
        Console.WriteLine("===============================");
        Console.WriteLine("A utility to manage authentication tokens for the PortwayApi service.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  TokenGenerator.exe [options] [username]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -h, --help                    Show this help message");
        Console.WriteLine("  -d, --database <path>         Specify the path to the auth.db file");
        Console.WriteLine("  -t, --tokens <path>           Specify the folder to store token files");
        Console.WriteLine("  -s, --scopes <scopes>         Specify allowed scopes (comma-separated or * for all)");
        Console.WriteLine("  -e, --environments <envs>     Specify allowed environments (comma-separated or * for all)");
        Console.WriteLine("  --description <text>          Add a description for the token");
        Console.WriteLine("  --expires <days>              Set token expiration in days");
        Console.WriteLine("  -v, --verbose                 Enable verbose logging");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  TokenGenerator.exe                                 Run in interactive mode");
        Console.WriteLine("  TokenGenerator.exe -d \"C:\\path\\to\\auth.db\"    Use specific database file");
        Console.WriteLine("  TokenGenerator.exe                                 Generate token with auto-generated UUID username");
        Console.WriteLine("  TokenGenerator.exe admin                           Generate token for user 'admin'");
        Console.WriteLine("  TokenGenerator.exe admin -s \"Products,Orders\"    Generate token with specific scopes");
        Console.WriteLine("  TokenGenerator.exe admin -e \"prod,dev\"           Generate token for specific environments");
        Console.WriteLine("  TokenGenerator.exe -s \"*\" --expires 90 admin     Generate token that expires in 90 days");
    }

    static void DisplayErrorAndExit(string errorMessage)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("\nERROR: " + errorMessage);
        Console.ResetColor();
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
        Environment.Exit(1);
    }
    
    static CommandLineOptions ParseCommandLineArguments(string[] args)
    {
        var options = new CommandLineOptions();
        
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].ToLowerInvariant();
            
            switch (arg)
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    return options;
                    
                case "-d":
                case "--database":
                    if (i + 1 < args.Length)
                    {
                        options.DatabasePath = args[++i];
                    }
                    break;
                    
                case "-t":
                case "--tokens":
                    if (i + 1 < args.Length)
                    {
                        options.TokensFolder = args[++i];
                    }
                    break;
                    
                case "-s":
                case "--scopes":
                    if (i + 1 < args.Length)
                    {
                        options.Scopes = args[++i];
                    }
                    break;

                case "-e":
                case "--environments":
                    if (i + 1 < args.Length)
                    {
                        options.Environments = args[++i];
                    }
                    break;
                    
                case "--description":
                    if (i + 1 < args.Length)
                    {
                        options.Description = args[++i];
                    }
                    break;
                    
                case "--expires":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int days))
                    {
                        options.ExpiresInDays = days;
                        i++;
                    }
                    break;

                case "-v":
                case "--verbose":
                    options.Verbose = true;
                    break;
                    
                default:
                    // If not a known option and no username set yet, assume it's a username
                    if (!arg.StartsWith("-") && string.IsNullOrWhiteSpace(options.Username))
                    {
                        options.Username = args[i];
                    }
                    break;
            }
        }
        
        return options;
    }

    static void DisplayMenu()
    {
        Console.WriteLine("");
        Console.WriteLine("===============================================");
        Console.WriteLine("            Portway Token Generator           ");
        Console.WriteLine("===============================================");
        Console.WriteLine("1. List all existing tokens");
        Console.WriteLine("2. Generate new token");
        Console.WriteLine("3. Revoke token");
        Console.WriteLine("4. Update token scopes");
        Console.WriteLine("5. Update token environments");
        Console.WriteLine("6. Update token expiration");
        Console.WriteLine("0. Exit");
        Console.WriteLine("-----------------------------------------------");
        Console.WriteLine("");
        Console.Write("Select an option: ");
    }

    static async Task ListAllTokensAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();

        var tokens = await tokenService.GetActiveTokensAsync();

        if (tokens.Count == 0)
        {
            Console.WriteLine("\nNo active tokens found in the database.");
            return;
        }

        Console.WriteLine("\n=== Active Tokens ===");
        Console.WriteLine($"{"ID",-5} {"Username",-20} {"Created",-20} {"Expires",-20} {"Scopes",-15} {"Environments",-15}");
        Console.WriteLine(new string('-', 100));

        string tokenFolderPath = tokenService.GetTokenFolderPath();
        
        foreach (var token in tokens)
        {                
            string tokenFilePath = Path.Combine(tokenFolderPath, $"{token.Username}.txt");
            string expiration = token.ExpiresAt?.ToString("yyyy-MM-dd") ?? "Never";
            
            // Truncate scopes and environments if too long
            string scopes = token.AllowedScopes;
            if (scopes.Length > 15)
            {
                scopes = scopes[..12] + "...";
            }
            
            string environments = token.AllowedEnvironments;
            if (environments.Length > 15)
            {
                environments = environments[..12] + "...";
            }

            Console.WriteLine($"{token.Id,-5} {token.Username,-20} {token.CreatedAt:yyyy-MM-dd HH:mm,-20} {expiration,-20} {scopes,-15} {environments,-15}");
        }
    }

    static async Task AddNewTokenAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("\n=== Generate New Token ===");
        
        // Get username
        Console.Write("Enter username (leave blank for auto-generated UUID): ");
        string? input = Console.ReadLine();
        string username = string.IsNullOrWhiteSpace(input) 
            ? $"user_{Guid.NewGuid().ToString("N")[..8]}" 
            : input;

        // Get scopes e.g. endpoints
        Console.Write("Enter allowed scopes (comma-separated, or * for all endpoints): ");
        string scopesInput = Console.ReadLine() ?? "*";
        string scopes = string.IsNullOrWhiteSpace(scopesInput) ? "*" : scopesInput;

        // Get environments
        Console.Write("Enter allowed environments (comma-separated, or * for all environments): ");
        string environmentsInput = Console.ReadLine() ?? "*";
        string environments = string.IsNullOrWhiteSpace(environmentsInput) ? "*" : environmentsInput;

        // Get description
        Console.Write("Enter description (optional): ");
        string description = Console.ReadLine() ?? "";

        // Get expiration
        Console.Write("Enter expiration in days (leave blank for no expiration): ");
        string expirationInput = Console.ReadLine() ?? "";
        int? expirationDays = null;
        if (!string.IsNullOrWhiteSpace(expirationInput) && int.TryParse(expirationInput, out int days))
        {
            expirationDays = days;
        }

        using var scope = serviceProvider.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();

        try
        {
            Console.WriteLine($"Generating token for user: {username}");
            var token = await tokenService.GenerateTokenAsync(
                username, 
                scopes, 
                environments, 
                description, 
                expirationDays);

            Console.WriteLine("\n--- Token Generated Successfully ---");
            Console.WriteLine($"Username: {username}");
            Console.WriteLine($"Token: {token}");
            Console.WriteLine($"Allowed Scopes: {scopes}");
            Console.WriteLine($"Allowed Environments: {environments}");
            if (expirationDays.HasValue)
            {
                Console.WriteLine($"Expires: In {expirationDays} days ({DateTime.Now.AddDays(expirationDays.Value):yyyy-MM-dd})");
            }
            else
            {
                Console.WriteLine("Expires: Never");
            }
            
            string tokenFolderPath = tokenService.GetTokenFolderPath();
            
            Console.WriteLine($"Token file: {Path.Combine(tokenFolderPath, $"{username}.txt")}");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\nToken generation canceled.");
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error generating token: {ErrorMessage}", ex.Message);
            Console.WriteLine($"\nError generating token: {ex.Message}");
        }
    }

    static async Task RevokeTokenAsync(IServiceProvider serviceProvider)
    {
        // Display current tokens first
        await ListAllTokensAsync(serviceProvider);

        Console.WriteLine("\n=== Revoke Token ===");
        Console.Write("Enter token ID to revoke (or 0 to cancel): ");
        
        if (!int.TryParse(Console.ReadLine(), out int tokenId) || tokenId <= 0)
        {
            Console.WriteLine("Operation cancelled.");
            return;
        }

        using var scope = serviceProvider.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();

        bool result = await tokenService.RevokeTokenAsync(tokenId);
        if (result)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Token with ID {tokenId} has been revoked successfully.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Token with ID {tokenId} not found.");
            Console.ResetColor();
        }
    }

    static async Task UpdateTokenScopesAsync(IServiceProvider serviceProvider)
    {
        // Display current tokens first
        await ListAllTokensAsync(serviceProvider);

        Console.WriteLine("\n=== Update Token Scopes ===");
        Console.Write("Enter token ID to update (or 0 to cancel): ");
        
        if (!int.TryParse(Console.ReadLine(), out int tokenId) || tokenId <= 0)
        {
            Console.WriteLine("Operation cancelled.");
            return;
        }

        using var scope = serviceProvider.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();
        
        // Get the token to update
        var token = await tokenService.GetTokenByIdAsync(tokenId);
        if (token == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Token with ID {tokenId} not found.");
            Console.ResetColor();
            return;
        }
        
        // Display current scopes
        Console.WriteLine($"Current scopes: {token.AllowedScopes}");
        Console.WriteLine("\nScope format options:");
        Console.WriteLine("  * - Full access to all endpoints");
        Console.WriteLine("  Products,Orders,Invoices - Access to specific endpoints (comma separated)");
        Console.WriteLine("  Product* - Access to all endpoints that start with 'Product'");
        
        // Get new scopes
        Console.Write("\nEnter new scopes: ");
        string newScopes = Console.ReadLine() ?? "*";
        if (string.IsNullOrWhiteSpace(newScopes))
        {
            newScopes = "*";
        }
        
        // Confirm update
        Console.WriteLine($"\nUpdating token for {token.Username}");
        Console.WriteLine($"Old scopes: {token.AllowedScopes}");
        Console.WriteLine($"New scopes: {newScopes}");
        Console.Write("\nConfirm update? (y/n): ");
        
        string? response = Console.ReadLine()?.Trim().ToLower();
        if (response != "y" && response != "yes")
        {
            Console.WriteLine("Update cancelled.");
            return;
        }
        
        // Update token scopes
        bool result = await tokenService.UpdateTokenScopesAsync(tokenId, newScopes);
        if (result)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Token scopes updated successfully for {token.Username}.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Failed to update token scopes.");
            Console.ResetColor();
        }
    }

    static async Task UpdateTokenEnvironmentsAsync(IServiceProvider serviceProvider)
    {
        // Display current tokens first
        await ListAllTokensAsync(serviceProvider);

        Console.WriteLine("\n=== Update Token Environments ===");
        Console.Write("Enter token ID to update (or 0 to cancel): ");
        
        if (!int.TryParse(Console.ReadLine(), out int tokenId) || tokenId <= 0)
        {
            Console.WriteLine("Operation cancelled.");
            return;
        }

        using var scope = serviceProvider.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();
        
        // Get the token to update
        var token = await tokenService.GetTokenByIdAsync(tokenId);
        if (token == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Token with ID {tokenId} not found.");
            Console.ResetColor();
            return;
        }
        
        // Display current environments
        Console.WriteLine($"Current environments: {token.AllowedEnvironments}");
        Console.WriteLine("\nEnvironment format options:");
        Console.WriteLine("  * - Full access to all environments");
        Console.WriteLine("  600,700,Synergy - Access to specific environments (comma separated)");
        Console.WriteLine("  6* - Access to all environments that start with '6'");
        
        // Get new environments
        Console.Write("\nEnter new environments: ");
        string newEnvironments = Console.ReadLine() ?? "*";
        if (string.IsNullOrWhiteSpace(newEnvironments))
        {
            newEnvironments = "*";
        }
        
        // Confirm update
        Console.WriteLine($"\nUpdating token for {token.Username}");
        Console.WriteLine($"Old environments: {token.AllowedEnvironments}");
        Console.WriteLine($"New environments: {newEnvironments}");
        Console.Write("\nConfirm update? (y/n): ");
        
        string? response = Console.ReadLine()?.Trim().ToLower();
        if (response != "y" && response != "yes")
        {
            Console.WriteLine("Update cancelled.");
            return;
        }
        
        bool result = await tokenService.UpdateTokenEnvironmentsAsync(tokenId, newEnvironments);
        if (result)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Token environments updated successfully for {token.Username}.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Failed to update token environments.");
            Console.ResetColor();
        }
    }
    
    static async Task UpdateTokenExpirationAsync(IServiceProvider serviceProvider)
    {
        // Display current tokens first
        await ListAllTokensAsync(serviceProvider);

        Console.WriteLine("\n=== Update Token Expiration ===");
        Console.Write("Enter token ID to update (or 0 to cancel): ");
        
        if (!int.TryParse(Console.ReadLine(), out int tokenId) || tokenId <= 0)
        {
            Console.WriteLine("Operation cancelled.");
            return;
        }

        using var scope = serviceProvider.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();
        
        // Get the token to update
        var token = await tokenService.GetTokenByIdAsync(tokenId);
        if (token == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Token with ID {tokenId} not found.");
            Console.ResetColor();
            return;
        }
        
        // Display current expiration
        string currentExpiration = token.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never";
        Console.WriteLine($"Current expiration: {currentExpiration}");
        
        // Get new expiration
        Console.WriteLine("\nExpiration options:");
        Console.WriteLine("  0 - No expiration (token never expires)");
        Console.WriteLine("  30 - Expires in 30 days");
        Console.WriteLine("  90 - Expires in 90 days");
        Console.WriteLine("  365 - Expires in 1 year");
        
        Console.Write("\nEnter days until expiration (or 0 for no expiration): ");
        if (!int.TryParse(Console.ReadLine(), out int days))
        {
            Console.WriteLine("Invalid input. Operation cancelled.");
            return;
        }
        
        // Convert to nullable int (null for no expiration)
        int? daysValid = days <= 0 ? null : days;
        
        // Calculate and display new expiration
        string newExpiration = daysValid.HasValue 
            ? DateTime.Now.AddDays(daysValid.Value).ToString("yyyy-MM-dd HH:mm:ss")
            : "Never";
                
        Console.WriteLine($"\nUpdating token for {token.Username}");
        Console.WriteLine($"Old expiration: {currentExpiration}");
        Console.WriteLine($"New expiration: {newExpiration}");
        Console.Write("\nConfirm update? (y/n): ");
        
        string? response = Console.ReadLine()?.Trim().ToLower();
        if (response != "y" && response != "yes")
        {
            Console.WriteLine("Update cancelled.");
            return;
        }
        
        // Update token expiration
        bool result = await tokenService.UpdateTokenExpirationAsync(tokenId, daysValid);
        if (result)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Token expiration updated successfully for {token.Username}.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Failed to update token expiration.");
            Console.ResetColor();
        }
    }

    static async Task GenerateTokenForUserAsync(
        string username, 
        string scopes, 
        string environments,
        string description,
        int? expiresInDays,
        IServiceProvider serviceProvider)
    {
        try
        {   
            // If username is blank, generate a UUID-based one
            if (string.IsNullOrWhiteSpace(username))
            {
                username = $"user_{Guid.NewGuid().ToString("N")[..8]}";
                Log.Information("No username provided. Generated UUID-based username: {Username}", username);
            } 
                        
            using var scope = serviceProvider.CreateScope();
            var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();

            var token = await tokenService.GenerateTokenAsync(
                username, 
                scopes, 
                environments,
                description,
                expiresInDays);
            
            Log.Information("✅ Token generation successful!");
            Log.Information("Username: {Username}", username);
            Log.Information("Token: {Token}", token);
            Log.Information("Scopes: {Scopes}", scopes);
            Log.Information("Environments: {Environments}", environments);
            
            if (expiresInDays.HasValue)
            {
                Log.Information("Expires: In {Days} days ({Date})", 
                    expiresInDays, 
                    DateTime.Now.AddDays(expiresInDays.Value).ToString("yyyy-MM-dd"));
            }
            else
            {
                Log.Information("Expires: Never");
            }
            
            Log.Information("Token file: {FilePath}", Path.Combine(tokenService.GetTokenFolderPath(), $"{username}.txt"));
        }
        catch (OperationCanceledException)
        {
            Log.Information("Token generation canceled by user");
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error generating token: {ErrorMessage}", ex.Message);
            DisplayErrorAndExit($"Error generating token: {ex.Message}");
        }
    }

    static async Task<(IServiceProvider ServiceProvider, AppConfig Config)> ConfigureServicesAsync(CommandLineOptions cliOptions)
    {
        var services = new ServiceCollection();
        var config = await LoadConfigurationAsync();
        
        // Override with command-line options if provided
        if (!string.IsNullOrWhiteSpace(cliOptions.DatabasePath))
        {
            config.DatabasePath = cliOptions.DatabasePath;
            Log.Information("Using database path from command line: {Path}", config.DatabasePath);
        }
        
        if (!string.IsNullOrWhiteSpace(cliOptions.TokensFolder))
        {
            config.TokensFolder = cliOptions.TokensFolder;
            Log.Information("Using tokens folder from command line: {Path}", config.TokensFolder);
        }
        
        // Register config as a singleton
        services.AddSingleton(config);
        
        // Ensure paths are absolute
        if (!Path.IsPathRooted(config.DatabasePath))
        {
            config.DatabasePath = Path.GetFullPath(config.DatabasePath);
        }
        
        // Check if database is in parent directory (../auth.db or ../../auth.db)
        string parentDbPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "auth.db"));
        string parentParentDbPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "auth.db"));
        string appDbPath = "";
        
        if (File.Exists(parentParentDbPath))
        {
            appDbPath = parentParentDbPath;
            var parentParentDir = Path.GetDirectoryName(parentParentDbPath);
            
            Log.Information("Found database in parent's parent directory: {DbPath}", parentParentDbPath);
            
            // Check if tokens folder exists in the same directory
            string parentParentTokensPath = Path.Combine(parentParentDir!, "tokens");
            if (Directory.Exists(parentParentTokensPath))
            {
                config.TokensFolder = parentParentTokensPath;
                Log.Information("Using tokens folder from parent's parent directory: {TokensFolder}", parentParentTokensPath);
            }
        }
        else if (File.Exists(parentDbPath))
        {
            appDbPath = parentDbPath;
            var parentDir = Path.GetDirectoryName(parentDbPath);
            
            Log.Information("Found database in parent directory: {DbPath}", parentDbPath);
            
            // Check if tokens folder exists in the same directory
            string parentTokensPath = Path.Combine(parentDir!, "tokens");
            if (Directory.Exists(parentTokensPath))
            {
                config.TokensFolder = parentTokensPath;
                Log.Information("Using tokens folder from parent directory: {TokensFolder}", parentTokensPath);
            }
        }
        
        // Use found database if not explicitly specified by user
        if (!string.IsNullOrEmpty(appDbPath) && string.IsNullOrEmpty(cliOptions.DatabasePath))
        {
            config.DatabasePath = appDbPath;
            Log.Information("Using database found in application directory: {DbPath}", appDbPath);
        }
        
        if (!Path.IsPathRooted(config.TokensFolder))
        {
            config.TokensFolder = Path.GetFullPath(config.TokensFolder);
        }
        
        // Ensure tokens directory exists
        if (!Directory.Exists(config.TokensFolder))
        {
            Directory.CreateDirectory(config.TokensFolder);
            Log.Information("Created tokens directory at {Path}", config.TokensFolder);
        }
        
        Log.Debug("Database path: {DbPath}", config.DatabasePath);
        Log.Debug("Tokens folder: {TokensFolder}", config.TokensFolder);
        
        // Add logging services with EF filtering
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
            
            // CRITICAL: Filter out Entity Framework logging for .NET 9.0
            builder.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
            builder.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);
            builder.AddFilter("Microsoft.EntityFrameworkCore.Infrastructure", LogLevel.None);
            builder.AddFilter("Microsoft.EntityFrameworkCore.Database.Connection", LogLevel.None);
            builder.AddFilter("Microsoft.EntityFrameworkCore.Database.Transaction", LogLevel.None);
            builder.AddFilter("Microsoft.EntityFrameworkCore.Storage", LogLevel.None);
            builder.AddFilter("Microsoft.EntityFrameworkCore.Query", LogLevel.None);
            builder.AddFilter("Microsoft.Data.Sqlite", LogLevel.None);
            
            // Additional filters for .NET 9.0 EF Core changes
            builder.AddFilter((category, level) => 
            {
                return !string.IsNullOrEmpty(category) && 
                       !category.StartsWith("Microsoft.EntityFrameworkCore") && 
                       !category.StartsWith("Microsoft.Data.Sqlite");
            });
        });
        
        // Add EF DbContext with better configuration and NO logging
        services.AddDbContext<AuthDbContext>(options =>
        {
            options.UseSqlite($"Data Source={config.DatabasePath}", sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(30);
            });
            
            // Performance and reliability improvements
            options.EnableSensitiveDataLogging(false);
            options.EnableServiceProviderCaching();
            options.EnableDetailedErrors(false);
            
            // CRITICAL: For .NET 9.0 - Completely disable Entity Framework logging
            options.UseLoggerFactory(Microsoft.Extensions.Logging.LoggerFactory.Create(builder => 
            {
                builder.AddFilter((category, level) => false);
            }));
            
            // Additional safeguards for .NET 9.0
            options.ConfigureWarnings(warnings => 
            {
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted);
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ContextInitialized);
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.ConnectionOpened);
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.ConnectionClosed);
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandCreated);
            });
        });

        services.AddScoped<TokenService>();
        
        return (services.BuildServiceProvider(), config);
    }
    
    static async Task<AppConfig> LoadConfigurationAsync()
    {
        var config = new AppConfig();
        string configFileName = "appsettings.json";
        string configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configFileName);
        
        // Create default config if it doesn't exist
        if (!File.Exists(configFilePath))
        {
            Log.Information("Configuration file not found. Creating default configuration.");
            await CreateDefaultConfigAsync(configFilePath);
        }
        
        try
        {
            var configJson = await File.ReadAllTextAsync(configFilePath);
            var configValues = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(configJson);
            
            if (configValues != null)
            {
                if (configValues.TryGetValue("DatabasePath", out var dbPathElement) && 
                    dbPathElement.ValueKind == JsonValueKind.String)
                {
                    config.DatabasePath = dbPathElement.GetString() ?? "auth.db";
                }
                
                if (configValues.TryGetValue("TokensFolder", out var tokensFolderElement) && 
                    tokensFolderElement.ValueKind == JsonValueKind.String)
                {
                    config.TokensFolder = tokensFolderElement.GetString() ?? "tokens";
                }

                if (configValues.TryGetValue("EnableDetailedLogging", out var loggingElement) && 
                    loggingElement.ValueKind == JsonValueKind.True)
                {
                    config.EnableDetailedLogging = loggingElement.GetBoolean();
                }
            }
            
            Log.Information("Configuration loaded successfully.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error loading configuration. Using default values.");
        }
        
        return config;
    }
    
    static async Task CreateDefaultConfigAsync(string configFilePath)
    {
        // Ensure directory exists
        var directory = Path.GetDirectoryName(configFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        var defaultConfig = new AppConfig
        {
            DatabasePath = "../../auth.db",  // Default to parent of parent directory
            TokensFolder = "tokens",         // Default to local tokens directory
            EnableDetailedLogging = false
        };
        
        try
        {
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(defaultConfig, jsonOptions);
            await File.WriteAllTextAsync(configFilePath, json);
            Log.Information("Created default configuration file at {Path}", configFilePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create default configuration file");
        }
    }
}