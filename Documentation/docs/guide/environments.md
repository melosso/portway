# Environments

> Route API requests to different servers, databases, and configurations by environment name.

Each request URL includes an environment segment — `/api/{environment}/{endpoint}`. Portway maps that name to a folder under `environments/`, which defines the connection string, server name, custom headers, and access rules for that target. Development, testing, and production configurations are completely separate.

## Directory structure

```
environments/
├── settings.json       # Global: allowed environment names and server name
├── prod/
│   └── settings.json   # Production connection string, headers, auth
├── test/
│   └── settings.json
└── dev/
    └── settings.json
```

:::info
Environment names are arbitrary. You can use `dev`, `test`, `prod`, or any identifier meaningful to your organisation — `WMS`, `Synergy`, `500`. The folder name becomes the URL segment.
:::

### Global settings

`environments/settings.json` controls which environments are accessible through the API:

```json
{
  "Environment": {
    "ServerName": "SERVERNAME",
    "AllowedEnvironments": ["prod", "dev", "test"]
  }
}
```

| Field | Required | Description |
|---|---|---|
| `ServerName` | Yes | Default server name included in forwarded headers |
| `AllowedEnvironments` | Yes | Names of environments accessible via the API. Requests to any name not listed return 404 |

:::warning
Adding a folder under `environments/` is not enough — the name must also appear in `AllowedEnvironments` before Portway will route requests to it.
:::

### Environment settings

Each environment's `settings.json` defines its connection and forwarding configuration:

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
| `Headers` | No | Key-value pairs added to all forwarded requests. Primarily used by Proxy endpoints |

## SQL provider detection

Portway selects the SQL driver automatically from the connection string — no additional configuration needed. Switching databases for an environment is as simple as updating `ConnectionString`.

| Provider | Detection signal |
|---|---|
| SQL Server | `TrustServerCertificate=`, `Integrated Security=`, `MultiSubnetFailover=` |
| PostgreSQL | `Host=`, `Port=5432` URI schemes |
| MySQL / MariaDB | `Server=...;Uid=`, `SslMode=` |
| SQLite | `Data Source=...db` file path |

:::tip
Point an SQLite environment at a local `.db` file to run a self-contained demo with no database server:
```json
{ "ConnectionString": "Data Source=environments/demo/demo.db;" }
```
:::

For full detection priority rules and per-provider capability differences, see the [SQL Providers reference](/reference/sql-providers).

## Configuration examples

**SQL Server — production (Windows Authentication):**
```json
{
  "ServerName": "PROD-SQL-CLUSTER",
  "ConnectionString": "Server=PROD-SQL-CLUSTER;Database=ProductionDB;Integrated Security=True;MultiSubnetFailover=True;TrustServerCertificate=true;"
}
```

**SQL Server — development (SQL auth):**
```json
{
  "ServerName": "DEV-SQL-01",
  "ConnectionString": "Server=DEV-SQL-01;Database=DevelopmentDB;User Id=dev_user;Password=dev_password;TrustServerCertificate=true;"
}
```

**PostgreSQL:**
```json
{
  "ServerName": "pg-host",
  "ConnectionString": "Host=pg-host;Port=5432;Database=mydb;Username=portway;Password=your-password;"
}
```

**MySQL:**
```json
{
  "ServerName": "mysql-host",
  "ConnectionString": "Server=mysql-host;Port=3306;Database=mydb;Uid=portway;Pwd=your-password;SslMode=Preferred;"
}
```

**SQLite:**
```json
{
  "ServerName": "localhost",
  "ConnectionString": "Data Source=environments/demo/demo.db;"
}
```

## Access control

Environment access is enforced at two independent layers. A request must pass both to succeed.

### Token-level restrictions

When creating a token, specify which environments it can access. Use `*` for all environments, a comma-separated list for specific ones, or a prefix pattern like `pro*`.

Example token with restricted access:
```json
{
  "Username": "api-user",
  "Token": "your-token-here",
  "AllowedEnvironments": "prod,dev"
}
```

### Endpoint-level restrictions

Individual endpoints can also limit which environments they respond to:

```json
{
  "DatabaseObjectName": "ServiceRequests",
  "AllowedEnvironments": ["prod"]
}
```

Both restrictions apply. The token must permit the environment **and** the endpoint must list it.

| Token environments | Endpoint `AllowedEnvironments` | Request environment | Result |
|---|---|---|---|
| `*` | _(not set)_ | Any | Allowed |
| `*` | `["prod"]` | `prod` | Allowed |
| `*` | `["prod"]` | `dev` | Blocked |
| `prod,dev` | _(not set)_ | `prod` | Allowed |
| `prod,dev` | _(not set)_ | `test` | Blocked |
| `prod,dev` | `["prod"]` | `prod` | Allowed |
| `prod,dev` | `["prod"]` | `dev` | Blocked |

## Per-environment authentication

Portway supports environment-specific authentication methods for backends that require their own credentials — API keys, Basic Auth, Bearer tokens, JWT, or HMAC.

Add an `Authentication` block to the environment's `settings.json`:

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

| Type | Key fields |
|---|---|
| `ApiKey` | `Name`, `Value`, `In` (`Header` or `Query`) |
| `Basic` | `Name`, `Value` |
| `Bearer` | `Value` |
| `JWT` | `Issuer`, `Secret`, `PublicKey` |
| `HMAC` | `Name`, `Secret` |

By default, both the environment-specific auth method and the global Portway token are accepted. Set `OverrideGlobalToken: true` to require only the environment-specific method:

```json
{
  "Authentication": {
    "Enabled": true,
    "OverrideGlobalToken": true,
    "Methods": [...]
  }
}
```

:::tip
Portway automatically encrypts plaintext secrets in `settings.json` on next startup. Values become `PWENC:...` format. The original plaintext is no longer stored on disk.
:::

For JWT and HMAC configuration, see the [Environment Authentication reference](../reference/environment-auth).

## Azure Key Vault

Store connection strings and other secrets in Azure Key Vault instead of `settings.json`:

1. Set the Key Vault URI:
   ```powershell
   $env:KEYVAULT_URI = "https://your-keyvault.vault.azure.net/"
   ```

2. Create secrets named by environment:
   - `{environment}-ConnectionString`
   - `{environment}-ServerName`
   - `{environment}-Headers` (JSON string)

Portway fetches these values at startup and treats them identically to file-based configuration.

## Environment headers

Headers defined in `settings.json` are added to all forwarded requests for that environment. This is primarily used by Proxy endpoints to pass context to internal services.

```json
{
  "Headers": {
    "DatabaseName": "prod",
    "X-Environment": "Production",
    "Origin": "Portway"
  }
}
```

## Troubleshooting

**"Environment not in the allowed list"** — Add the environment name to `AllowedEnvironments` in `environments/settings.json`.

**"Settings.json not found for environment"** — Create `environments/{name}/settings.json`. The folder must exist and contain the file.

**"Access denied to environment"** — The token does not have permission for this environment. Update token permissions in the [Web UI](./webui) under **Tokens**.

**Unexpected SQL syntax errors** — The connection string may not contain enough signal for provider auto-detection. Check the [SQL Providers reference](/reference/sql-providers) for required keywords.

To increase log verbosity for environment issues:

```json
{
  "Logging": {
    "LogLevel": {
      "PortwayApi.Classes.EnvironmentSettings": "Debug"
    }
  }
}
```

## Next steps

- [Configure SQL Endpoints](./endpoints-sql)
- [Set up Proxy Endpoints](./endpoints-proxy)
- [Security — token management](./security#managing-tokens)
- [Deploy to production](./deployment)
