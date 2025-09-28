# Encrypt/Decrypt Usage Guide

A utility to securely manage `environment` files in **Portway**.

---

## Features

- Encrypts and decrypts all `settings.json` files in `Environments/*`.  
- Uses an **RSA keypair**:
  - Public key is loaded from private key if available.  
  - Private key is generated automatically if missing.  
- Exports **private key** to `<root>/certs/key_b.pem`.  
- Automatically derives and saves the **public key** to `<root>/certs/key_a.pem`.  
- Detects already-encrypted files using the header `PWENC:`.  
- Prompts for private key only if missing (optional fallback).  
- Compatible with `PortwayApi/Helpers/SettingsEncryptionHelper`.

---

## Usage

```bash
# Encrypt all environment settings
Encrypt.exe -e

# Decrypt all environment settings
Encrypt.exe -d

# Verify encryption status of files
Encrypt.exe -v

# Specify a custom environment directory
Encrypt.exe --encrypt --envdir /path/to/Environments
```

---

## Behavior

1. **On first run**, if `<root>/certs/key_b.pem` does not exist:  
   - Generates a new 2048-bit RSA keypair.  
   - Saves the private key to `certs/key_b.pem`.  
   - Derives and saves the public key to `certs/key_a.pem`.  

2. **Subsequent runs**:  
   - Loads the existing private key and derives the current public key.  
   - Encrypts using the current public key.  
   - Decrypts using the private key.  

3. **File encryption detection**:  
   - Files starting with `PWENC:` are considered encrypted and skipped during re-encryption.  

---

## Notes

- Keep `certs/key_b.pem` safe — it is required to decrypt files.  
- The public key is dynamically derived from the private key and will not change mid-project.  

---

## Example

```bash

# Encrypt environment files
Encrypt.exe -e

# Decrypt environment files
Encrypt.exe -d

# Verify encryption status
Encrypt.exe -v
```

---