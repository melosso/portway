using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using TokenGenerator.Classes;
using TokenGenerator.Services;

namespace TokenGenerator;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Set up unhandled exception handlers
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            // Parse command line options first to check for verbose flag and help
            var cliOptions = ParseCommandLineOptions(args);
            
            // Show help and exit if requested
            if (cliOptions.ShowHelp)
            {
                DisplayHelp();
                return 0;
            }
            
            // Configure logging based on verbose flag
            ConfigureLogging(cliOptions.Verbose);
            
            Log.Debug("PortwayMgt starting up...");
            
            // Load configuration
            var config = await LoadConfigurationAsync(cliOptions);
            
            // Override with command line options if provided
            if (!string.IsNullOrWhiteSpace(cliOptions.DatabasePath))
            {
                config.DatabasePath = cliOptions.DatabasePath;
            }
            
            if (!string.IsNullOrWhiteSpace(cliOptions.TokensFolder))
            {
                config.TokensFolder = cliOptions.TokensFolder;
            }
            
            // Build service provider
            var services = new ServiceCollection();

            services.AddLogging(builder =>
            {
                builder.ClearProviders();  
                builder.AddSerilog(dispose: true); 
                builder.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
            });

            services.AddSingleton(config); 
            services.AddDbContext<AuthDbContext>(options =>
            {
                options.UseSqlite($"Data Source={config.DatabasePath}");
            });

            services.AddScoped<TokenService>(); 
            services.AddScoped<ManagementService>();

            var serviceProvider = services.BuildServiceProvider();
            
            // Ensure database and tables are created
            using (var scope = serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
                await context.EnsureTablesCreatedAsync();
            }
            
            // Check if passphrase is set up
            if (!cliOptions.NoAuth)
            {
                // Check if passphrase is set up
                using (var scope = serviceProvider.CreateScope())
                {
                    var managementService = scope.ServiceProvider.GetRequiredService<ManagementService>();
                    
                    if (!await managementService.IsPassphraseSetupAsync())
                    {
                        // First-time setup
                        int setupResult = await InitialPassphraseSetupAsync(managementService);
                        if (setupResult != 0)
                        {
                            return setupResult;
                        }
                    }
                    else
                    {
                        // Authenticate user
                        if (!await AuthenticateUserAsync(managementService))
                        {
                            Log.Warning("Authentication failed");
                            return 1;
                        }
                    }
                }
            }
            else
            {
                Log.Warning("Running in NO-AUTH mode (passphrase protection disabled)");
                Console.WriteLine("   WARNING: Running in NO-AUTH mode (passphrase protection disabled)");
                Console.WriteLine();
            }
            
            // If username was provided as command-line argument, generate token and exit
            if (!string.IsNullOrWhiteSpace(cliOptions.Username))
            {
                await GenerateTokenForUserAsync(
                    cliOptions.Username, 
                    cliOptions.Scopes ?? "*",
                    cliOptions.Environments ?? "*",
                    cliOptions.Description ?? "",
                    cliOptions.ExpiresInDays,
                    serviceProvider);
                return 0;
            }
            
            // Show menu and handle operations
            bool exitRequested = false;
            
            while (!exitRequested)
            {
                try
                {
                    DisplayMenu();
                    var choice = Console.ReadLine()?.Trim();
                    
                    Console.Clear();
                    
                    switch (choice)
                    {
                        case "1":
                            await ListAllTokensAsync(serviceProvider);
                            break;
                            
                        case "2":
                            await GenerateNewTokenAsync(serviceProvider);
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
                            
                        case "7":
                            await RotateTokenAsync(serviceProvider);
                            break;
                            
                        case "8":
                            if (cliOptions.NoAuth)
                            {
                                Console.WriteLine("  Passphrase management is disabled in no-auth mode.");
                            }
                            else
                            {
                                await ChangePassphraseAsync(serviceProvider);
                            }
                            break;
                            
                        case "0":
                            exitRequested = true;
                            Console.WriteLine("Exiting PortwayMgt...");
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
            Console.WriteLine("Operation canceled. Press any key to exit...");
            Console.ReadKey();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "An error occurred: {ErrorMessage}", ex.Message);
            Console.WriteLine($"An error occurred: {ex.Message}");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    static async Task<int> InitialPassphraseSetupAsync(ManagementService managementService)
    {
        Console.Clear();
        Console.WriteLine("==============================================");
        Console.WriteLine("          Portway Management Console");
        Console.WriteLine("==============================================");
        Console.WriteLine();
        Console.WriteLine("Welcome! This is your first time running PortwayMgt.");
        Console.WriteLine("You need to set up a passphrase to protect auth.db.");
        Console.WriteLine();
        Console.WriteLine("Passphrase requirements:");
        Console.WriteLine("  • At least 12 characters long");
        Console.WriteLine("  • Contains uppercase and lowercase letters");
        Console.WriteLine("  • Contains at least one digit");
        Console.WriteLine("  • Contains at least one special character");
        Console.WriteLine();
        
        int attempts = 0;
        const int maxAttempts = 3;
        
        while (attempts < maxAttempts)
        {
            Console.Write("Enter new passphrase: ");
            var passphrase = ReadPassword();
            Console.WriteLine();
            
            Console.Write("Confirm passphrase: ");
            var confirmPassphrase = ReadPassword();
            Console.WriteLine();
            
            if (passphrase != confirmPassphrase)
            {
                Console.WriteLine("Passphrases do not match. Please try again.");
                Console.WriteLine();
                attempts++;
                continue;
            }
            
            var result = await managementService.SetupPassphraseAsync(passphrase);
            
            if (result.Success)
            {
                Console.WriteLine();
                Console.WriteLine("Success: " + result.Message);
                Console.WriteLine();
                Console.WriteLine("Press any key to continue to PortwayMgt...");
                Console.ReadKey();
                Console.Clear();
                return 0;
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("" + result.Message);
                Console.WriteLine();
                attempts++;
            }
        }
        
        Console.WriteLine("Too many failed attempts. Exiting...");
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
        return 1;
    }

    static async Task<bool> AuthenticateUserAsync(ManagementService managementService)
    {
        Console.Clear();
        Console.WriteLine("==============================================");
        Console.WriteLine("          Portway Management Console");
        Console.WriteLine("==============================================");
        Console.WriteLine();
        
        var (isLocked, remainingTime) = await managementService.IsAccountLockedAsync();
        if (isLocked && remainingTime.HasValue)
        {
            Console.WriteLine($"Account is locked for {(int)remainingTime.Value.TotalMinutes}m {remainingTime.Value.Seconds}s");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
            return false;
        }
        
        int attempts = 0;
        const int maxAttempts = 5;
        
        while (attempts < maxAttempts)
        {
            Console.Write("Enter passphrase: ");
            var passphrase = ReadPassword();
            Console.WriteLine();
            
            var result = await managementService.VerifyPassphraseAsync(passphrase);
            
            if (result.Success)
            {
                Console.WriteLine("Success: Authentication successful!");
                await Task.Delay(500);
                Console.Clear();
                return true;
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("" + result.Message);
                Console.WriteLine();
                attempts++;
                
                (isLocked, remainingTime) = await managementService.IsAccountLockedAsync();
                if (isLocked)
                {
                    Console.WriteLine("Press any key to exit...");
                    Console.ReadKey();
                    return false;
                }
            }
        }
        
        return false;
    }

    static async Task ChangePassphraseAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var managementService = scope.ServiceProvider.GetRequiredService<ManagementService>();
        
        Console.WriteLine("==============================================");
        Console.WriteLine("              Change Passphrase");
        Console.WriteLine("==============================================");
        Console.WriteLine();
        
        Console.Write("Enter current passphrase: ");
        var currentPassphrase = ReadPassword();
        Console.WriteLine();
        
        Console.Write("Enter new passphrase: ");
        var newPassphrase = ReadPassword();
        Console.WriteLine();
        
        Console.Write("Confirm new passphrase: ");
        var confirmPassphrase = ReadPassword();
        Console.WriteLine();
        
        if (newPassphrase != confirmPassphrase)
        {
            Console.WriteLine("New passphrases do not match.");
            return;
        }
        
        var result = await managementService.ChangePassphraseAsync(currentPassphrase, newPassphrase);
        
        if (result.Success)
        {
            Console.WriteLine("Success: " + result.Message);
        }
        else
        {
            Console.WriteLine("" + result.Message);
        }
    }

    static string ReadPassword()
    {
        var password = new System.Text.StringBuilder();
        
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }
            else if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password.Remove(password.Length - 1, 1);
                Console.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
                Console.Write("*");
            }
        }
        
        return password.ToString();
    }

    static void DisplayMenu()
    {
        Console.WriteLine("");
        Console.WriteLine("==============================================");
        Console.WriteLine("          Portway Management Console");
        Console.WriteLine("==============================================");
        Console.WriteLine("");
        Console.WriteLine("1. List all existing tokens");
        Console.WriteLine("2. Generate new token");
        Console.WriteLine("3. Revoke token");
        Console.WriteLine("4. Update token scopes");
        Console.WriteLine("5. Update token environments");
        Console.WriteLine("6. Update token expiration");
        Console.WriteLine("7. Rotate token");
        Console.WriteLine("8. Change passphrase");
        Console.WriteLine("");
        Console.WriteLine("0. Exit");
        Console.WriteLine("");
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
            string expiration = token.ExpiresAt?.ToString("yyyy-MM-dd") ?? "Never";
            
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

            Console.WriteLine($"{token.Id,-5} {token.Username,-20} {token.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm,-20} {expiration,-20} {scopes,-15} {environments,-15}");
        }
    }

    static async Task GenerateNewTokenAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("\n=== Generate New Token ===");
        
        Console.Write("Enter username (leave blank for auto-generated UUID): ");
        string? input = Console.ReadLine();
        string username = string.IsNullOrWhiteSpace(input) 
            ? $"user_{Guid.NewGuid().ToString("N")[..8]}" 
            : input;

        Console.Write("Enter allowed scopes (comma-separated, or * for all endpoints): ");
        string scopesInput = Console.ReadLine() ?? "*";
        string scopes = string.IsNullOrWhiteSpace(scopesInput) ? "*" : scopesInput;

        Console.Write("Enter allowed environments (comma-separated, or * for all environments): ");
        string environmentsInput = Console.ReadLine() ?? "*";
        string environments = string.IsNullOrWhiteSpace(environmentsInput) ? "*" : environmentsInput;

        Console.Write("Enter description (optional): ");
        string description = Console.ReadLine() ?? "";

        Console.Write("Enter days until expiration (0 or blank for no expiration): ");
        string? expiresInput = Console.ReadLine();
        int? expiresInDays = null;
        
        if (!string.IsNullOrWhiteSpace(expiresInput) && int.TryParse(expiresInput, out int days) && days > 0)
        {
            expiresInDays = days;
        }

        await GenerateTokenForUserAsync(username, scopes, environments, description, expiresInDays, serviceProvider);
    }

    static async Task GenerateTokenForUserAsync(
        string username, 
        string scopes, 
        string environments,
        string description, 
        int? expiresInDays, 
        IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();

        try
        {
            string token = await tokenService.GenerateTokenAsync(
                username, 
                scopes, 
                environments,
                description, 
                expiresInDays);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\nSuccess: Token generated.");
            Console.ResetColor();
            
            Console.WriteLine("\n--- Token Details ---");
            Console.WriteLine($"Username: {username}");
            Console.WriteLine($"Token: {token}");
            Console.WriteLine($"Scopes: {scopes}");
            Console.WriteLine($"Environments: {environments}");
            
            if (expiresInDays.HasValue)
            {
                DateTime expirationDate = DateTime.Now.AddDays(expiresInDays.Value);
                Console.WriteLine($"Expires: {expirationDate:yyyy-MM-dd HH:mm:ss} ({expiresInDays} days)");
            }
            else
            {
                Console.WriteLine("Expires: Never");
            }
            
            if (!string.IsNullOrWhiteSpace(description))
            {
                Console.WriteLine($"Description: {description}");
            }

            string tokenFolderPath = tokenService.GetTokenFolderPath();
            Console.WriteLine($"Token file: {Path.Combine(tokenFolderPath, $"{username}.txt")}");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nError generating token: {ex.Message}");
            Console.ResetColor();
            Log.Error(ex, "Error generating token for {Username}", username);
        }
    }

    static async Task RevokeTokenAsync(IServiceProvider serviceProvider)
    {
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
        
        var token = await tokenService.GetTokenByIdAsync(tokenId);
        if (token == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Token with ID {tokenId} not found.");
            Console.ResetColor();
            return;
        }
        
        Console.WriteLine($"\nToken to revoke:");
        Console.WriteLine($"  Username: {token.Username}");
        Console.WriteLine($"  Created: {token.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"  Scopes: {token.AllowedScopes}");
        Console.WriteLine($"  Environments: {token.AllowedEnvironments}");
        Console.WriteLine();
        
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("Are you sure you want to revoke this token? (y/n): ");
        Console.ResetColor();
        
        string? response = Console.ReadLine()?.Trim().ToLower();
        if (response != "y" && response != "yes")
        {
            Console.WriteLine("Token revocation cancelled.");
            return;
        }
        
        bool result = await tokenService.RevokeTokenAsync(tokenId);
        if (result)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\nSuccess: Token for {token.Username} has been revoked successfully.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\nFailed to revoke token.");
            Console.ResetColor();
        }
    }

    static async Task UpdateTokenScopesAsync(IServiceProvider serviceProvider)
    {
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
        
        var token = await tokenService.GetTokenByIdAsync(tokenId);
        if (token == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Token with ID {tokenId} not found.");
            Console.ResetColor();
            return;
        }
        
        Console.WriteLine($"\nCurrent scopes for {token.Username}: {token.AllowedScopes}");
        Console.Write("Enter new scopes (comma-separated, or * for all): ");
        
        string? newScopes = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(newScopes))
        {
            Console.WriteLine("Operation cancelled.");
            return;
        }
        
        bool result = await tokenService.UpdateTokenScopesAsync(tokenId, newScopes);
        if (result)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\nSuccess: Scopes updated for {token.Username}.");
            Console.WriteLine($"New scopes: {newScopes}");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\nFailed to update token scopes.");
            Console.ResetColor();
        }
    }

    static async Task UpdateTokenEnvironmentsAsync(IServiceProvider serviceProvider)
    {
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
        
        var token = await tokenService.GetTokenByIdAsync(tokenId);
        if (token == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Token with ID {tokenId} not found.");
            Console.ResetColor();
            return;
        }
        
        Console.WriteLine($"\nCurrent environments for {token.Username}: {token.AllowedEnvironments}");
        Console.Write("Enter new environments (comma-separated, or * for all): ");
        
        string? newEnvironments = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(newEnvironments))
        {
            Console.WriteLine("Operation cancelled.");
            return;
        }
        
        bool result = await tokenService.UpdateTokenEnvironmentsAsync(tokenId, newEnvironments);
        if (result)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\nSuccess: Environments updated for {token.Username}.");
            Console.WriteLine($"New environments: {newEnvironments}");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\nFailed to update token environments.");
            Console.ResetColor();
        }
    }

    static async Task UpdateTokenExpirationAsync(IServiceProvider serviceProvider)
    {
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
        
        var token = await tokenService.GetTokenByIdAsync(tokenId);
        if (token == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Token with ID {tokenId} not found.");
            Console.ResetColor();
            return;
        }
        
        string currentExpiration = token.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never";
        Console.WriteLine($"\nCurrent expiration for {token.Username}: {currentExpiration}");
        Console.Write("Enter days until expiration (0 for no expiration, or blank to cancel): ");
        
        string? input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.WriteLine("Operation cancelled.");
            return;
        }
        
        if (!int.TryParse(input, out int days) || days < 0)
        {
            Console.WriteLine("Invalid input. Operation cancelled.");
            return;
        }
        
        int? daysValid = days <= 0 ? null : days;
        
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
        
        bool result = await tokenService.UpdateTokenExpirationAsync(tokenId, daysValid);
        if (result)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\nSuccess: Token expiration updated for {token.Username}.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\nFailed to update token expiration.");
            Console.ResetColor();
        }
    }

    static async Task RotateTokenAsync(IServiceProvider serviceProvider)
    {
        await ListAllTokensAsync(serviceProvider);

        Console.WriteLine("\n=== Rotate Token ===");
        Console.WriteLine("Token rotation will:");
        Console.WriteLine("  • Revoke the existing token");
        Console.WriteLine("  • Generate a new token with the same permissions");
        Console.WriteLine("  • Update the token file");
        Console.WriteLine("  • Log all operations for audit trail");
        Console.WriteLine();
        
        Console.Write("Enter token ID to rotate (or 0 to cancel): ");
        
        if (!int.TryParse(Console.ReadLine(), out int tokenId) || tokenId <= 0)
        {
            Console.WriteLine("Operation cancelled.");
            return;
        }

        using var scope = serviceProvider.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();
        
        var token = await tokenService.GetTokenByIdAsync(tokenId);
        if (token == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Token with ID {tokenId} not found.");
            Console.ResetColor();
            return;
        }
        
        if (!token.IsActive)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Cannot rotate inactive token for {token.Username}.");
            Console.ResetColor();
            return;
        }
        
        Console.WriteLine($"\nToken to rotate:");
        Console.WriteLine($"  Username: {token.Username}");
        Console.WriteLine($"  Scopes: {token.AllowedScopes}");
        Console.WriteLine($"  Environments: {token.AllowedEnvironments}");
        Console.WriteLine($"  Expires: {token.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never"}");
        Console.WriteLine($"  Created: {token.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine();
        
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("WARNING:");
        Console.WriteLine("   After rotation, the old token will be permanently revoked!");
        Console.ResetColor();
        Console.Write("Confirm token rotation? (y/n): ");
        
        string? response = Console.ReadLine()?.Trim().ToLower();
        if (response != "y" && response != "yes")
        {
            Console.WriteLine("Token rotation cancelled.");
            return;
        }
        
        try
        {
            Console.WriteLine("\nRotating token...");
            
            string newToken = await tokenService.RotateTokenAsync(tokenId);
            
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\nSuccess: Token rotation completed.");
            Console.ResetColor();
            
            Console.WriteLine("\n--- Rotation Results ---");
            Console.WriteLine($"Username: {token.Username}");
            Console.WriteLine($"New Token: {newToken}");
            Console.WriteLine($"Scopes: {token.AllowedScopes}");
            Console.WriteLine($"Environments: {token.AllowedEnvironments}");
            Console.WriteLine($"Expires: {token.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never"}");
            
            string tokenFolderPath = tokenService.GetTokenFolderPath();
            Console.WriteLine($"Token file: {Path.Combine(tokenFolderPath, $"{token.Username}.txt")}");
            
            Console.WriteLine("\nThe old token has been revoked and logged in the audit trail.");
            Console.WriteLine("The new token is now active and ready for use.");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nError rotating token: {ex.Message}");
            Console.ResetColor();
            Log.Error(ex, "Error rotating token for {Username}", token.Username);
        }
    }

    static CommandLineOptions ParseCommandLineOptions(string[] args)
    {
        var options = new CommandLineOptions();
        
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            
            switch (arg.ToLower())
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                    
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
                    
                case "--docker":
                    options.NoAuth = true;
                    break;

                default:
                    if (!arg.StartsWith("-") && string.IsNullOrWhiteSpace(options.Username))
                    {
                        options.Username = args[i];
                    }
                    break;
            }
        }
        
        return options;
    }

    static async Task<TokenGenerator.Classes.AppConfig> LoadConfigurationAsync(CommandLineOptions cliOptions)
    {
        var config = new TokenGenerator.Classes.AppConfig();
        
        string configFilePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        
        if (!File.Exists(configFilePath))
        {
            Log.Information("Configuration file not found at {Path}. Creating default configuration...", configFilePath);
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
            
            Log.Debug("Configuration loaded successfully.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error loading configuration. Using default values.");
        }
        
        if (!Path.IsPathRooted(config.DatabasePath))
        {
            config.DatabasePath = Path.GetFullPath(config.DatabasePath);
        }
        
        string parentDbPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "auth.db"));
        string parentParentDbPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "auth.db"));
        string appDbPath = "";
        
        if (File.Exists(parentParentDbPath))
        {
            appDbPath = parentParentDbPath;
            var parentParentDir = Path.GetDirectoryName(parentParentDbPath);
            
            Log.Information("Found database in parent's parent directory: {DbPath}", parentParentDbPath);
            
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
            
            string parentTokensPath = Path.Combine(parentDir!, "tokens");
            if (Directory.Exists(parentTokensPath))
            {
                config.TokensFolder = parentTokensPath;
                Log.Information("Using tokens folder from parent directory: {TokensFolder}", parentTokensPath);
            }
        }
        
        if (!string.IsNullOrEmpty(appDbPath) && string.IsNullOrEmpty(cliOptions.DatabasePath))
        {
            config.DatabasePath = appDbPath;
        }
        
        return config;
    }
    
    static async Task CreateDefaultConfigAsync(string configFilePath)
    {
        var directory = Path.GetDirectoryName(configFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        var defaultConfig = new TokenGenerator.Classes.AppConfig
        {
            DatabasePath = "../../auth.db",
            TokensFolder = "tokens",
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

    static void ConfigureLogging(bool verbose = false)
    {
        string logsDirectory = Path.Combine(AppContext.BaseDirectory, "log");
        Directory.CreateDirectory(logsDirectory);
        
        var logConfig = new LoggerConfiguration()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: Path.Combine(logsDirectory, "portwaymgt-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .MinimumLevel.Information();

        if (verbose)
        {
            logConfig.MinimumLevel.Debug();
        }

        Log.Logger = logConfig.CreateLogger();
        Log.Debug("PortwayMgt started - logging to {LogsDirectory}", logsDirectory);
    }

    static void DisplayHelp()
    {
        Console.WriteLine("Portway Management Console");
        Console.WriteLine("===============================");
        Console.WriteLine("A utility to manage authentication tokens for Portway API.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  PortwayMgt.exe [options] [username]");
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
        Console.WriteLine("Interactive Features:");
        Console.WriteLine("  • List and manage existing tokens");
        Console.WriteLine("  • Generate new tokens with custom permissions");
        Console.WriteLine("  • Revoke tokens securely");
        Console.WriteLine("  • Update token scopes, environments, and expiration");
        Console.WriteLine("  • Rotate tokens (revoke old + generate new)");
        Console.WriteLine("  • Change management passphrase");
        Console.WriteLine("  • Complete audit trail for all operations");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  PortwayMgt.exe                                    Run in interactive mode");
        Console.WriteLine("  PortwayMgt.exe -d \"C:\\path\\to\\auth.db\"       Use specific database file");
        Console.WriteLine("  PortwayMgt.exe myuser -s \"*\" -e \"prod,dev\"    Generate token with specific permissions");
        Console.WriteLine();
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
}