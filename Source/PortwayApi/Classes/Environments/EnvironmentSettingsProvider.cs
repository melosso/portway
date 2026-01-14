using System.Text.Json;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Serilog;
using PortwayApi.Interfaces;
using Microsoft.Data.SqlClient;
using System.Security;
using System.Security.Cryptography; 
using PortwayApi.Helpers;
using System.Text;

namespace PortwayApi.Classes;

public class EnvironmentSettingsProvider : IEnvironmentSettingsProvider
{
    private readonly string _basePath;
    private readonly string? _keyVaultUri;
    private readonly string? _privateKeyPem;
    private readonly string _certsPath;

    public EnvironmentSettingsProvider()
    {
        // Support both lowercase and uppercase folder names for cross-platform compatibility
        var baseDir = Directory.GetCurrentDirectory();
        _basePath = Directory.Exists(Path.Combine(baseDir, "Environments"))
            ? Path.Combine(baseDir, "Environments")
            : Path.Combine(baseDir, "environments");
        _keyVaultUri = Environment.GetEnvironmentVariable("KEYVAULT_URI");
        _certsPath = Path.Combine(Directory.GetCurrentDirectory(), ".core");
        
        // Ensure .core directory exists and generate keys if needed
        EnsureEncryptionKeysExist();
        
        // Load private key from file
        var privateKeyPath = Path.Combine(_certsPath, "recovery.binlz4");
        
        if (File.Exists(privateKeyPath))
        {
            try
            {
                var encryptedPrivateKey = File.ReadAllText(privateKeyPath);
                var encryptionKey = LoadEncryptionKey();
                _privateKeyPem = DecryptPrivateKey(encryptedPrivateKey, encryptionKey);
                Log.Debug("Loaded and decrypted private key from: {KeyPath}", privateKeyPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load or decrypt private key from {KeyPath}. If you changed PORTWAY_ENCRYPTION_KEY, you must delete the .core folder to regenerate keys.", privateKeyPath);
            }
        }

        // Debug logging
        Log.Debug("Environment settings initialized:");
        Log.Debug("  Local environments path: {BasePath}", _basePath);
        Log.Debug("  Azure Key Vault: {Status}", !string.IsNullOrWhiteSpace(_keyVaultUri) ? _keyVaultUri : "Not configured");
        Log.Debug("  Settings decryption: {Status}", !string.IsNullOrWhiteSpace(_privateKeyPem) ? "Available" : "Not configured");
        
        // Encrypt all environments on startup 
        AutoEncryptAllEnvironmentsOnStartup();
    }

    private void AutoEncryptAllEnvironmentsOnStartup()
    {
        if (string.IsNullOrWhiteSpace(_privateKeyPem))
        {
            Log.Error("Cannot auto-encrypt environments on startup: No private key available");
            return;
        }

        if (!Directory.Exists(_basePath))
        {
            Log.Debug("Environments directory does not exist: {Path}", _basePath);
            return;
        }

        Log.Debug("Scanning all environments for auto-encryption...");

        var environmentDirs = Directory.GetDirectories(_basePath)
            .Select(d => new DirectoryInfo(d).Name)
            .ToList();

        if (!environmentDirs.Any())
        {
            Log.Debug("No environment directories found in {Path}", _basePath);
            return;
        }

        Log.Debug("Found {Count} environment(s): {Environments}", 
            environmentDirs.Count, 
            string.Join(", ", environmentDirs));

        int encryptedCount = 0;
        int alreadyEncryptedCount = 0;
        int errorCount = 0;

        foreach (var env in environmentDirs)
        {
            var settingsPath = Path.Combine(_basePath, env, "settings.json");
            
            if (!File.Exists(settingsPath))
            {
                Log.Debug("Skipping {Env}: settings.json not found", env);
                continue;
            }

            try
            {
                var json = File.ReadAllText(settingsPath);
                var config = JsonSerializer.Deserialize<EnvironmentConfig>(json);

                if (config == null)
                {
                    Log.Error("Failed to deserialize settings.json for environment: {Env}", env);
                    errorCount++;
                    continue;
                }

                var result = AutoEncryptIfNeeded(settingsPath, config, env);
                
                if (result == EncryptionResult.Encrypted)
                    encryptedCount++;
                else if (result == EncryptionResult.AlreadyEncrypted)
                    alreadyEncryptedCount++;
                else
                    errorCount++;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing environment {Env}", env);
                errorCount++;
            }
        }

        Log.Debug("Auto-encryption scan complete: {Encrypted} encrypted, {AlreadyEncrypted} already encrypted, {Errors} errors",
            encryptedCount, alreadyEncryptedCount, errorCount);
    }

    private enum EncryptionResult
    {
        Encrypted,
        AlreadyEncrypted,
        Error
    }

    private void EnsureEncryptionKeysExist()
    {
        var privateKeyPath = Path.Combine(_certsPath, "recovery.binlz4");
        var publicKeyPath = Path.Combine(_certsPath, "snapshot_blob.bin");

        if (File.Exists(privateKeyPath) && File.Exists(publicKeyPath))
        {
            Log.Debug("Encryption keys already exist in {Path}", _certsPath);
            return;
        }

        Log.Debug("Generating new RSA encryption keys...");

        try
        {
            Directory.CreateDirectory(_certsPath);

            if (OperatingSystem.IsWindows())
            {
                var dirInfo = new DirectoryInfo(_certsPath);
                if ((dirInfo.Attributes & FileAttributes.Hidden) == 0)
                {
                    dirInfo.Attributes |= FileAttributes.Hidden;
                }
            }

            using var rsa = RSA.Create(2048);
            
            var privateKeyPem = ExportPrivateKeyPem(rsa);
            var publicKeyPem = ExportPublicKeyPem(rsa);

            var encryptionKey = LoadEncryptionKey();
            var encryptedPrivateKey = EncryptPrivateKey(privateKeyPem, encryptionKey);

            File.WriteAllText(privateKeyPath, encryptedPrivateKey);
            Log.Debug("Saved encrypted private key to: {Path}", privateKeyPath);

            File.WriteAllText(publicKeyPath, publicKeyPem);
            Log.Debug("Saved public key to: {Path}", publicKeyPath);

            Log.Debug("RSA encryption keys generated successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to generate encryption keys");
            throw;
        }
    }

    private static string ExportPrivateKeyPem(RSA rsa)
    {
        var builder = new StringBuilder();
        builder.AppendLine("-----BEGIN PRIVATE KEY-----");
        builder.AppendLine(Convert.ToBase64String(rsa.ExportPkcs8PrivateKey(), Base64FormattingOptions.InsertLineBreaks));
        builder.AppendLine("-----END PRIVATE KEY-----");
        return builder.ToString();
    }

    private static string ExportPublicKeyPem(RSA rsa)
    {
        var builder = new StringBuilder();
        builder.AppendLine("-----BEGIN PUBLIC KEY-----");
        builder.AppendLine(Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo(), Base64FormattingOptions.InsertLineBreaks));
        builder.AppendLine("-----END PUBLIC KEY-----");
        return builder.ToString();
    }

