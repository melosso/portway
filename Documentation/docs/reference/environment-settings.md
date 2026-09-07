---
title: Environment Settings
description: "Environments are how Portway keeps your development, testing, and production worlds from bleeding into each other"
---

# Environment Settings

Environments are how Portway keeps your development, testing, and production worlds from bleeding into each other. Their settings control database connections, the allowed environment list, and per-environment behavior. This page covers the files involved and what you can configure in each.

## File structure

Environment configuration files are organized in the following structure:

```
/environments/
  ├── [EnvironmentName]/             # Environment-specific folders
  │   └── settings.json              # Environment-specific settings
  ├── settings.json                  # Global settings
  └── network-access-policy.json     # Network security policy
```

## Global settings

The root `settings.json` file defines which environments are allowed:

### File location
`/environments/settings.json`

### Configuration structure

```json
{
  "Environment": {
    "ServerName": "SERVERNAME",
    "AllowedEnvironments": ["prod", "dev", "test"]
  }
}
```

### Property reference

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Environment` | object | Yes | Environment configuration container |
| `Environment.ServerName` | string | Yes | Default server name |
| `Environment.AllowedEnvironments` | array | Yes | List of allowed environment names |

## Environment-Specific settings

Each environment has its own configuration file with connection details:

### File location
`/environments/[EnvironmentName]/settings.json`

### Basic configuration

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

### Property reference

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `ServerName` | string | Yes | Database server name (used for display and health checks) |
| `ConnectionString` | string | Yes | Database connection string |
| `Headers` | object | No | Custom headers for requests |

### Headers configuration

Custom headers added to all requests for this environment:

| Header | Type | Description |
|--------|------|-------------|
| `DatabaseName` | string | Target database name |
| `ServerName` | string | Target server name |
| `Origin` | string | Request origin identifier |
| `[Custom]` | string | Any additional headers needed |

## Network access policy

Controls which hosts and IP ranges are allowed for proxy requests:

### File location
`/environments/network-access-policy.json`

### Configuration structure

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

### Property reference

| Property | Type | Description |
|----------|------|-------------|
| `allowedHosts` | array | Whitelisted hostnames |
| `blockedIpRanges` | array | Blocked IP ranges (CIDR notation) |

## Examples

### Production environment

`/environments/prod/settings.json`
```json
{
  "ServerName": "SQLPROD01",
  "ConnectionString": "Server=SQLPROD01;Database=ProductionDB;User Id=svc_portway;Password=${PROD_DB_PASSWORD};Connection Timeout=30;TrustServerCertificate=false;Encrypt=true;",
  "Headers": {
    "DatabaseName": "ProductionDB",
    "ServerName": "SQLPROD01",
    "Environment": "Production",
    "X-Strict-Mode": "true"
  }
}
```

### Development environment

`/environments/dev/settings.json`
```json
{
  "ServerName": "SQLDEV01",
  "ConnectionString": "Server=SQLDEV01;Database=DevelopmentDB;Trusted_Connection=True;Connection Timeout=15;TrustServerCertificate=true;",
  "Headers": {
    "DatabaseName": "DevelopmentDB",
    "ServerName": "SQLDEV01",
    "Environment": "Development",
    "X-Debug-Mode": "true"
  }
}
```

## Connection string configuration

The `ConnectionString` value determines both the target database and the SQL driver Portway uses. No additional property is needed, the provider is detected automatically from the connection string itself.

```json
{
  "ConnectionString": "Server=SERVER;Database=DB;Trusted_Connection=True;TrustServerCertificate=true;"
}
```

[SQL providers](/reference/sql-providers#connection-string-reference) carries a worked connection string for each supported database, the parameter tables, the detection algorithm and the capability differences between providers.

SQLite paths are resolved relative to the Portway application working directory. SQLite connection strings carry no credentials and are not subject to automatic encryption or masking.

## Variables

Sensitive values can use environment variables:

```json
{
  "ConnectionString": "Server=SQLPROD;Database=ProdDB;User Id=svc_portway;Password=${PROD_DB_PASSWORD};"
}
```

Supported variables:
- `${VARIABLE_NAME}` - Replaced at runtime
- Azure Key Vault integration (if configured)

## Security notes

:::warning
Never store passwords or secrets directly in configuration files. Use environment variables, Azure Key Vault, or Portway's automatic `PWENC:` encryption.
:::

The network access policy in `network-access-policy.json` prevents Server-Side Request Forgery (SSRF) by blocking private IP ranges and restricting proxy target hosts. Use separate database credentials per environment, and set `Encrypt=true; TrustServerCertificate=false` on production SQL Server connections.

## Troubleshooting

See the [troubleshooting guide](/guide/troubleshooting).

## Related topics

- [Entity Configuration](/reference/entity-config)
- [Security Guide](/guide/security)
- [Deployment Guide](/guide/deployment)
- [Application Settings](/reference/app-settings)
