---
title: Secrets encryption
description: "Encryption of connection strings, headers and authentication secrets in environment settings"
---

# Secrets encryption

At startup, Portway encrypts plaintext secrets in `environments/*/settings.json` and saves the encrypted values back to the file. Encrypted values start with `PWENC:` and use RSA 2048 with AES.

## Encrypted values

| Field | Condition |
|---|---|
| `ConnectionString` | Key-value format (`Key=Value;...`) |
| `Headers` | Header name contains `password`, `secret`, `token`, `key`, `auth`, `credential`, `signature`, `hmac`, `bearer` or `value` |
| `Authentication.Methods` | `Value`, `Secret` and `ClientSecret` |

```json
{
  "ConnectionString": "PWENC:aG5kc2...::dG9rZW4=",
  "Headers": {
    "ApiToken": "PWENC:bXlzZW...::c2VjcmV0"
  }
}
```

A connection string that is not in key-value format (e.g. `postgresql://...`) is not encrypted, and the error `Invalid connection string format in environment '{Env}' - skipping encryption.` is logged.

## Keys

| File | Content |
|---|---|
| `.core/snapshot_blob.bin` | Public key |
| `.core/recovery.binlz4` | Private key, encrypted with `PORTWAY_ENCRYPTION_KEY` |

The key pair is generated on the first start. `PORTWAY_ENCRYPTION_KEY` is read from, in order:

1. Machine environment variable (Windows)
2. Process environment variable
3. `.env` in the working directory

Outside Development, Portway refuses to start without `PORTWAY_ENCRYPTION_KEY`.

::: code-group

```powershell [Windows]
[System.Environment]::SetEnvironmentVariable('PORTWAY_ENCRYPTION_KEY', 'your-secure-key', 'Machine')
```

```bash [Docker .env]
PORTWAY_ENCRYPTION_KEY=your-secure-key
```

:::

:::warning
Back up `.core` together with `PORTWAY_ENCRYPTION_KEY`. Without both, `PWENC:` values cannot be decrypted. Keep `.core` out of source control.
:::

## Key replacement

1. Replace every `PWENC:` value in `environments/*/settings.json` with its plaintext value.
2. Stop Portway and delete `.core`.
3. Set the new `PORTWAY_ENCRYPTION_KEY`.
4. Start Portway; new keys are generated and the values are encrypted again.

Changing `PORTWAY_ENCRYPTION_KEY` without these steps leaves the private key unreadable: `Failed to load or decrypt private key from .core/recovery.binlz4`.

## Troubleshooting

| Error | Resolution |
|---|---|
| `Access denied when saving to ...settings.json` | Remove the read-only attribute or grant the service account write access |
| `Failed to load or decrypt private key` | Restore the original `PORTWAY_ENCRYPTION_KEY` or `.core` |
| `PORTWAY_ENCRYPTION_KEY is not set` | Set the key before starting outside Development |

Encryption details are logged at `Debug` ([Logging](/reference/logging)).

## Related topics

- [Environment Settings](/reference/environment-settings)
- [Security](/guide/security)
