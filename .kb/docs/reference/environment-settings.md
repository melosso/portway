---
title: Environment Settings
description: "Global and per-environment settings files and the network access policy"
---

# Environment Settings

Environment files define the routable environments, the database connection, headers, secrets and authentication per environment, and the hosts proxy endpoints may call.

## File structure

```
/environments/
  ├── [EnvironmentName]/
  │   └── settings.json              # Environment settings
  ├── settings.json                  # Global settings
  └── network-access-policy.json     # Proxy target policy
```

## Global settings

File: `/environments/settings.json`

```json
{
  "Environment": {
    "ServerName": "SERVERNAME",
    "AllowedEnvironments": ["prod", "dev", "test"]
  }
}
```

| Property | Type | Required | Description |
|---|---|---|---|
| `Environment` | object | Yes | Container |
| `Environment.ServerName` | string | Yes | Default server name |
| `Environment.AllowedEnvironments` | array | Yes | Routable environment names |

## Environment settings

File: `/environments/[EnvironmentName]/settings.json`

```json
{
  "ServerName": "SERVERNAME",
  "ConnectionString": "Server=SERVERNAME;Database=prod;Trusted_Connection=True;Connection Timeout=15;TrustServerCertificate=true;",
  "Headers": {
    "DatabaseName": "prod",
    "Origin": "Portway"
  },
  "Secrets": { "CRM-KEY": "PWENC:..." }
}
```

| Property | Type | Required | Description |
|---|---|---|---|
| `ServerName` | string | No | Server name for display and health checks. Defaults to the global `ServerName` |
| `ConnectionString` | string | For SQL and webhook endpoints | Database connection string. Selects the SQL provider ([SQL providers](/reference/sql-providers#connection-strings)) |
| `Headers` | object | No | Headers added to proxy requests. They replace client headers of the same name. Endpoint `Headers` replace environment headers of the same name ([Endpoint headers](/guide/endpoints-proxy#endpoint-headers)) |
| `Secrets` | object | No | Named values referenced as `${secret:NAME}` ([Secrets](/reference/secrets#secret-store)) |
| `Authentication` | object | No | Environment authentication ([Environment Authentication](/reference/environment-auth)) |
| `Encrypt` | boolean | No | Default `true`. `false` stores secrets in plaintext. For non-production credentials |

SQLite paths in `ConnectionString` are relative to the working directory. SQLite connection strings contain no credentials and are not encrypted or masked. Production SQL Server connections use `Encrypt=true;TrustServerCertificate=false` and credentials per environment.

Portway encrypts plaintext connection strings, authentication values and `Secrets` values at the next start (`PWENC:`): [Encryption](/reference/secrets#encryption). Azure Key Vault is an alternative source: [Azure Key Vault](/reference/secrets#azure-key-vault).

## Network access policy

File: `/environments/network-access-policy.json`. Fields, matching rules and examples: [Network access policy](/guide/environments#network-access-policy).

## Related topics

- [Environments](/guide/environments)
- [Entity Configuration](/reference/entity-config)
- [Security](/guide/security)
- [Application Settings](/reference/app-settings)
