# Secrets Encryption

Portway automatically encrypts sensitive data in your environment settings files on startup. Connection strings and sensitive headers (containing words like "password", "secret", "token", etc.) are encrypted using RSA + AES hybrid encryption to keep your data safe at rest.

## How It Works

**Automatic Encryption on Startup:**
1. When Portway starts, it checks for encryption keys in the `.core` folder
2. If keys don't exist, it generates a new RSA 2048-bit keypair automatically
3. It scans all environment folders (`environments/*/settings.json`)
4. Any unencrypted connection strings or sensitive headers are automatically encrypted
5. The encrypted values are saved back to the settings files

**Key Storage:**
- Private key: `.core/recovery.binlz4` (encrypted with `PORTWAY_ENCRYPTION_KEY`)
- Public key: `.core/snapshot_blob.bin`
- The `.core` folder is hidden on Windows

## Encryption Key Management

### Setting the Encryption Key

The `PORTWAY_ENCRYPTION_KEY` is used to protect the private key. Priority order:

1. **Windows Machine Environment Variable** (recommended for production):
   ```powershell
   [System.Environment]::SetEnvironmentVariable('PORTWAY_ENCRYPTION_KEY', 'your-secure-key-here', 'Machine')
   ```

2. **Process Environment Variable**:
   ```powershell
   $env:PORTWAY_ENCRYPTION_KEY = 'your-secure-key-here'
   ```

3. **.env file** (for Docker):
   ```bash
   PORTWAY_ENCRYPTION_KEY=your-secure-key-here
   ```

4. **Fallback**: A hardcoded key (not recommended for production)

### Regenerating Keys

If you need to regenerate encryption keys:

1. Stop the application
2. Delete the `.core` folder
3. (Optional) Set a new `PORTWAY_ENCRYPTION_KEY`
4. Restart the application - new keys will be generated
5. All environment files will be re-encrypted with the new keys

**Warning:** If you change `PORTWAY_ENCRYPTION_KEY` without deleting `.core`, the application won't be able to decrypt the existing private key and encryption will fail.

## What Gets Encrypted

**Always encrypted:**
- `ConnectionString` field (if valid MSSQL format)

**Conditionally encrypted (headers containing these keywords):**
- password
- secret
- token
- key
- auth
- credential
- signature
- hmac
- bearer

**Example - Before encryption:**
```json
{
  "ServerName": "localhost",
  "ConnectionString": "Server=localhost;Database=MyDB;User ID=sa;Password=mypass;",
  "Headers": {
    "ApiToken": "secret-token-123"
  }
}
```

**After encryption:**
```json
{
  "ServerName": "localhost",
  "ConnectionString": "PWENC:aG5kc2...::dG9rZW4=",
  "Headers": {
    "ApiToken": "PWENC:bXlzZW...::c2VjcmV0"
  }
}
```

## Validation

Connection strings are validated before encryption. They must have:
- `DataSource` (server name)
- `InitialCatalog` (database name)
- Either `IntegratedSecurity=true` OR valid `UserID` and `Password`

**Invalid connection strings will not be encrypted** and an error will be logged:
```
[ERR] Invalid MSSQL connection string format in environment 'MyEnv' - skipping encryption.
```

## Troubleshooting

You may encounter issues upon starting the application, perhaps when a system error occurred. 

### File Permission Errors

If you see:
```
[ERR] Access denied when saving to C:\...\settings.json
```

**Solution:** Remove the read-only flag:
```powershell
# Single file
Set-ItemProperty "C:\Apps\Portway API\v1\environments\prod\settings.json" -Name IsReadOnly -Value $false

# All settings files
Get-ChildItem "C:\Apps\Portway API\v1\environments\*\settings.json" | ForEach-Object { $_.IsReadOnly = $false }
```

### Decryption Failures

If the application can't decrypt settings on startup:

```
[ERR] Failed to load or decrypt private key from .core/recovery.binlz4
```

**Causes:**
1. The `PORTWAY_ENCRYPTION_KEY` was changed after keys were generated
2. The `.core/recovery.binlz4` file is corrupted
3. Wrong encryption key is being used

**Solution:** Delete the `.core` folder and restart to regenerate keys.

### Viewing Logs

Enable debug logging to see encryption details:

In `appsettings.json`:
```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug"
    }
  }
}
```

You'll see:
```
[DBG] Scanning all environments for auto-encryption...
[DBG] Found 3 environment(s): 600, 700, Synergy
[DBG] Auto-encrypted ConnectionString for environment: 600
[DBG] Auto-encryption scan complete: 3 encrypted, 0 already encrypted, 0 errors
```

## Security Best Practices

Please make sure to implement the following best practices:

1. **Set a strong `PORTWAY_ENCRYPTION_KEY`** in production (use machine environment variable)
2. **Backup the `.core` folder** securely - without it, you cannot decrypt your settings
3. **Never commit `.core` folder** to source control, if you use this
4. **Use different encryption keys** for different environments (dev/staging/production)