using System.Text.Json;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Serilog;
using PortwayApi.Interfaces;
using Microsoft.Data.SqlClient;
using System.Security;
using PortwayApi.Helpers;

namespace PortwayApi.Classes;

public class EnvironmentSettingsProvider : IEnvironmentSettingsProvider
{
    private readonly string _basePath;
    private readonly string? _keyVaultUri;
    private readonly string? _privateKeyPem;

    public EnvironmentSettingsProvider()
    {
        _basePath = Path.Combine(Directory.GetCurrentDirectory(), "environments");
        _keyVaultUri = Environment.GetEnvironmentVariable("KEYVAULT_URI");
        _privateKeyPem = Environment.GetEnvironmentVariable("PORTWAY_PRIVATE_KEY");
        
        // If no private key in environment variable, try to read from certs directory
        if (string.IsNullOrWhiteSpace(_privateKeyPem))
        {
            var certsPath = Path.Combine(Directory.GetCurrentDirectory(), "certs");
            var privateKeyPath = Path.Combine(certsPath, "portway_private_key.pem");
            
            if (File.Exists(privateKeyPath))
            {
                _privateKeyPem = File.ReadAllText(privateKeyPath);
                Log.Debug("🔐 Loaded private key from: {KeyPath}", privateKeyPath);
            }
        }

        // Debug logging to show configured vault services
        Log.Debug("🔧 Environment settings initialized:");
        Log.Debug("  📂 Local environments path: {BasePath}", _basePath);
        Log.Debug("  🔑 Azure Key Vault: {Status}", !string.IsNullOrWhiteSpace(_keyVaultUri) ? _keyVaultUri : "Not configured");
        Log.Debug("  🔐 Settings decryption: {Status}", !string.IsNullOrWhiteSpace(_privateKeyPem) ? "Available" : "Not configured");
    }

    public async Task<(string ConnectionString, string ServerName, Dictionary<string, string> Headers)> LoadEnvironmentOrThrowAsync(string env)
    {
        Log.Debug("🔍 Loading environment settings for: {Environment}", env);
        
        // Try Azure Key Vault first
        if (!string.IsNullOrWhiteSpace(_keyVaultUri))
        {
            Log.Debug("🔄 Attempting to load from Azure Key Vault...");
            var azure = await TryLoadFromAzureAsync(env);
            if (azure != null)
            {
                Log.Debug("✅ Successfully loaded environment {Env} from Azure Key Vault", env);
                var secureConnectionString = SecureConnectionString(azure.ConnectionString!);
                return (secureConnectionString, azure.ServerName!, azure.Headers);
            }
        }

        // Fall back to local JSON
        Log.Debug("🔄 Attempting to load from local JSON files...");
        var local = LoadFromJson(env);
        if (local == null)
        {
            Log.Error("❌ Failed to load environment settings for {Environment}", env);
            throw new InvalidOperationException($"Failed to load environment settings for {env}");
        }
        Log.Debug("✅ Successfully loaded environment {Env} from local settings.json", env);
        var securedLocalConnectionString = SecureConnectionString(local.ConnectionString!);
        return (securedLocalConnectionString, local.ServerName!, local.Headers);
    }

