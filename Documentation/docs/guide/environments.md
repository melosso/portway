---
title: Environments
description: "Route API requests to different servers, databases, and configurations by environment name"
---

# Environments

The environment segment in `/api/{environment}/{endpoint}` selects a folder under `environments/`. The folder contains the connection string, server name, headers, secrets and authentication for that target.

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

`AllowedEnvironments` in the global file defines the routable environments:

```json [environments/settings.json]
{
  "Environment": {
    "ServerName": "SERVERNAME",
    "AllowedEnvironments": ["prod", "dev", "test"]
  }
}
```

`ServerName` is the default server name in forwarded headers. Property reference: [Environment settings](/reference/environment-settings#global-settings).

:::warning
An environment folder is routed only when `AllowedEnvironments` contains its name.
:::

### Environment settings

Each environment folder contains its own `settings.json`:

```json [environments/prod/settings.json]
{
  "ServerName": "PROD-SQL-CLUSTER",
  "ConnectionString": "Server=PROD-SQL-CLUSTER;Database=ProductionDB;Integrated Security=True;TrustServerCertificate=true;",
  "Headers": {
    "DatabaseName": "ProductionDB",
    "Origin": "Portway"
  }
}
```

Properties: [Environment settings](/reference/environment-settings#environment-settings).

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
        "Value": "${secret:CUSTOM-AUTH}",
        "In": "Header",
        "AllowedEndpoints": ["CRM/*"]
      }
    ]
  },
  "Secrets": { "CUSTOM-AUTH": "PWENC:..." }
}
```

`OverrideGlobalToken: true` rejects Portway Bearer tokens in the environment. `AllowedEndpoints` limits a method to matching endpoints. Requests authenticated by these methods have no Portway token, and endpoints with `Tenancy` refuse them. Method fields for `ApiKey`, `Basic`, `Bearer`, `JWT` and `HMAC`: [Environment Authentication](/reference/environment-auth).

Portway encrypts plaintext secrets in `settings.json` at the next start (`PWENC:...`). Keys and backup: [Secrets](/reference/secrets#encryption).

## Azure Key Vault

`PORTWAY_KEYVAULT_URI` adds Azure Key Vault as a source for connection strings, headers and secrets: [Azure Key Vault](/reference/secrets#azure-key-vault).

## Network access policy

The file `environments/network-access-policy.json` defines the upstream hosts proxy endpoints may call (SSRF protection). It is created at first start.

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
| `500` on every request in one environment | `settings.json` fails to load or decrypt, or a referenced secret has no value. The log entry contains the cause. |
| "Secret NAME has no value in environment" | Add the secret under **Environments** or in `Secrets`. |
| "Access denied to environment" | Grant the environment to the token in the [console](/guide/webui) under **Access Tokens**. |
| Proxy blocked, host not allowed | Add the host to `allowedHosts` in `network-access-policy.json` and restart. |
| Proxy blocked, IP in blocked range | The host resolves to an address in `blockedIpRanges`; change the range only for a trusted target. |
| Unexpected SQL syntax errors | The connection string does not identify the provider; required keywords: [SQL Providers](/reference/sql-providers). |

Debug logging: [Log levels](/guide/monitoring#log-levels).