    private static string EncryptPrivateKey(string privateKeyPem, string encryptionKey)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(encryptionKey.PadRight(32).Substring(0, 32));
        aes.GenerateIV();

        using var ms = new MemoryStream();
        ms.Write(aes.IV, 0, aes.IV.Length);

        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs))
        {
            sw.Write(privateKeyPem);
        }

        return Convert.ToBase64String(ms.ToArray());
    }

    public async Task<(string ConnectionString, string ServerName, Dictionary<string, string> Headers)> LoadEnvironmentOrThrowAsync(string env)
    {
        Log.Debug("Loading environment settings for: {Environment}", env);
        
        if (!string.IsNullOrWhiteSpace(_keyVaultUri))
        {
            Log.Debug("Attempting to load from Azure Key Vault...");
            var azure = await TryLoadFromAzureAsync(env);
            if (azure != null)
            {
                Log.Debug("Successfully loaded environment {Env} from Azure Key Vault", env);
                var secureConnectionString = SecureConnectionString(azure.ConnectionString!);
                return (secureConnectionString, azure.ServerName!, azure.Headers);
            }
        }

        Log.Debug("Attempting to load from local JSON files...");
        var local = LoadFromJson(env);
        if (local == null)
        {
            Log.Error("Failed to load environment settings for {Environment}", env);
            throw new InvalidOperationException($"Failed to load environment settings for {env}");
        }
        Log.Debug("Successfully loaded environment {Env} from local settings.json", env);
        var securedLocalConnectionString = SecureConnectionString(local.ConnectionString!);
        return (securedLocalConnectionString, local.ServerName!, local.Headers);
    }

    private async Task<EnvironmentConfig?> TryLoadFromAzureAsync(string env)
    {
        if (string.IsNullOrWhiteSpace(_keyVaultUri))
        {
            Log.Debug("Azure Key Vault not configured.");
            return null;
        }

        try
        {
            Log.Information("Azure Key Vault: Attempting connection to {KeyVaultUri}", _keyVaultUri);
            
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
            var client = new SecretClient(new Uri(_keyVaultUri), credential);
            
            var connectionStringKey = $"{env}-ConnectionString";
            var connectionString = await TryGetSecretValue(client, connectionStringKey);
            
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Log.Warning("Required secret {SecretName} not found in Azure Key Vault", connectionStringKey);
                return null;
            }

            var serverNameKey = $"{env}-ServerName";
            var serverName = await TryGetSecretValue(client, serverNameKey) ?? ".";

            var headersKey = $"{env}-Headers";
            var headersJson = await TryGetSecretValue(client, headersKey);
            
            var headers = new Dictionary<string, string>();
            
            if (!string.IsNullOrWhiteSpace(headersJson))
            {
                try
                {
                    headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson) ?? new Dictionary<string, string>();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error parsing headers from Azure Key Vault");
                }
            }
            
            if (!headers.ContainsKey("DatabaseName"))
                headers["DatabaseName"] = env;
                
            if (!headers.ContainsKey("ServerName"))
                headers["ServerName"] = serverName;
            
            return new EnvironmentConfig 
            { 
                ConnectionString = connectionString, 
                ServerName = serverName,
                Headers = headers
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Azure Key Vault access failed: {ErrorType} - {ErrorMessage}", 
                ex.GetType().Name, ex.Message);
            return null;
        }
    }

    private async Task<string?> TryGetSecretValue(SecretClient client, string secretName)
    {
        try
        {
            var secretResponse = await client.GetSecretAsync(secretName);
            return secretResponse?.Value?.Value;
        }
        catch (Azure.RequestFailedException rfEx)
        {
            if (rfEx.Status == 404)
            {
                Log.Debug("Secret {SecretName} not found (404)", secretName);
            }
            return null;
        }
        catch (Exception ex)
        {
            Log.Debug("Exception retrieving secret {SecretName}: {ErrorType} - {ErrorMessage}", 
                secretName, ex.GetType().Name, ex.Message);
            return null;
        }
    }

    private EnvironmentConfig? LoadFromJson(string env)
    {
        var settingsPath = Path.Combine(_basePath, env, "settings.json");
        Log.Debug("Attempting to load from file: {FilePath}", settingsPath);

        if (!File.Exists(settingsPath))
        {
            Log.Error("settings.json not found for environment: {Environment}, path: {FilePath}", env, settingsPath);
            throw new FileNotFoundException($"settings.json not found for environment: {env}", settingsPath);
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            var config = JsonSerializer.Deserialize<EnvironmentConfig>(json);
                     
            if (config == null)
            {
                Log.Error("Failed to deserialize JSON from {FilePath}", settingsPath);
                throw new InvalidOperationException($"Application connection configuration could not be loaded for the current environment.");
            }

            if (string.IsNullOrWhiteSpace(config.ConnectionString))
            {
                Log.Error("Missing ConnectionString in settings.json for environment: {Environment}", env);
                throw new InvalidOperationException($"Application connection settings could not be loaded for the current environment.");
            }

            // Decrypt ConnectionString if encrypted
            if (SettingsEncryptionHelper.IsEncrypted(config.ConnectionString))
            {
                if (string.IsNullOrWhiteSpace(_privateKeyPem))
                {
                    Log.Error("ConnectionString is encrypted but no private key available for environment: {Environment}", env);
                    throw new InvalidOperationException($"Application configuration could not be decrypted for the current environment.");
                }
                
                config.ConnectionString = SettingsEncryptionHelper.Decrypt(config.ConnectionString, _privateKeyPem);
            }

            // Decrypt encrypted headers
            if (config.Headers != null)
            {
                var decryptedHeaders = new Dictionary<string, string>();
                foreach (var header in config.Headers)
                {
                    if (SettingsEncryptionHelper.IsEncrypted(header.Value))
                    {
                        if (string.IsNullOrWhiteSpace(_privateKeyPem))
                        {
                            Log.Error("Header '{HeaderKey}' is encrypted but no private key available for environment: {Environment}", header.Key, env);
                            throw new InvalidOperationException($"Application configuration could not be decrypted for the current environment.");
                        }
                        
                        decryptedHeaders[header.Key] = SettingsEncryptionHelper.Decrypt(header.Value, _privateKeyPem);
                    }
                    else
                    {
                        decryptedHeaders[header.Key] = header.Value;
                    }
                }
                config.Headers = decryptedHeaders;
            }

            config.Headers ??= new Dictionary<string, string>();
            
            if (!config.Headers.ContainsKey("DatabaseName"))
                config.Headers["DatabaseName"] = env;
                
            if (!config.Headers.ContainsKey("ServerName"))
                config.Headers["ServerName"] = config.ServerName ?? Environment.MachineName;
                
            return config;
        }
        catch (Exception ex) when (ex is not FileNotFoundException && ex is not InvalidOperationException)
        {
            Log.Error(ex, "Error reading or parsing settings.json for environment: {Environment}", env);
            throw;
        }
    }

    private string SecureConnectionString(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            
            bool hasUserID = !string.IsNullOrEmpty(builder.UserID);
            bool hasPassword = !string.IsNullOrEmpty(builder.Password);
            
            if (hasUserID || hasPassword)
            {
                Log.Warning("Connection string contains hardcoded credentials. Consider encrypting the connection string.");
                
                if (hasPassword)
                {
                    string originalPassword = builder.Password;
                    var securePassword = new SecureString();
                    foreach (char c in originalPassword)
                    {
                        securePassword.AppendChar(c);
                    }
                    securePassword.MakeReadOnly();
                    builder.Password = "";
                }
                
                var masked = MaskConnectionString(connectionString);
                Log.Debug("Using connection string: {ConnectionString}", masked);
                
                return connectionString;
            }
            
            return connectionString;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error while securing connection string: {ErrorMessage}", ex.Message);
            return connectionString;
        }
    }

    private string MaskConnectionString(string connectionString)
    {
        try
        {
            var parts = connectionString.Split(';')
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(part => {
                    var keyValue = part.Split('=', 2);
                    if (keyValue.Length != 2) return part;
                    
                    var key = keyValue[0].Trim();
                    var value = keyValue[1].Trim();
                    
                    if (key.Contains("password", StringComparison.OrdinalIgnoreCase) || 
                        key.Contains("pwd", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"{key}=***MASKED***";
                    }
                    
                    if (key.Contains("server", StringComparison.OrdinalIgnoreCase) || 
                        key.Contains("data source", StringComparison.OrdinalIgnoreCase) ||
                        key.Contains("database", StringComparison.OrdinalIgnoreCase) ||
                        key.Contains("initial catalog", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"{key}={value}";
                    }
                    
                    return $"{key}=***";
                });
                
            return string.Join("; ", parts);
        }
        catch
        {
            return "ConnectionString parsing failed";
        }
    }

    private EncryptionResult AutoEncryptIfNeeded(string settingsPath, EnvironmentConfig config, string envName)
    {
        bool needsSave = false;
        bool alreadyEncrypted = true;

        // 1. Validate and encrypt ConnectionString if needed
        if (!string.IsNullOrWhiteSpace(config.ConnectionString))
        {
            if (!SettingsEncryptionHelper.IsEncrypted(config.ConnectionString))
            {
                alreadyEncrypted = false;
                
                if (IsValidMssqlConnectionString(config.ConnectionString))
                {
                    config.ConnectionString = SettingsEncryptionHelper.Encrypt(config.ConnectionString);
                    needsSave = true;
                    Log.Debug("Auto-encrypted ConnectionString for environment: {Env}", envName);
                }
                else
                {
                    Log.Error("Invalid MSSQL connection string format in environment '{Env}' - skipping encryption. Connection string must have DataSource, InitialCatalog, and either IntegratedSecurity=true or valid UserID/Password.", envName);
                    return EncryptionResult.Error;
                }
            }
        }

        // 2. Encrypt sensitive headers
        if (config.Headers != null)
        {
            var headersToEncrypt = config.Headers
                .Where(h => !SettingsEncryptionHelper.IsEncrypted(h.Value) && IsSensitiveHeader(h.Key))
                .ToList();

            if (headersToEncrypt.Any())
            {
                alreadyEncrypted = false;
            }

            foreach (var header in headersToEncrypt)
            {
                config.Headers[header.Key] = SettingsEncryptionHelper.Encrypt(header.Value);
                needsSave = true;
                Log.Debug("Auto-encrypted header '{HeaderKey}' for environment: {Env}", header.Key, envName);
            }
        }

        // 3. Save file if any changes were made
        if (needsSave)
        {
            SaveEncryptedConfig(settingsPath, config);
            Log.Debug("Auto-encryption complete for environment: {Env}", envName);
            return EncryptionResult.Encrypted;
        }
        
        return alreadyEncrypted ? EncryptionResult.AlreadyEncrypted : EncryptionResult.Error;
    }

    private bool IsValidMssqlConnectionString(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            
            if (string.IsNullOrWhiteSpace(builder.DataSource))
                return false;
                
            if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
                return false;
            
            if (!builder.IntegratedSecurity && 
                (string.IsNullOrWhiteSpace(builder.UserID) || 
                 string.IsNullOrWhiteSpace(builder.Password)))
                return false;
            
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsSensitiveHeader(string headerName)
    {
        string[] sensitivePatterns = 
        {
            "password", "secret", "token", "key", "auth", 
            "credential", "signature", "hmac", "bearer"
        };
        
        var lowerName = headerName.ToLowerInvariant();
        return sensitivePatterns.Any(pattern => lowerName.Contains(pattern));
    }

    private void SaveEncryptedConfig(string settingsPath, EnvironmentConfig config)
    {
        try
        {
            // Check if file is read-only before attempting to write
            if (File.Exists(settingsPath))
            {
                var fileInfo = new FileInfo(settingsPath);
                if (fileInfo.IsReadOnly)
                {
                    Log.Warning("File {Path} is marked as read-only. Attempting to remove read-only attribute...", settingsPath);
                    try
                    {
                        fileInfo.IsReadOnly = false;
                        Log.Debug("Successfully removed read-only attribute from {Path}", settingsPath);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to remove read-only attribute from {Path}. Please manually remove the read-only flag and restart the application.", settingsPath);
                        throw;
                    }
                }
            }

            var jsonOptions = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            
            var json = JsonSerializer.Serialize(config, jsonOptions);
            File.WriteAllText(settingsPath, json);
            Log.Debug("Successfully saved encrypted configuration to {Path}", settingsPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Error(ex, "Access denied when saving to {Path}. Possible causes: 1) File is read-only, 2) Insufficient permissions, 3) File is locked by another process. Please check file permissions and try again.", settingsPath);
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save encrypted configuration to {Path}", settingsPath);
            throw;
        }
    }

    private static string LoadEncryptionKey()
    {
        var envKey = Environment.GetEnvironmentVariable("PORTWAY_ENCRYPTION_KEY", EnvironmentVariableTarget.Machine);
        if (!string.IsNullOrWhiteSpace(envKey))
            return envKey;

        envKey = Environment.GetEnvironmentVariable("PORTWAY_ENCRYPTION_KEY", EnvironmentVariableTarget.Process);
        if (!string.IsNullOrWhiteSpace(envKey))
            return envKey;

        var projectRoot = Directory.GetCurrentDirectory();
        var envFilePath = Path.Combine(projectRoot, ".env");
        
        if (File.Exists(envFilePath))
        {
            try
            {
                var envLines = File.ReadAllLines(envFilePath);
                foreach (var line in envLines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("#") || string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    var parts = trimmed.Split('=', 2);
                    if (parts.Length == 2 && parts[0].Trim() == "PORTWAY_ENCRYPTION_KEY")
                    {
                        var key = parts[1].Trim().Trim('"', '\'');
                        if (!string.IsNullOrWhiteSpace(key))
                            return key;
                    }
                }
            }
            catch { }
        }

        return "$XTSI5gTEf1hawq3G2uOdWTsFUrgZ6mkCBGrdr0fsRTegXwis68HxGEoCsIBpgbPl5swwY9BQ0qiXG6CaeEPJzp3SPyGebl0ZyHL3jLACKIuSw7G1ufAZ5XATtetKatH0sr#";
    }

    private static string DecryptPrivateKey(string encrypted, string encryptionKey)
    {
        var bytes = Convert.FromBase64String(encrypted);
        using var ms = new MemoryStream(bytes);
        var iv = new byte[16];
        ms.Read(iv, 0, 16);
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(encryptionKey.PadRight(32).Substring(0, 32));
        aes.IV = iv;
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var sr = new StreamReader(cs);
        return sr.ReadToEnd();
    }

    private class EnvironmentConfig
    {
        public string? ConnectionString { get; set; }
        public string? ServerName { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
    }
}