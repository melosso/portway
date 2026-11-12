---
title: Security
description: "Token authentication, scope control, network restrictions, and encryption for a Portway deployment"
---

# Security

Tokens authenticate callers. Scopes, environments and tenant headers restrict the endpoints and rows a token reaches. The network access policy restricts upstream targets.

## Authentication

API requests require a Bearer token:

```http
Authorization: Bearer your-token-here
```

Tokens are cryptographically random values, stored hashed in `auth.db`, and bound to a username for auditing.

Two other credentials replace the Bearer token on specific routes:

| Credential | Scope | Reference |
|---|---|---|
| Environment authentication method | The environment, limited to `AllowedEndpoints` | [Environment Authentication](/reference/environment-auth) |
| Webhook `Verification` | One webhook id. Bearer tokens are refused on a verified id | [Webhook verification](/guide/endpoints-webhook#verification) |

An environment whose `settings.json` fails to load or decrypt, or whose referenced secret is missing, returns `500` and does not accept Bearer tokens.

### Console accounts

Roles, recovery and session keys: [Accounts](/guide/accounts).

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

A request must pass both the token and the endpoint restrictions. Matrix: [Access Tokens](/guide/tokens#scoping-tokens).

## Network security

### Upstream hosts

`environments/network-access-policy.json` restricts Proxy and Composite targets: [Network access policy](/guide/environments#network-access-policy). A request with a secret follows same-host redirects and returns any other redirect to the client: [Redirects](/guide/endpoints-proxy#redirects).

### Security headers

Every response includes `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy` and `Content-Security-Policy`. HTTPS responses add `Strict-Transport-Security`. Values: [Security headers](/reference/headers#security-headers).

## Secrets management

Credentials belong in the `Secrets` object of an environment `settings.json`. Configuration refers to them as `${secret:NAME}`: [Secrets](/reference/secrets#secret-store). Portway encrypts connection strings, sensitive header values, authentication values and every `Secrets` value at the next start (`PWENC:...`). The chat API key in `mcp.db` is encrypted. Portway does not rewrite `appsettings.json`: values there stay plaintext. Azure Key Vault as a secret source: [Secrets](/reference/secrets#azure-key-vault).

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

Grant the roles the endpoints use. A read-only deployment requires `db_datareader`.

## Logging and auditing

Invalid tokens are logged at `Debug`, rate limit blocks at `Warning`. Per-request traffic logging with headers and bodies: [Health and Logs](/guide/monitoring#request-traffic-logging). Token changes are recorded in the [token audit log](/guide/tokens#token-audit-log).

## Pre-deployment checklist

- [ ] HTTPS terminated at IIS or a reverse proxy
- [ ] Service account or Application Pool identity with minimum privileges
- [ ] Console account with a strong password; legacy `PORTWAY_ADMIN_KEY` removed
- [ ] `PORTWAY_KNOWN_PROXIES` set to the reverse proxy (client addresses for rate limiting, sign-in lockout and the console network gate)
- [ ] Read-only console users have the `viewer` role
- [ ] `portway.key` stored with the deployment and excluded from shared backups
- [ ] Credentials stored in `Secrets` and referenced as `${secret:NAME}`
- [ ] `AllowedEndpoints` set on every environment authentication method
- [ ] Azure Key Vault configured (if applicable)
- [ ] Initial token file removed from disk
- [ ] Tokens created with specific scopes and environments
- [ ] Rate limiting configured
- [ ] Firewall rules reviewed
- [ ] Security headers verified with a response inspection tool

## Compromised token

1. Under **Access Tokens**, rotate the affected token; rotation revokes the old value and issues a replacement.
2. Update the applications that use the token.
3. Enable [traffic logging](/guide/monitoring#request-traffic-logging) and review requests with the old token value.
4. Record the incident.
