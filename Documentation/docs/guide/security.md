---
title: Security
description: "Token authentication, scope control, network restrictions, and encryption for a Portway deployment"
---

# Security

Tokens authenticate callers; scopes, environments and tenant headers restrict what a token reaches; the network access policy restricts upstream targets.

## Authentication

API requests require a Bearer token:

```http
Authorization: Bearer your-token-here
```

Tokens are cryptographically random values, stored hashed in `auth.db`, and bound to a username for auditing.

### First-run token

The first start generates a token and writes it to `tokens/YOUR_SERVER_NAME.txt`. File format: [Token Audit Log](/reference/token-audit).

::: warning
This file contains a token with full scope and environment access. Delete it after recording the token.
:::

### Console accounts

Roles, recovery and session keys: [Console Accounts](/guide/accounts).

## Authorization

### Scopes and environments

Token fields `AllowedScopes` and `AllowedEnvironments` restrict a token to endpoints and environments. Patterns: [Access Tokens](/guide/tokens#scoping-tokens).

### Tenant headers

Tenant headers restrict a token to the rows, upstream records and files of specific customers: [Tenant Headers](/guide/tenant-headers).

### Endpoint-level restrictions

Endpoints define their own environments and methods:

```json
{
  "DatabaseObjectName": "SensitiveData",
  "AllowedEnvironments": ["prod"],
  "AllowedMethods": ["GET"]
}
```

`Hidden` removes an endpoint from the OpenAPI document and does not restrict access.

A request must pass both the token and the endpoint restrictions. Matrix: [Environments, access control](/guide/environments#access-control).

## Network security

### Upstream hosts

Proxy targets are restricted by `environments/network-access-policy.json`: [Network access policy](/guide/environments#network-access-policy).

### Security headers

Response headers:

| Header | Value |
|---|---|
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` |
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains` (HTTPS requests) |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `Content-Security-Policy` | `default-src 'self'; object-src 'none'; frame-ancestors 'none'; ...` |

Console pages at `/ui` also send `Cross-Origin-Opener-Policy` and `Cross-Origin-Resource-Policy` (`same-origin`). Full list: [Headers](/reference/headers).

## Secrets management

### Automatic encryption

Plaintext connection strings and authentication values in environment `settings.json` files are encrypted at the next start (`PWENC:...`). The chat API key in `mcp.db` is encrypted. `appsettings.json` is not rewritten; values there (e.g. `WebUi:AdminApiKey`) remain plaintext.

### Azure Key Vault

Connection strings, server names and headers can be read from Azure Key Vault: [Environments, Azure Key Vault](/guide/environments#azure-key-vault).

## SQL Server permissions

Minimum permissions for a Windows Authentication (NTLM) database account under IIS:

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

Grant only the roles the endpoints use; read-only deployments need only `db_datareader`.

## Logging and auditing

Security events are logged at `Warning` or `Debug`:

```
[DBG] Invalid token: {masked}
[WRN] IP {IP} has exceeded rate limit, blocking for {period}
```

Traffic logging with headers and bodies:

```json
{
  "RequestTrafficLogging": {
    "Enabled": true,
    "CaptureHeaders": true,
    "IncludeRequestBodies": true
  }
}
```

Configuration: [Health and Logs](/guide/monitoring).

## Pre-deployment checklist

- [ ] HTTPS binding configured in IIS
- [ ] IIS Application Pool using minimum-privilege identity
- [ ] Console account with a strong password; legacy `PORTWAY_ADMIN_KEY` removed
- [ ] `PORTWAY_KNOWN_PROXIES` set to the reverse proxy (client addresses for rate limiting, sign-in lockout and the console network gate)
- [ ] Read-only console users hold `viewer`
- [ ] `portway.key` kept with the deployment and out of shared backups
- [ ] Azure Key Vault configured (if applicable)
- [ ] Initial token file removed from disk
- [ ] Tokens created with specific scopes and environments
- [ ] Rate limiting configured
- [ ] Firewall rules reviewed
- [ ] Security headers verified with a response inspection tool

## Compromised token

Please make sure to take these actions if a token may be compromised.

1. Under **Access Tokens**, rotate the affected token; rotation revokes the old value and issues a replacement.
2. Update the applications that use the token.
3. Enable traffic logging to monitor further use:
   ```json
   {
     "RequestTrafficLogging": { "Enabled": true, "CaptureHeaders": true }
   }
   ```
4. Monitor the traffic for any indicators of compromise.
5. Record the incident.

## Next steps

- [Rate Limiting](/guide/rate-limiting)
- [Environments, authentication](/guide/environments#per-environment-authentication)
- [Health and Logs](/guide/monitoring)
- [Deployment](/guide/deployment)
