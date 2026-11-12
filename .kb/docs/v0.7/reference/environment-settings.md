---
title: Environment Settings
description: "Global and per-environment settings files and the network access policy"
---

# Environment Settings

Environment files define the routable environments, each environment's database connection and headers, and the hosts proxy endpoints may call.

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
    "ServerName": "SERVERNAME",
    "Origin": "Portway"
  }
}
```

| Property | Type | Required | Description |
|---|---|---|---|
| `ServerName` | string | No | Server name for display and health checks; defaults to the global `ServerName` |
| `ConnectionString` | string | For SQL and webhook endpoints | Database connection string; also selects the SQL provider |
| `Headers` | object | No | Headers added to proxy requests; they replace client headers of the same name |
| `Authentication` | object | No | Environment authentication ([Environment Authentication](/reference/environment-auth)) |
| `Encrypt` | boolean | No | `false` keeps secrets plaintext (default `true`) |

Common headers: `DatabaseName`, `ServerName`, `Origin`; any header name is accepted.

## Network access policy

File: `/environments/network-access-policy.json`

```json
{
  "allowedHosts": [
    "localhost",
    "127.0.0.1"
  ],
  "blockedIpRanges": [
    "10.0.0.0/8",
    "172.16.0.0/12",
    "192.168.0.0/16",
    "169.254.0.0/16"
  ]
}
```

| Property | Type | Description |
|---|---|---|
| `allowedHosts` | array | Host names proxy endpoints may call |
| `blockedIpRanges` | array | CIDR ranges refused after DNS resolution |

## Connection strings

The provider is detected from `ConnectionString`. Per-provider examples, parameters, detection rules and capability differences: [SQL providers](/reference/sql-providers#connection-strings).

SQLite paths are relative to the working directory. SQLite connection strings contain no credentials and are neither encrypted nor masked.

## Secrets

Plaintext connection strings and authentication values are encrypted at the next start (`PWENC:`). Azure Key Vault is an alternative source ([Environments, Azure Key Vault](/guide/environments#azure-key-vault)). Production SQL Server connections use `Encrypt=true;TrustServerCertificate=false` and credentials per environment.

## Related topics

- [Environments](/guide/environments)
- [Entity Configuration](/reference/entity-config)
- [Security](/guide/security)
- [Application Settings](/reference/app-settings)
