# Encrypt/Decrypt Tool

A utility to securely manage `settings.json` files in **PortwayApi** environments.

---

## Features

- Encrypts and decrypts all `settings.json` files in `Environments/*`.  
- Uses an **RSA keypair**:
  - Public key is loaded from private key if available.  
  - Private key is generated automatically if missing.  
- Exports **private key** to `<root>/certs/portway_private_key.pem`.  
- Automatically derives and saves the **public key** to `<root>/certs/portway_public_key.pem`.  
- Detects already-encrypted files using the header `PWENC:`.  
- Prompts for private key only if missing (optional fallback).  
- Compatible with `PortwayApi/Helpers/SettingsEncryptionHelper`.

---

## Usage

```bash
# Encrypt all environment settings
dotnet run -- encrypt

# Decrypt all environment settings
dotnet run -- decrypt

# Verify encryption status of files
dotnet run -- verify

# Specify a custom environment directory
dotnet run -- encrypt --envdir /path/to/Environments
```

---

## Behavior

1. **On first run**, if `<root>/certs/portway_private_key.pem` does not exist:  
   - Generates a new 2048-bit RSA keypair.  
   - Saves the private key to `certs/portway_private_key.pem`.  
   - Derives and saves the public key to `certs/portway_public_key.pem`.  

2. **Subsequent runs**:  
   - Loads the existing private key and derives the current public key.  
   - Encrypts using the current public key.  
   - Decrypts using the private key.  

3. **File encryption detection**:  
   - Files starting with `PWENC:` are considered encrypted and skipped during re-encryption.  

---

## Notes

- Keep `certs/portway_private_key.pem` safe — it is required to decrypt files.  
- The public key is dynamically derived from the private key and will not change mid-project.  

---

## Example

```bash
# First-time setup (generates keypair)
dotnet run -- encrypt

# Encrypt environment files
dotnet run -- encrypt

# Decrypt environment files
dotnet run -- decrypt

# Verify encryption status
dotnet run -- verify
```

---