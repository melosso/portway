using System.Text.Json;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Serilog;
using PortwayApi.Interfaces;
using System.Security.Cryptography;
using PortwayApi.Helpers;

namespace PortwayApi.Classes;

public sealed class EnvironmentSettingsProvider : IEnvironmentSettingsProvider
{
    private static readonly JsonSerializerOptions SettingsFileJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _basePath;
    private readonly string? _keyVaultUri;
    private readonly string? _privateKeyPem;
    private readonly string _certsPath;

    // RSA decryption costs ~2 ms per field; file stamp invalidates entries
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Stamp, long Length, EnvironmentConfig Config)> _jsonCache = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Stamp, long Length)> _badStamps = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, KeyVaultEntry> _keyVaultCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string Env, string Name), byte> _missingLogged = new();
    private readonly SecretClient? _secretClient;
    private readonly TimeProvider _time;

    internal static readonly TimeSpan KeyVaultValueLifetime = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan KeyVaultMissLifetime = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan KeyVaultLastKnownLifetime = TimeSpan.FromHours(1);

    private enum KeyVaultState { Found, NotFound, Disabled, Unavailable }

    private sealed record KeyVaultEntry(KeyVaultState State, string? Value, DateTimeOffset FetchedAt, DateTimeOffset LastKnownAt);

    public EnvironmentSettingsProvider() : this(null, TimeProvider.System)
    {
    }

    internal EnvironmentSettingsProvider(SecretClient? secretClient, TimeProvider timeProvider)
    {
        _time = timeProvider;
        // Support both lowercase and uppercase folder names for cross-platform compatibility
        var baseDir = Directory.GetCurrentDirectory();
        _basePath = Path.Combine(baseDir, "environments");
        _keyVaultUri = EnvAliases.GetDirect("PORTWAY_KEYVAULT_URI");
        _certsPath = Path.Combine(Directory.GetCurrentDirectory(), ".core");
        _secretClient = secretClient ?? CreateSecretClient(_keyVaultUri);

        try
        {
            // Ensure .core directory exists and generate keys if needed
            EnsureEncryptionKeysExist();

            // Load private key from file
            var privateKeyPath = Path.Combine(_certsPath, "recovery.binlz4");

            if (File.Exists(privateKeyPath))
            {
                try
                {
                    var encryptedPrivateKey = File.ReadAllText(privateKeyPath);
                    var encryptionKey = SettingsEncryptionHelper.LoadEncryptionKey();
                    _privateKeyPem = SettingsEncryptionHelper.DecryptPrivateKey(encryptedPrivateKey, encryptionKey);
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
            WarnOnUnscopedAuthentication();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Critical error during EnvironmentSettingsProvider initialization");
        }
    }

    private void AutoEncryptAllEnvironmentsOnStartup()
    {
        if (string.IsNullOrWhiteSpace(_privateKeyPem))
        {
            Log.Error("Cannot encrypt environments on startup: No private key available");
            return;
        }

        if (!Directory.Exists(_basePath))
        {
            Log.Debug("Environments directory does not exist: {Path}", _basePath);
            return;
        }

        Log.Debug("Scanning all environments for values to encrypt");

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

        Log.Debug("Encryption scan complete: {Encrypted} encrypted, {AlreadyEncrypted} already encrypted, {Errors} errors",
            encryptedCount, alreadyEncryptedCount, errorCount);
    }

    internal static bool HasUnscopedMethods(AuthenticationSettings? settings) =>
        settings is { Enabled: true, Methods: { } methods } && methods.Any(m => m.AllowedEndpoints is null);

    private void WarnOnUnscopedAuthentication()
    {
        if (!Directory.Exists(_basePath))
            return;

        foreach (var settingsPath in Directory.GetDirectories(_basePath).Select(d => Path.Combine(d, "settings.json")).Where(File.Exists))
        {
            try
            {
                if (HasUnscopedMethods(JsonSerializer.Deserialize<EnvironmentConfig>(File.ReadAllText(settingsPath))?.Authentication))
                    Log.Warning("Environment {Environment} has an authentication method without AllowedEndpoints; it grants every endpoint in the environment",
                        new DirectoryInfo(Path.GetDirectoryName(settingsPath)!).Name);
            }
            catch (JsonException)
            {
                // reported when the environment loads
            }
        }
    }

    public void EncryptEnvironmentIfNeeded(string envName)
    {
        if (string.IsNullOrWhiteSpace(_privateKeyPem))
        {
            Log.Error("Cannot encrypt environment '{Env}' on file change: no private key available", envName);
            return;
        }

        var settingsPath = Path.Combine(_basePath, envName, "settings.json");
        if (!File.Exists(settingsPath)) return;

        try
        {
            var json = ReadSettingsWhenWritable(settingsPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                Log.Debug("Skipping encryption for '{Env}': settings.json was empty or locked while being written", envName);
                return;
            }

            var config = JsonSerializer.Deserialize<EnvironmentConfig>(json);
            if (config == null) return;
            AutoEncryptIfNeeded(settingsPath, config, envName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error encrypting environment '{Env}' after file change", envName);
        }
    }

    // The watcher fires as soon as an editor truncates the file, so retry briefly until content lands
    private static string ReadSettingsWhenWritable(string path)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                var json = File.ReadAllText(path);
                if (!string.IsNullOrWhiteSpace(json)) return json;
            }
            catch (IOException)
            {
                // File still locked by the writer
            }

            Thread.Sleep(100);
        }

        return string.Empty;
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

        // Warn if any environment settings are already encrypted, new keys will orphan them
        if (Directory.Exists(_basePath))
        {
            var encryptedSettings = Directory.GetDirectories(_basePath)
                .Select(d => Path.Combine(d, "settings.json"))
                .Where(File.Exists)
                .Where(p =>
                {
                    try { return SettingsEncryptionHelper.IsEncrypted(File.ReadAllText(p)); }
                    catch { return false; }
                })
                .Select(p => new DirectoryInfo(Path.GetDirectoryName(p)!).Name)
                .ToList();

            if (encryptedSettings.Count > 0)
            {
                Log.Warning(
                    "New RSA encryption keys are being generated, but the following environments have settings.json files " +
                    "already encrypted with the OLD key: {Environments}. " +
                    "These environments will FAIL to decrypt until their settings.json files are replaced with unencrypted versions. " +
                    "To recover: restore the original plaintext connection strings in each settings.json, then restart.",
                    string.Join(", ", encryptedSettings));
            }
        }

        Log.Debug("Generating new RSA encryption keys...");

        try
        {
            HiddenDirectoryHelper.Ensure(_certsPath);

            using var rsa = RSA.Create(2048);

            var privateKeyPem = rsa.ExportPkcs8PrivateKeyPem();
            var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();

            var encryptionKey = SettingsEncryptionHelper.LoadEncryptionKey();
            var encryptedPrivateKey = SettingsEncryptionHelper.EncryptPrivateKey(privateKeyPem, encryptionKey);

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

    public async Task<(string ConnectionString, string ServerName, Dictionary<string, string> Headers)> LoadEnvironmentOrThrowAsync(string env)
    {
        Log.Debug("Loading environment settings for: {Environment}", env);

        if (_secretClient is not null)
        {
            Log.Debug("Attempting to load from Azure Key Vault...");
            var azure = await TryLoadFromAzureAsync(env);
            if (azure != null)
            {
                Log.Debug("Successfully loaded environment {Env} from Azure Key Vault", env);
                var secureConnectionString = SecureConnectionString(azure.ConnectionString!);
                return (await this.ResolveOrThrowAsync(env, secureConnectionString, SecretReference.ResolveConnectionString), azure.ServerName!, azure.Headers);
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

        var securedLocalConnectionString = string.IsNullOrWhiteSpace(local.ConnectionString)
            ? string.Empty
            : await this.ResolveOrThrowAsync(env, SecureConnectionString(local.ConnectionString!), SecretReference.ResolveConnectionString);
        return (securedLocalConnectionString, local.ServerName ?? string.Empty, local.Headers);
    }

    public Task<EnvironmentConfig?> GetEnvironmentConfigAsync(string env) =>
        Task.FromResult(File.Exists(Path.Combine(_basePath, env, "settings.json")) ? LoadFromJson(env) : null);

    public async Task<string?> GetSecretAsync(string env, string name, CancellationToken cancellationToken = default)
    {
        var value = await LookupSecretAsync(env, name, cancellationToken);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (_missingLogged.TryAdd((env, name.ToUpperInvariant()), 0))
                Log.Error("Secret {SecretName} has no value in environment {Environment}", name, env);
            return null;
        }

        _missingLogged.TryRemove((env, name.ToUpperInvariant()), out _);
        return value;
    }

    private async Task<string?> LookupSecretAsync(string env, string name, CancellationToken cancellationToken)
    {
        if (_secretClient is not null)
        {
            var entry = await GetKeyVaultValueAsync($"{env}-Secret-{name}", cancellationToken);
            if (entry.State == KeyVaultState.Found)
                return entry.Value;
            if (entry.State != KeyVaultState.NotFound)
                return null;
        }

        var config = await GetEnvironmentConfigAsync(env);
        return config?.Secrets is { } secrets && secrets.TryGetValue(name, out var value) ? value : null;
    }

    private static SecretClient? CreateSecretClient(string? keyVaultUri)
    {
        if (string.IsNullOrWhiteSpace(keyVaultUri))
            return null;

        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeInteractiveBrowserCredential = true });
        return new SecretClient(new Uri(keyVaultUri), credential);
    }

    private async Task<KeyVaultEntry> GetKeyVaultValueAsync(string secretName, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        _keyVaultCache.TryGetValue(secretName, out var cached);
        if (cached is not null && now - cached.FetchedAt < (cached.State == KeyVaultState.Found ? KeyVaultValueLifetime : KeyVaultMissLifetime)
            && (cached.State != KeyVaultState.Found || now - cached.LastKnownAt <= KeyVaultLastKnownLifetime))
            return cached;

        KeyVaultEntry entry;
        try
        {
            var response = await _secretClient!.GetSecretAsync(secretName, null, cancellationToken);
            entry = new KeyVaultEntry(KeyVaultState.Found, response.Value.Value, now, now);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            entry = new KeyVaultEntry(KeyVaultState.NotFound, null, now, now);
        }
        catch (Azure.RequestFailedException ex) when (ex.ErrorCode == "SecretDisabled")
        {
            Log.Warning("Azure Key Vault secret {SecretName} is disabled", secretName);
            entry = new KeyVaultEntry(KeyVaultState.Disabled, null, now, now);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var lastKnownValid = cached is { State: KeyVaultState.Found } && now - cached.LastKnownAt <= KeyVaultLastKnownLifetime;
            Log.Warning("Azure Key Vault lookup of {SecretName} failed ({ErrorType}); {Fallback}",
                secretName, ex.GetType().Name, lastKnownValid ? "using the last known value" : "no last known value");
            entry = lastKnownValid
                ? cached! with { FetchedAt = now }
                : new KeyVaultEntry(KeyVaultState.Unavailable, null, now, now);
        }

        _keyVaultCache[secretName] = entry;
        return entry;
    }

    private async Task<EnvironmentConfig?> TryLoadFromAzureAsync(string env)
    {
        if (_secretClient is null)
            return null;

        var connectionString = await GetKeyVaultValueAsync($"{env}-ConnectionString", CancellationToken.None);
        if (connectionString.State == KeyVaultState.Unavailable)
            throw new InvalidOperationException("Application connection configuration could not be loaded for the current environment.");
        if (connectionString.State != KeyVaultState.Found || string.IsNullOrWhiteSpace(connectionString.Value))
        {
            Log.Debug("Secret {SecretName} not found in Azure Key Vault", $"{env}-ConnectionString");
            return null;
        }

        var serverNameEntry = await GetKeyVaultValueAsync($"{env}-ServerName", CancellationToken.None);
        var serverName = serverNameEntry.State == KeyVaultState.Found ? serverNameEntry.Value ?? "." : ".";

        var headersEntry = await GetKeyVaultValueAsync($"{env}-Headers", CancellationToken.None);
        var headers = new Dictionary<string, string>();
        if (headersEntry.State == KeyVaultState.Found && !string.IsNullOrWhiteSpace(headersEntry.Value))
        {
            try
            {
                headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersEntry.Value) ?? new Dictionary<string, string>();
            }
            catch (JsonException ex)
            {
                Log.Warning(ex, "Error parsing headers from Azure Key Vault");
            }
        }

        headers.TryAdd("DatabaseName", env);
        headers.TryAdd("ServerName", serverName);

        return new EnvironmentConfig
        {
            ConnectionString = connectionString.Value,
            ServerName = serverName,
            Headers = headers
        };
    }

    private EnvironmentConfig? LoadFromJson(string env)
    {
        var settingsPath = Path.Combine(_basePath, env, "settings.json");
        Log.Debug("Attempting to load from file: {FilePath}", settingsPath);

        var file = new FileInfo(settingsPath);
        if (!file.Exists)
        {
            Log.Error("settings.json not found for environment: {Environment}, path: {FilePath}", env, settingsPath);
            throw new FileNotFoundException($"settings.json not found for environment: {env}", settingsPath);
        }

        var hasCached = _jsonCache.TryGetValue(settingsPath, out var cached);
        if (hasCached && cached.Stamp == file.LastWriteTimeUtc && cached.Length == file.Length)
            return cached.Config;

        if (_badStamps.TryGetValue(settingsPath, out var bad) && bad == (file.LastWriteTimeUtc, file.Length))
            return hasCached ? cached.Config : throw new InvalidOperationException("Application connection configuration could not be loaded for the current environment.");

        try
        {
            EnvironmentConfig? config;
            try
            {
                config = JsonSerializer.Deserialize<EnvironmentConfig>(File.ReadAllText(settingsPath));
            }
            catch (JsonException)
            {
                config = null;
            }

            if (config == null)
            {
                _badStamps[settingsPath] = (file.LastWriteTimeUtc, file.Length);
                if (hasCached)
                {
                    Log.Error("settings.json for environment {Environment} could not be parsed; serving the last good configuration", env);
                    return cached.Config;
                }

                Log.Error("Failed to deserialize JSON from {FilePath}", settingsPath);
                throw new InvalidOperationException($"Application connection configuration could not be loaded for the current environment.");
            }

            if (string.IsNullOrWhiteSpace(config.ConnectionString))
            {
                Log.Debug("No ConnectionString configured for environment: {Environment} (SQL endpoints will be unavailable)", env);
            }

            DecryptConfig(config, env);
            config.Secrets = ValidateSecrets(config.Secrets, env);

            config.Headers ??= new Dictionary<string, string>();

            if (!config.Headers.ContainsKey("DatabaseName"))
                config.Headers["DatabaseName"] = env;

            if (!config.Headers.ContainsKey("ServerName"))
                config.Headers["ServerName"] = config.ServerName ?? Environment.MachineName;

            _jsonCache[settingsPath] = (file.LastWriteTimeUtc, file.Length, config);
            return config;
        }
        catch (Exception ex) when (ex is not FileNotFoundException && ex is not InvalidOperationException)
        {
            Log.Error(ex, "Error reading or parsing settings.json for environment: {Environment}", env);
            throw;
        }
    }

    private static Dictionary<string, string>? ValidateSecrets(Dictionary<string, string>? secrets, string env)
    {
        if (secrets is null)
            return null;

        var valid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in secrets.GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase))
        {
            var (name, value) = group.First();
            if (!SecretReference.IsValidName(name))
                Log.Error("Secret name {SecretName} in environment {Environment} is invalid; allowed are letters, digits and hyphens, up to 64", name, env);
            else if (group.Count() > 1)
                Log.Error("Secret name {SecretName} in environment {Environment} is defined more than once with different casing", name, env);
            else if (string.IsNullOrWhiteSpace(value))
                Log.Error("Secret {SecretName} in environment {Environment} has an empty value", name, env);
            else
                valid[name] = value;
        }
        return valid;
    }

    private void DecryptConfig(EnvironmentConfig config, string env)
    {
        // Decrypt ConnectionString if encrypted
        if (!string.IsNullOrWhiteSpace(config.ConnectionString) && SettingsEncryptionHelper.IsEncrypted(config.ConnectionString))
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

        if (config.Secrets != null)
        {
            foreach (var name in config.Secrets.Keys.ToList())
                config.Secrets[name] = TryDecrypt(config.Secrets[name], "Secrets", env);
        }

        // Decrypt authentication settings
        if (config.Authentication?.Methods != null)
        {
            foreach (var method in config.Authentication.Methods)
            {
                method.Value = TryDecrypt(method.Value, "Value", env);
                method.Secret = TryDecrypt(method.Secret, "Secret", env);
                method.ClientSecret = TryDecrypt(method.ClientSecret, "ClientSecret", env);
            }
        }
    }

    private string TryDecrypt(string? value, string fieldName, string env)
    {
        if (string.IsNullOrWhiteSpace(value) || !SettingsEncryptionHelper.IsEncrypted(value))
            return value ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_privateKeyPem))
        {
            Log.Error("Field '{FieldName}' is encrypted but no private key available for environment: {Environment}", fieldName, env);
            throw new InvalidOperationException($"Application configuration could not be decrypted for the current environment.");
        }

        return SettingsEncryptionHelper.Decrypt(value, _privateKeyPem);
    }

    private string SecureConnectionString(string connectionString)
    {
        try
        {
            var builder = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connectionString };

            bool hasPassword = builder.ContainsKey("Password") || builder.ContainsKey("Pwd");
            bool hasUserID = builder.ContainsKey("User ID") || builder.ContainsKey("Uid") || builder.ContainsKey("User");

            if (hasUserID || hasPassword)
            {
                Log.Debug("Connection string contains hardcoded credentials, this will later be encrypted.");

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

    internal static string MaskConnectionString(string connectionString)
    {
        try
        {
            var builder = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connectionString };
            return string.Join("; ", builder.Keys.Cast<string>().Select(key =>
                key.Contains("password", StringComparison.OrdinalIgnoreCase) || key.Contains("pwd", StringComparison.OrdinalIgnoreCase)
                    ? $"{key}=***MASKED***"
                    : IsVisibleConnectionKey(key) ? $"{key}={builder[key]}" : $"{key}=***"));
        }
        catch (ArgumentException)
        {
            return "ConnectionString parsing failed";
        }
    }

    private static bool IsVisibleConnectionKey(string key) =>
        key.Contains("server", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("data source", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("database", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("initial catalog", StringComparison.OrdinalIgnoreCase);

    private EncryptionResult AutoEncryptIfNeeded(string settingsPath, EnvironmentConfig config, string envName)
    {
        // Explicit opt-out keeps the file plaintext, for dev checkouts where encrypted files get committed
        if (config.Encrypt == false)
        {
            Log.Debug("Encryption disabled for environment {Env} via Encrypt: false", envName);
            return EncryptionResult.AlreadyEncrypted;
        }

        bool needsSave = false;
        bool alreadyEncrypted = true;

        if (!TryAutoEncryptConnectionString(config, envName, ref needsSave, ref alreadyEncrypted))
        {
            return EncryptionResult.Error;
        }

        AutoEncryptHeaders(config, envName, ref needsSave, ref alreadyEncrypted);
        AutoEncryptAuthentication(config, envName, ref needsSave, ref alreadyEncrypted);
        AutoEncryptSecrets(config, envName, ref needsSave, ref alreadyEncrypted);

        if (needsSave)
        {
            SaveEncryptedConfig(settingsPath, config);
            Log.Debug("Encryption complete for environment: {Env}", envName);
            return EncryptionResult.Encrypted;
        }

        return alreadyEncrypted ? EncryptionResult.AlreadyEncrypted : EncryptionResult.Error;
    }

    private bool TryAutoEncryptConnectionString(EnvironmentConfig config, string envName, ref bool needsSave, ref bool alreadyEncrypted)
    {
        if (string.IsNullOrWhiteSpace(config.ConnectionString) || SettingsEncryptionHelper.IsEncrypted(config.ConnectionString))
            return true;

        alreadyEncrypted = false;

        if (IsValidConnectionString(config.ConnectionString))
        {
            config.ConnectionString = SettingsEncryptionHelper.Encrypt(config.ConnectionString);
            needsSave = true;
            Log.Debug("Encrypted ConnectionString for environment: {Env}", envName);
            return true;
        }

        Log.Error("Invalid connection string format in environment '{Env}' - skipping encryption. Connection string must be a valid key-value format.", envName);
        return false;
    }
    private void AutoEncryptHeaders(EnvironmentConfig config, string envName, ref bool needsSave, ref bool alreadyEncrypted)
    {
        if (config.Headers == null) return;

        var headersToEncrypt = config.Headers
            .Where(h => !SettingsEncryptionHelper.IsEncrypted(h.Value) && IsSensitiveField(h.Key))
            .ToList();

        if (headersToEncrypt.Any())
        {
            alreadyEncrypted = false;
        }

        foreach (var header in headersToEncrypt)
        {
            config.Headers[header.Key] = SettingsEncryptionHelper.Encrypt(header.Value);
            needsSave = true;
            Log.Debug("Encrypted header '{HeaderKey}' for environment: {Env}", header.Key, envName);
        }
    }

    private void AutoEncryptAuthentication(EnvironmentConfig config, string envName, ref bool needsSave, ref bool alreadyEncrypted)
    {
        if (config.Authentication?.Methods == null) return;

        foreach (var method in config.Authentication.Methods)
        {
            method.Value = EncryptFieldIfNeeded(method.Value, "Value", envName, ref needsSave, ref alreadyEncrypted) ?? string.Empty;
            method.Secret = EncryptFieldIfNeeded(method.Secret, "Secret", envName, ref needsSave, ref alreadyEncrypted);
            method.ClientSecret = EncryptFieldIfNeeded(method.ClientSecret, "ClientSecret", envName, ref needsSave, ref alreadyEncrypted);
        }
    }

    private static void AutoEncryptSecrets(EnvironmentConfig config, string envName, ref bool needsSave, ref bool alreadyEncrypted)
    {
        if (config.Secrets == null) return;

        foreach (var name in config.Secrets.Keys.ToList())
        {
            var value = config.Secrets[name];
            if (string.IsNullOrWhiteSpace(value) || SettingsEncryptionHelper.IsEncrypted(value))
                continue;

            config.Secrets[name] = SettingsEncryptionHelper.Encrypt(value);
            needsSave = true;
            alreadyEncrypted = false;
            Log.Debug("Encrypted secret {SecretName} for environment: {Env}", name, envName);
        }
    }

    private string? EncryptFieldIfNeeded(string? fieldValue, string fieldName, string envName, ref bool needsSave, ref bool alreadyEncrypted)
    {
        if (!string.IsNullOrWhiteSpace(fieldValue) && !SettingsEncryptionHelper.IsEncrypted(fieldValue) && IsSensitiveField(fieldName))
        {
            var encryptedValue = SettingsEncryptionHelper.Encrypt(fieldValue);
            needsSave = true;
            alreadyEncrypted = false;
            Log.Debug("Encrypted Auth {FieldName} for environment: {Env}", fieldName, envName);
            return encryptedValue;
        }
        return fieldValue;
    }

    private bool IsValidConnectionString(string connectionString)
    {
        try
        {
            var builder = new System.Data.Common.DbConnectionStringBuilder
            {
                ConnectionString = connectionString
            };

            return builder.Count > 0;
        }
        catch
        {
            return false;
        }
    }
    private static bool IsSensitiveField(string fieldName) => HeaderPolicy.IsSensitiveName(fieldName);

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

            var json = JsonSerializer.Serialize(config, SettingsFileJsonOptions);
            AtomicFile.WriteAllText(settingsPath, json);
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

}
