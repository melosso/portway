---
title: Security
description: "Token authentication, scope control, network restrictions, and encryption for a Portway deployment"
---

# Security

Tokens authenticate callers; scopes, environments and tenant headers restrict what a token reaches; the network access policy restricts upstream targets.

::: Note
Align this configuration with your organisation's security policies before production use.
:::

## Authentication

API requests require a Bearer token:

```http
Authorization: Bearer your-token-here
```

Tokens are cryptographically random values, stored hashed in `auth.db`, and bound to a username for auditing.

### First-run token

The first start generates a token and writes it to `tokens/YOUR_SERVER_NAME.txt`. File format: [Token generator](/reference/token-generator).

::: Caution
This file contains a token with full scope and environment access. Delete it after recording the token.
:::

## Authorization

### Scopes and environments

Token fields `AllowedScopes` and `AllowedEnvironments` restrict a token to endpoints and environments. Patterns: [Access Tokens](/guide/tokens#scoping-tokens).

### Tenant headers

Tenant headers restrict a token to the rows, upstream records and files of specific customers. `Tenancy` on the endpoint maps each header to its target; `AllowedTenants` on the token lists the permitted values per header. The request header selects one permitted value and cannot add a value.

```json [endpoints/SQL/Sales/Orders/entity.json]
{
  "DatabaseObjectName": "Orders",
  "AllowedColumns": ["Id", "Total"],
  "Tenancy": { "X-Company-Id": "CompanyId" }
}
```

```json [token AllowedTenants]
{ "X-Company-Id": ["ACME", "GLOBEX"] }
```

Header names are configurable (e.g. `X-Company-Id`, `X-Client-Id`, `Administratie`). Reserved names are rejected: `Authorization`, `Cookie`, `Host`, `Origin`, `Content-*`, `X-Forwarded-*` and hop-by-hop headers. Values match `[A-Za-z0-9][A-Za-z0-9_.-]{0,63}`; `*` in `AllowedTenants` accepts any value of that form.

| Request | Result |
|---|---|
| Endpoint has no `Tenancy` | Request unchanged |
| No bearer token (environment authentication) | `403` |
| Token holds no value for the header | `403` |
| Header absent, token holds one value | That value |
| Header absent, token holds several values or `*` | `400` |
| Header holds a value the token holds | That value |
| Header holds another value | `403` |
| Header repeated or malformed | `400` |

The `Tenancy` value depends on the endpoint type:

| Endpoint type | `Tenancy` value | Behavior |
|---|---|---|
| SQL table or view | Column | Reads and `$count` include `Column = value`, combined with `AND` outside the client `$filter`. Inserts set the column; updates and deletes require a match; the column cannot be changed. Rows of other tenants return `404`. |
| SQL table-valued function | Function parameter | Receives the tenant value; client values are ignored. |
| SQL stored procedure | Procedure parameter | `@{value}` receives the tenant value after the payload parameters. The procedure enforces it. |
| Proxy | Upstream header | Set to the tenant value. Client copies of the inbound and upstream headers are removed. |
| File | Ignored | `BaseDirectory` contains a `{Header}` placeholder per tenant header. Uploads, downloads, deletes and listings are restricted to the resolved directory. |

Tenancy is not supported on static, webhook or composite endpoints, on composite step targets or on `$expand` targets. Endpoints with invalid `Tenancy` are not loaded and cannot be saved in the console. The OpenAPI document lists tenant headers as optional header parameters; MCP tools accept them in the `tenants` argument.

### Endpoint-level restrictions

Endpoints define their own environments, visibility and methods:

```json
{
  "DatabaseObjectName": "SensitiveData",
  "AllowedEnvironments": ["prod"],
  "Hidden": true,
  "AllowedMethods": ["GET"]
}
```

A request must pass both the token and the endpoint restrictions. Matrix: [Environments, access control](/guide/environments#access-control).

## Network security

### IP restrictions

Allowed upstream hosts and blocked IP ranges are set in `environments/network-access-policy.json`:

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

Headers added to every response:

| Header | Value |
|---|---|
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` |
| `Strict-Transport-Security` | `max-age=31536000` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `Content-Security-Policy` | `default-src 'self'; object-src 'none'; frame-ancestors 'none'; ...` |

Console pages at `/ui` also send `Cross-Origin-Opener-Policy` and `Cross-Origin-Resource-Policy` (`same-origin`). Full list: [Headers](/reference/headers).

## Secrets management

### Automatic encryption

Plaintext connection strings and authentication values in environment `settings.json` files are encrypted at the next start (`PWENC:...`). The MCP configuration store (`mcp.db`) is encrypted too. `appsettings.json` is not rewritten; values there (e.g. `WebUi:AdminApiKey`) remain plaintext.

### Console accounts

Console accounts are stored in `auth.db` with PBKDF2-SHA256 password hashes. The first start without accounts creates an administrator with a random one-time password, logged once and changed at first sign-in. The account name is `admin-` plus eight random characters, or `admin` when `WebUi:SeedPassword` or the legacy `WebUi:AdminApiKey` (`PORTWAY_ADMIN_KEY`) is set. `WebUi:SeedPassword` sets a fixed password without a forced change, for demo instances only.

The legacy `WebUi:AdminApiKey` is not used for sign-in. It enables the console like `WebUi:Enabled` and can be removed, also from **Settings → Security → Deployment & Access**.

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

Configuration: [Monitoring](/guide/monitoring).

### Recovering an account

Account recovery runs from the shell, in the directory that contains `auth.db`:

```bash
portway accounts list
portway accounts password <username> <new-password>
portway accounts create <username> <password> [administrator|viewer]
```

Docker (the image has no `portway` binary):

```bash
docker exec <container> dotnet /app/PortwayApi.dll accounts list
docker exec <container> dotnet /app/PortwayApi.dll accounts password <username> <new-password>
docker exec <container> dotnet /app/PortwayApi.dll accounts create <username> <password> [administrator|viewer]
```

Further subcommands: `promote`, `demote`, `enable`, `disable`, `delete`. A command that would leave no active administrator is refused.

### Account roles

| Role | Access |
|---|---|
| `administrator` | All console settings, endpoints, environments, tokens and accounts |
| `viewer` | Read access; own password and own [single sign-on](/guide/sso) link. Other writes return `403` |

The role is read from `auth.db` on every request, not from the session cookie; a demotion applies to the next request.

::: warning Upgrades
Earlier builds did not enforce the `viewer` role. Review the accounts under **Users** after upgrading.
:::

Sessions are signed with `portway.key`, created next to `auth.db`. Deleting it ends all sessions.

## Pre-deployment checklist

- [ ] HTTPS binding configured in IIS
- [ ] IIS Application Pool using minimum-privilege identity
- [ ] Console account with a strong password; legacy `PORTWAY_ADMIN_KEY` removed
- [ ] `ForwardedHeaders__KnownProxies` set to the reverse proxy (client addresses for rate limiting, sign-in lockout and the console network gate)
- [ ] Read-only console users hold `viewer`
- [ ] `portway.key` kept with the deployment and out of shared backups
- [ ] Azure Key Vault configured (if applicable)
- [ ] Initial token file removed from disk
- [ ] Tokens created with specific scopes and environments
- [ ] Rate limiting configured
- [ ] Firewall rules reviewed
- [ ] Security headers verified with a response inspection tool

## Incident response: compromised token

1. Under **Access Tokens**, rotate the affected token; rotation revokes the old value and issues a replacement.
2. Update the applications that use the token.
3. Enable traffic logging to monitor further use:
   ```json
   {
     "RequestTrafficLogging": { "Enabled": true, "CaptureHeaders": true }
   }
   ```
4. Record the incident.

## Next steps

- [Rate Limiting](/guide/rate-limiting)
- [Environments, authentication](/guide/environments#per-environment-authentication)
- [Monitoring](/guide/monitoring)
- [Deployment](/guide/deployment)