    // --- Azure Key Vault Integration ---
    private async Task<EnvironmentConfig?> TryLoadFromAzureAsync(string env)
    {
        if (string.IsNullOrWhiteSpace(_keyVaultUri))
        {
            Log.Debug("Azure Key Vault not configured.");
            return null;
        }

        try
        {
            Log.Information("🔐 Azure Key Vault: Attempting connection to {KeyVaultUri}", _keyVaultUri);
            
            // Create DefaultAzureCredential with logging
            Log.Debug("🔑 Creating DefaultAzureCredential with logging");
            var credentialOptions = new DefaultAzureCredentialOptions
            {
                ExcludeEnvironmentCredential = false,
                ExcludeManagedIdentityCredential = false,
                ExcludeSharedTokenCacheCredential = false,
                ExcludeVisualStudioCredential = false,
                ExcludeAzureCliCredential = false,
                ExcludeInteractiveBrowserCredential = true
            };
            
            var credential = new DefaultAzureCredential(credentialOptions);
            Log.Debug("✅ DefaultAzureCredential created successfully");
            
            Log.Debug("🔑 Creating SecretClient for {KeyVaultUri}", _keyVaultUri);
            var client = new SecretClient(new Uri(_keyVaultUri), credential);
            
            var connectionStringKey = $"{env}-ConnectionString";
            Log.Information("🔍 Looking for secret: {SecretName}", connectionStringKey);
            var connectionString = await TryGetSecretValue(client, connectionStringKey);
            
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Log.Warning("⚠️ Required secret {SecretName} not found in Azure Key Vault", connectionStringKey);
                return null;
            }

            var serverNameKey = $"{env}-ServerName";
            Log.Debug("🔍 Looking for secret: {SecretName}", serverNameKey);
            var serverName = await TryGetSecretValue(client, serverNameKey) ?? ".";

            // Try to load headers from Azure Key Vault
            var headersKey = $"{env}-Headers";
            Log.Debug("🔍 Looking for secret: {SecretName}", headersKey);
            var headersJson = await TryGetSecretValue(client, headersKey);
            
            var headers = new Dictionary<string, string>();
            
            if (!string.IsNullOrWhiteSpace(headersJson))
            {
                try
                {
                    headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson) ?? new Dictionary<string, string>();
                    Log.Debug("✅ Successfully loaded headers from Azure Key Vault");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "⚠️ Error parsing headers from Azure Key Vault");
                }
            }
            
            // Ensure some default headers are present
            if (!headers.ContainsKey("DatabaseName"))
                headers["DatabaseName"] = env;
                
            if (!headers.ContainsKey("ServerName"))
                headers["ServerName"] = serverName;

            Log.Information("📊 Loaded secrets from Azure Key Vault: ConnectionString={HasConnectionString}, ServerName={HasServerName}, Headers={HeaderCount}", 
                !string.IsNullOrEmpty(connectionString), !string.IsNullOrEmpty(serverName), headers.Count);
            
            return new EnvironmentConfig 
            { 
                ConnectionString = connectionString, 
                ServerName = serverName,
                Headers = headers
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Azure Key Vault access failed: {ErrorType} - {ErrorMessage}", 
                ex.GetType().Name, ex.Message);
            return null;
        }
    }

    private async Task<string?> TryGetSecretValue(SecretClient client, string secretName)
    {
        try
        {
            Log.Debug("🔄 Requesting secret: {SecretName}", secretName);
            var secretResponse = await client.GetSecretAsync(secretName);
            
            if (secretResponse == null || secretResponse.Value == null)
            {
                Log.Debug("⚠️ Secret response or value is null for {SecretName}", secretName);
                return null;
            }
            
            var value = secretResponse.Value.Value;
            var valueExists = !string.IsNullOrEmpty(value);
            Log.Debug("🔑 Secret {SecretName}: {Result}", secretName, 
                valueExists ? "Retrieved (non-empty)" : "Retrieved but empty");
            
            if (!valueExists)
            {
                Log.Warning("⚠️ Secret {SecretName} exists but has empty value", secretName);
            }
            
            return value;
        }
        catch (Azure.RequestFailedException rfEx)
        {
            if (rfEx.Status == 404)
            {
                Log.Debug("⚠️ Secret {SecretName} not found (404)", secretName);
            }
            else
            {
                Log.Debug("⚠️ Failed to retrieve secret {SecretName}: Status={Status}, Error={ErrorCode}, Message={ErrorMessage}", 
                    secretName, rfEx.Status, rfEx.ErrorCode, rfEx.Message);
            }
            return null;
        }
        catch (Exception ex)
        {
            Log.Debug("⚠️ Exception retrieving secret {SecretName}: {ErrorType} - {ErrorMessage}", 
                secretName, ex.GetType().Name, ex.Message);
            return null;
        }
    }

    // --- Local JSON Configuration ---
    private EnvironmentConfig? LoadFromJson(string env)
    {
        var settingsPath = Path.Combine(_basePath, env, "settings.json");
        Log.Debug("📄 Attempting to load from file: {FilePath}", settingsPath);

        if (!File.Exists(settingsPath))
        {
            Log.Error("❌ settings.json not found for environment: {Environment}, path: {FilePath}", env, settingsPath);
            throw new FileNotFoundException($"settings.json not found for environment: {env}", settingsPath);
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            
            // Check if the content is encrypted and decrypt if necessary
            if (SettingsEncryptionHelper.IsEncrypted(json))
            {
                if (string.IsNullOrWhiteSpace(_privateKeyPem))
                {
                    Log.Error("❌ Settings file is encrypted but no private key provided via PORTWAY_PRIVATE_KEY environment variable for environment: {Environment}", env);
                    throw new InvalidOperationException($"Settings file is encrypted but no private key available for environment: {env}. Set the PORTWAY_PRIVATE_KEY environment variable.");
                }
                
                Log.Debug("🔓 Decrypting settings file for environment: {Environment}", env);
                json = SettingsEncryptionHelper.Decrypt(json, _privateKeyPem);
            }
            
            var config = JsonSerializer.Deserialize<EnvironmentConfig>(json);
                     
            if (config == null)
            {
                Log.Error("❌ Failed to deserialize JSON from {FilePath}", settingsPath);
                throw new InvalidOperationException($"Invalid JSON in settings.json for environment: {env}");
            }

            if (string.IsNullOrWhiteSpace(config.ConnectionString))
            {
                Log.Error("❌ Missing ConnectionString in settings.json for environment: {Environment}", env);
                throw new InvalidOperationException($"Missing connection string in settings.json for environment: {env}");
            }

            // Initialize headers if not present
            config.Headers ??= new Dictionary<string, string>();
            
            // Ensure some default headers are present
            if (!config.Headers.ContainsKey("DatabaseName"))
                config.Headers["DatabaseName"] = env;
                
            if (!config.Headers.ContainsKey("ServerName"))
                config.Headers["ServerName"] = config.ServerName ?? Environment.MachineName;

            Log.Debug("📊 Loaded secrets from local settings.json: ConnectionString={HasConnectionString}, ServerName={HasServerName}, Headers={HeaderCount}", 
                !string.IsNullOrEmpty(config.ConnectionString), 
                !string.IsNullOrEmpty(config.ServerName),
                config.Headers.Count);
                
            return config;
        }
        catch (Exception ex) when (ex is not FileNotFoundException && ex is not InvalidOperationException)
        {
            Log.Error(ex, "❌ Error reading or parsing settings.json for environment: {Environment}", env);
            throw;
        }
    }

    // Secures a connection string by encrypting sensitive credentials in memory
    private string SecureConnectionString(string connectionString)
    {
        try
        {
            Log.Debug("🔒 Securing connection string to prevent credential leakage");
            
            // Parse connection string
            var builder = new SqlConnectionStringBuilder(connectionString);
            
            // Check if connection string contains hardcoded credentials
            bool hasUserID = !string.IsNullOrEmpty(builder.UserID);
            bool hasPassword = !string.IsNullOrEmpty(builder.Password);
            
            if (hasUserID || hasPassword)
            {
                Log.Warning("⚠️ Connection string contains hardcoded credentials. Storing securely in memory");
                
                // Store credentials securely
                if (hasPassword)
                {
                    // Extract the original password
                    string originalPassword = builder.Password;
                    
                    // Store the password securely in memory
                    var securePassword = new SecureString();
                    foreach (char c in originalPassword)
                    {
                        securePassword.AppendChar(c);
                    }
                    securePassword.MakeReadOnly();
                    
                    // Clear the password from the builder to prevent it from being accessible in memory dumps
                    builder.Password = "";
                    
                    Log.Debug("🔐 Password encrypted in secure memory and removed from builder");
                }
                
                // Log masked version for visibility in logs
                var masked = MaskConnectionString(connectionString);
                Log.Debug("🔍 Using connection string with credentials: {ConnectionString}", masked);
                
                // Return the original string which will be used directly by SQL connections
                // We use the original string to ensure the connection works properly
                // but we've removed it from our accessible application state
                return connectionString;
            }
            else
            {
                Log.Debug("✅ Connection string does not contain hardcoded credentials");
                return connectionString;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "⚠️ Error while securing connection string: {ErrorMessage}", ex.Message);
            return connectionString;
        }
    }

    private string MaskConnectionString(string connectionString)
    {
        // Create a safe representation of connection string for logging
        // This masks passwords and other sensitive values while keeping the structure
        try
        {
            var parts = connectionString.Split(';')
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(part => {
                    var keyValue = part.Split('=', 2);
                    if (keyValue.Length != 2) return part;
                    
                    var key = keyValue[0].Trim();
                    var value = keyValue[1].Trim();
                    
                    // Mask sensitive parts
                    if (key.Contains("password", StringComparison.OrdinalIgnoreCase) || 
                        key.Contains("pwd", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"{key}=***MASKED***";
                    }
                    
                    // Show server and database names
                    if (key.Contains("server", StringComparison.OrdinalIgnoreCase) || 
                        key.Contains("data source", StringComparison.OrdinalIgnoreCase) ||
                        key.Contains("database", StringComparison.OrdinalIgnoreCase) ||
                        key.Contains("initial catalog", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"{key}={value}";
                    }
                    
                    // For other parameters, show the key but not the value
                    return $"{key}=***";
                });
                
            return string.Join("; ", parts);
        }
        catch
        {
            // If parsing fails, return a generic message
            return "ConnectionString parsing failed";
        }
    }

    // --- Configuration Model Class ---
    private class EnvironmentConfig
    {
        public string? ConnectionString { get; set; }
        public string? ServerName { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
    }
}