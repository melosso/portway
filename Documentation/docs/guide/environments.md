---
title: Environments
description: "Route API requests to different servers, databases, and configurations by environment name"
---

# Environments

The environment segment in `/api/{environment}/{endpoint}` selects a folder under `environments/` with the connection string, server name, headers and authentication for that target.

## Directory structure

```
environments/
├── settings.json              # Global: allowed environment names and server name
├── network-access-policy.json # SSRF protection for Proxy endpoints
├── prod/
│   └── settings.json          # Production connection string, headers, auth
├── test/
│   └── settings.json
└── dev/
    └── settings.json
```

:::info
Environment names are free-form (e.g. `dev`, `prod`, `WMS`, `500`). The folder name is the URL segment.
:::

### Global settings

The global file `environments/settings.json` lists the routable environments:

```json
{
  "Environment": {
    "ServerName": "SERVERNAME",
    "AllowedEnvironments": ["prod", "dev", "test"]
  }
}
```

`ServerName` is the default server name in forwarded headers. Property reference: [Environment settings](/reference/environment-settings#global-settings).

:::warning
An environment folder is routed only when its name is listed in `AllowedEnvironments`.
:::

### Environment settings

Per-environment `settings.json`:

```json
{
  "ServerName": "PROD-SQL-CLUSTER",
  "ConnectionString": "Server=PROD-SQL-CLUSTER;Database=ProductionDB;Integrated Security=True;TrustServerCertificate=true;",
  "Headers": {
    "DatabaseName": "ProductionDB",
    "Origin": "Portway"
  }
}
```

| Field | Required | Description |
|---|---|---|
| `ServerName` | No | Overrides the global server name for this environment |
| `ConnectionString` | No | Database connection string. Required for SQL and Webhook endpoints |
| `Headers` | No | Headers added to forwarded proxy requests; they replace client headers of the same name |

## Connection strings

The SQL provider (SQL Server, PostgreSQL, MySQL/MariaDB or SQLite) is detected from the connection string. Per-provider examples and detection rules: [SQL Providers](/reference/sql-providers#connection-strings).

An SQLite environment requires no database server:

```json
{ "ConnectionString": "Data Source=environments/demo/demo.db;" }
```

## Access control

A request must pass the token's environments and the endpoint's `AllowedEnvironments`. Matrix: [Access Tokens](/guide/tokens#scoping-tokens).

## Per-environment authentication

An `Authentication` block in the environment's `settings.json` adds API key, Basic, Bearer, JWT or HMAC authentication for that environment:

```json
{
  "ServerName": "PROD-SQL",
  "ConnectionString": "...",
  "Authentication": {
    "Enabled": true,
    "Methods": [
      {
        "Type": "ApiKey",
        "Name": "X-Custom-Auth",
        "Value": "your-secret-key",
        "In": "Header"
      }
    ]
  }
}
```

`OverrideGlobalToken: true` rejects Portway bearer tokens and accepts only the configured methods. Requests authenticated by these methods have no Portway token and are refused on endpoints with `Tenancy`. Method fields for `ApiKey`, `Basic`, `Bearer`, `JWT` and `HMAC`: [Environment Authentication](/reference/environment-auth).

Plaintext secrets in `settings.json` are encrypted at the next start (`PWENC:...`). `"Encrypt": false` keeps an environment plaintext, for non-production credentials only. Keys and backup: [Secret Encryption](/reference/secrets).

## Azure Key Vault

Connection strings and other secrets can be read from Azure Key Vault instead of `settings.json`:

1. Set the Key Vault URI:

   ::: code-group

   ```powershell [Windows]
   $env:PORTWAY_KEYVAULT_URI = "https://your-keyvault.vault.azure.net/"
   ```

   ```bash [Linux]
   export PORTWAY_KEYVAULT_URI="https://your-keyvault.vault.azure.net/"
   ```

   :::

2. Create secrets per environment:
   - `{environment}-ConnectionString`
   - `{environment}-ServerName`
   - `{environment}-Headers` (JSON string)

The secrets are read at startup and used like file-based values. Authentication uses the Azure SDK `DefaultAzureCredential`, which reads `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` and `AZURE_CLIENT_SECRET`.

## Network access policy

The file `environments/network-access-policy.json` lists the upstream hosts proxy endpoints may call (SSRF protection). It is created at first start.

```json
{
  "allowedHosts": [
    "localhost",
    "127.0.0.1",
    "api.internal.example.com",
    "*.services.corp"
  ],
  "blockedIpRanges": [
    "10.0.0.0/8",
    "172.16.0.0/12",
    "192.168.0.0/16",
    "169.254.0.0/16"
  ]
}
```

| Field | Description |
|---|---|
| `allowedHosts` | Hosts proxy endpoints may call; `*` matches one label (e.g. `*.corp`) |
| `blockedIpRanges` | CIDR ranges refused after DNS resolution |

A proxy request is allowed when the target host matches `allowedHosts` and no resolved address is in `blockedIpRanges`.

With only the two default localhost entries in `allowedHosts`, the machine's host name and interface addresses are added at startup. Explicit entries disable this.

Wildcard examples (`*` does not cross dots):

- `*.corp` matches `api.corp` and `db.corp`
- `api.*.corp` matches `api.v1.corp` and `api.v2.corp`

:::warning
Set `allowedHosts` explicitly in production. Auto-discovery adds every local address.
:::

:::tip
The `ASPNETCORE_DOMAIN` environment variable adds a host name to the allowed list at runtime.
:::

## Troubleshooting

| Symptom | Resolution |
|---|---|
| "Environment not in the allowed list" | Add the name to `AllowedEnvironments` in `environments/settings.json`. |
| "Settings.json not found for environment" | Create `environments/{name}/settings.json`. |
| "Access denied to environment" | Grant the environment to the token in the [console](/guide/webui) under **Access Tokens**. |
| Proxy blocked, host not allowed | Add the host to `allowedHosts` in `network-access-policy.json` and restart. |
| Proxy blocked, IP in blocked range | The host resolves to an address in `blockedIpRanges`; change the range only for a trusted target. |
| Unexpected SQL syntax errors | The connection string does not identify the provider; required keywords: [SQL Providers](/reference/sql-providers). |

Debug logging: [Log levels](/guide/monitoring#log-levels).
