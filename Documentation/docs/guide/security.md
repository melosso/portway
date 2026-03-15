# Security

> Token authentication, scope control, network restrictions, and encryption for a Portway deployment.

:::warning
Configure Portway in accordance with your organisation's security policies before exposing it to production traffic. The defaults are sensible starting points, not a finished security posture.
:::

## Authentication

All API requests require a Bearer token:

```http
Authorization: Bearer your-token-here
```

Tokens are generated using cryptographically secure random values and stored encrypted on disk. Each token is bound to a username for audit trail purposes.

### First-run token

On first run, Portway generates an initial token and writes it to `tokens/YOUR_SERVER_NAME.txt`:

```json
{
  "Username": "SERVER-NAME",
  "Token": "base64-encoded-secure-token",
  "AllowedScopes": "*",
  "AllowedEnvironments": "*",
  "ExpiresAt": "Never",
  "CreatedAt": "2024-01-01 00:00:00"
}
```

Remove this file from disk immediately after recording the token. Use the Web UI to manage all subsequent tokens.

## Authorization

### Scope control

Restrict a token to specific endpoints using the `AllowedScopes` field:

| Pattern | Access |
|---|---|
| `*` | All endpoints |
| `Products,Orders` | Named endpoints only |
| `Product*` | All endpoints matching the prefix |
| `Company/Employees` | Specific namespaced endpoint |
| `Company/*` | All endpoints in a namespace |
| `GET:Products` | Single endpoint, single method |

### Environment control

Restrict a token to specific environments using `AllowedEnvironments`:

| Pattern | Access |
|---|---|
| `*` | All environments |
| `prod` | Single environment |
| `dev,test` | Named environments |
| `dev*` | All environments matching the prefix |

### Endpoint-level restrictions

Individual endpoints enforce their own environment and visibility constraints:

```json
{
  "DatabaseObjectName": "SensitiveData",
  "AllowedEnvironments": ["prod"],
  "IsPrivate": true,
  "AllowedMethods": ["GET"]
}
```

Both token-level and endpoint-level restrictions must pass for a request to succeed. See [Environments — access control](./environments#access-control) for the full matrix.

## Network security

### IP restrictions

Configure allowed hosts and blocked IP ranges in `environments/network-access-policy.json`:

```json
{
  "allowedHosts": [
    "localhost",
    "127.0.0.1",
    "your-internal-server.local"
  ],
  "blockedIpRanges": [
    "10.0.0.0/8",
    "172.16.0.0/12",
    "192.168.0.0/16"
  ]
}
```

### Security headers

Portway adds these headers to all responses automatically:

| Header | Value |
|---|---|
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` |
| `Strict-Transport-Security` | `max-age=31536000` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `Content-Security-Policy` | `default-src 'self'; object-src 'none'; ...` |

## Secrets management

### Automatic encryption

Portway encrypts plaintext secrets in `settings.json` files on next startup. Connection strings and authentication values written in plaintext become `PWENC:...` format. The original value is no longer stored.

### Azure Key Vault

Store connection strings and server names in Azure Key Vault instead of `settings.json`:

```powershell
$env:KEYVAULT_URI = "https://your-keyvault.vault.azure.net/"
```

Create secrets named by environment:
- `{environment}-ConnectionString`
- `{environment}-ServerName`
- `{environment}-Headers` (JSON string)

Portway fetches these at startup and uses them identically to file-based configuration.

## SQL Server permissions

When using Windows Authentication (NTLM) via IIS, configure the database account with minimum required permissions:

```sql
USE [master];
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'DOMAIN\USER_NAME')
BEGIN
    EXEC ('CREATE LOGIN [DOMAIN\USER_NAME] FROM WINDOWS;');
END
GO
USE [YourDatabase];
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'DOMAIN\USER_NAME')
BEGIN
    CREATE USER [DOMAIN\USER_NAME] FOR LOGIN [DOMAIN\USER_NAME];
END
GO
ALTER ROLE [db_datareader] ADD MEMBER [DOMAIN\USER_NAME];  -- Read tables/views
ALTER ROLE [db_datawriter] ADD MEMBER [DOMAIN\USER_NAME];  -- Write (insert/update/delete)
GRANT EXECUTE TO [DOMAIN\USER_NAME];                       -- Stored procedures
GRANT VIEW DEFINITION TO [DOMAIN\USER_NAME];               -- Schema metadata
GO
```

Grant only the roles your endpoints require. A read-only deployment needs only `db_datareader`.

## Logging and auditing

Security events are logged at Warning or Debug level:

```
[DBG] Invalid token: {masked}
[WRN] IP {IP} has exceeded rate limit, blocking for {period}
```

Enable request traffic logging to capture headers and bodies for security analysis:

```json
{
  "RequestTrafficLogging": {
    "Enabled": true,
    "CaptureHeaders": true,
    "IncludeRequestBodies": true
  }
}
```

See [Monitoring](./monitoring) for traffic logging configuration details.

## Pre-deployment checklist

- [ ] HTTPS binding configured in IIS
- [ ] IIS Application Pool using minimum-privilege identity
- [ ] Azure Key Vault configured (if applicable)
- [ ] Initial token file removed from disk
- [ ] Tokens created with specific scopes and environments
- [ ] Rate limiting configured
- [ ] Firewall rules reviewed
- [ ] Security headers verified with a response inspection tool

## Incident response — compromised token

1. Open the Web UI and navigate to **Access Tokens**
2. Click **Rotate Token** on the affected entry to invalidate it and generate a replacement
3. Update all applications using the compromised token with the new value
4. Enable traffic logging if not already active to monitor for continued unauthorized activity:
   ```json
   {
     "RequestTrafficLogging": { "Enabled": true, "CaptureHeaders": true }
   }
   ```
5. Document the incident for your security audit trail

:::info
Manage all tokens in the [Web UI](./webui) under **Tokens**. The **Tokens** page lists all active (non-revoked, non-expired) tokens with their scope and environment restrictions.
:::

:::warning
Token revocation is permanent. A revoked token cannot be reactivated. Create a new token for any affected user or system.
:::

## Next steps

- [Rate Limiting](./rate-limiting)
- [Environments — authentication](./environments#per-environment-authentication)
- [Monitoring](./monitoring)
- [Deployment](./deployment)
