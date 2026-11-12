---
title: Secrets
description: "Secret store, secret references and encryption of environment settings"
---

# Secrets

## Secret store

`environments/{env}/settings.json` contains a `Secrets` object of named values. Configuration refers to a value as `${secret:NAME}`. The route environment selects the value: a reference resolves in the environment of the request. There are no global secrets.

```json
{
  "ConnectionString": "Server=sql;Database=app;User ID=svc;Password=${secret:DB-PASSWORD}",
  "Headers": { "X-Api-Key": "${secret:ERP-KEY}" },
  "Secrets": {
    "DB-PASSWORD": "PWENC:...",
    "ERP-KEY": "PWENC:..."
  }
}
```

| Rule | Value |
|---|---|
| Name | `A-Z`, `a-z`, `0-9` and `-`, 1 to 64 characters, case-insensitive |
| Storage | Encrypted at rest with `PWENC:` unless the file sets `"Encrypt": false` |
| Resolution | At request time, single pass. A value that contains `${secret:...}` is not resolved again |
| Missing value | An absent, empty or whitespace value is missing. The request returns a generic `500` and logs one error per environment and name |
| Invalid entry | An invalid name, a name repeated with other casing or an empty value logs one error per name. The environment loads without that entry |

### Reference sites

| Site | File | Rule |
|---|---|---|
| `ConnectionString` | `environments/{env}/settings.json` | Substituted per connection-string key. A `;` in the value adds no keyword |
| `Headers` values | `environments/{env}/settings.json` | A value with CR, LF or NUL fails the request |
| `Authentication.Methods` `Value`, `Secret`, `ClientSecret` | `environments/{env}/settings.json` | Whole field. Resolved when the method is tried |
| Query value in `Url`, `FallbackUrls` (Proxy, Composite) | `entity.json` | URL-encoded. A reference outside a query value is a load error |
| `Headers` values (Proxy, Composite step target) | `entity.json` | A value with CR, LF or NUL fails the request |
| `TemplateTransformations` values (Composite) | `entity.json` | Whole value. Used values are replaced with `***` in the Composite response |
| `Verification` `Secret`, `PreviousSecret` (Webhook) | `entity.json` | Whole field. A literal value is a load error |

Client input is never resolved: a reference in a query string, path or body is sent as text. A redirect target from an upstream is never resolved.

### Azure Key Vault

With `PORTWAY_KEY_VAULT_URI` set, Key Vault values take precedence over the file:

::: code-group

```powershell [Windows]
$env:PORTWAY_KEY_VAULT_URI = "https://your-keyvault.vault.azure.net/"
```

```bash [Linux]
export PORTWAY_KEY_VAULT_URI="https://your-keyvault.vault.azure.net/"
```

:::

| Key Vault secret | Replaces |
|---|---|
| `{env}-ConnectionString` | `ConnectionString` |
| `{env}-ServerName` | `ServerName` |
| `{env}-Headers` | `Headers` (JSON string) |
| `{env}-Secret-{NAME}` | `Secrets.NAME` |

Authentication to Key Vault uses the Azure SDK `DefaultAzureCredential` (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_CLIENT_SECRET`).

| Key Vault response | Behaviour |
|---|---|
| Value | Used for 5 minutes |
| `404` | The file value is used. Checked again after 60 seconds |
| `SecretDisabled` | Missing. The file value is not used |
| Other failure | The last known value is used for at most 1 hour, then missing. The file value is not used |

Each instance caches on its own. A rotated or disabled Key Vault secret applies within 5 minutes per instance.

### Console

**Environments** displays secret names. Administrators add, replace and delete values; no route returns a value. Changes apply immediately and are written to the audit log without the value. Saving an endpoint returns every referenced secret without a value in an environment of that endpoint.

### Rotation

1. Add the new value under a second name, or set it as `PreviousSecret` for webhook verification.
2. Update the sender or upstream.
3. Remove the old value.

Emergency revocation: delete the value in the console or the file; the next request fails closed. In Key Vault, disable or delete the secret; each instance stops using it within 5 minutes.

## Encryption

At startup, Portway encrypts plaintext secrets in `environments/*/settings.json` and saves the encrypted values back to the file. Encrypted values start with `PWENC:` and use RSA 2048 with AES.

## Encrypted values

| Field | Condition |
|---|---|
| `ConnectionString` | Key-value format (`Key=Value;...`) |
| `Headers` | Header name contains `password`, `secret`, `token`, `key`, `auth`, `credential`, `signature`, `hmac`, `bearer` or `value` |
| `Authentication.Methods` | `Value`, `Secret` and `ClientSecret` |
| `Secrets` | Every value |

```json
{
  "ConnectionString": "PWENC:aG5kc2...::dG9rZW4=",
  "Headers": {
    "ApiToken": "PWENC:bXlzZW...::c2VjcmV0"
  }
}
```

A connection string that is not in key-value format (e.g. `postgresql://...`) is not encrypted, and the error `Invalid connection string format in environment '{Env}' - skipping encryption.` is logged.

### Keys

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

```bash [Linux]
export PORTWAY_ENCRYPTION_KEY=your-secure-key
# or in .env in the working directory:
# PORTWAY_ENCRYPTION_KEY=your-secure-key
```

:::

:::warning
Back up `.core` together with `PORTWAY_ENCRYPTION_KEY`. Without both, `PWENC:` values cannot be decrypted. Exclude `.core` from source control.
:::

### Key replacement

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
