# Secrets Encryption Tool

The Secrets Encryption Tool is a command-line utility for locking (encrypting) and unlocking (decrypting) your environment settings files in Portway. It helps keep sensitive information—like passwords and connection strings—safe, even if someone else gets access to your files.

## Installation

### Prerequisites

- .NET 9.0 Runtime (which is only available in the Hosting Bundle)
- Access to your environment settings files
- Your private key (for unlocking)

### Setup

1. Navigate to the tools directory:
```bash
cd Deployment/PortwayApi/tools/Encrypt
```

2. Run the executable:
```bash
Encrypt.exe --help
```

## Usage Modes

### Lock (Encrypt) All Settings

```bash
Encrypt.exe -e
```
This will encrypt all your environment settings files so only you (with your private key) can unlock them.

### Unlock (Decrypt) All Settings

```bash
Encrypt.exe -d
```
You’ll be asked to paste your private key. This will unlock all your settings files.

### Generate or Check Keys

- To generate a new keypair:
  ```bash
  Encrypt.exe -g
  ```
- To check if your private key matches the public key:
  ```bash
  Encrypt.exe -k
  ```

## Command Line Parameters

| Parameter         | Description                                 | Example           |
|-------------------|---------------------------------------------|-------------------|
| `-e, --encrypt`   | Encrypt all settings files                   | `-e`              |
| `-d, --decrypt`   | Decrypt all settings files                   | `-d`              |
| `-g, --genkeys`   | Generate a new keypair                       | `-g`              |
| `-k, --printpubkey` | Print public key from pasted private key    | `-k`              |
| `-p, --envdir`    | Override environments directory              | `-p "path"`       |
| `-h, --help`      | Show help message                            | `-h`              |

## Error Messages

If you see a message like:

> Decryption failed: The private key may not match the public key used for encryption, or the file may be corrupted.

This usually means you’re using the wrong key, or the file is not encrypted or is damaged. Try again or ask your administrator for help.

**Tip:** Never share your private key. Store it somewhere safe!
