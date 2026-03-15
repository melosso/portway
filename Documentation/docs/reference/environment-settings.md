# Environment Settings

Environment settings control database connections, allowed environments, and environment-specific configurations. These settings ensure proper isolation between development, testing, and production environments.

## File Structure

Environment configuration files are organized in the following structure:

```
/environments/
  ├── [EnvironmentName]/             # Environment-specific folders
  │   └── settings.json              # Environment-specific settings
  ├── settings.json                  # Global settings
  └── network-access-policy.json     # Network security policy
```

## Global Settings

The root `settings.json` file defines which environments are allowed:

### File Location
`/environments/settings.json`

### Configuration Structure

```json
{
  "Environment": {
    "ServerName": "SERVERNAME",
    "AllowedEnvironments": ["prod", "dev", "test"]
  }
}
```

### Property Reference

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Environment` | object | Yes | Environment configuration container |
| `Environment.ServerName` | string | Yes | Default server name |
| `Environment.AllowedEnvironments` | array | Yes | List of allowed environment names |

## Environment-Specific Settings

Each environment has its own configuration file with connection details:

### File Location
`/environments/[EnvironmentName]/settings.json`

### Basic Configuration

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

### Property Reference

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `ServerName` | string | Yes | Database server name (used for display and health checks) |
| `ConnectionString` | string | Yes | Database connection string |
| `Headers` | object | No | Custom headers for requests |

### Headers Configuration

Custom headers added to all requests for this environment:

| Header | Type | Description |
|--------|------|-------------|
| `DatabaseName` | string | Target database name |
| `ServerName` | string | Target server name |
| `Origin` | string | Request origin identifier |
| `[Custom]` | string | Any additional headers needed |

## Network Access Policy

Controls which hosts and IP ranges are allowed for proxy requests:

### File Location
`/environments/network-access-policy.json`

### Configuration Structure

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

### Property Reference

| Property | Type | Description |
|----------|------|-------------|
| `allowedHosts` | array | Whitelisted hostnames |
| `blockedIpRanges` | array | Blocked IP ranges (CIDR notation) |

## Examples

### Production Environment

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

### Development Environment

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

## Connection String Configuration

The `ConnectionString` value determines both the target database and the SQL driver Portway uses. No additional property is needed, the provider is detected automatically.

See the [SQL Providers reference](/reference/sql-providers) for the full detection algorithm and capability differences between providers.

### SQL Server: Windows authentication

```json
{
  "ConnectionString": "Server=SERVER;Database=DB;Trusted_Connection=True;Connection Timeout=15;TrustServerCertificate=true;"
}
```

### SQL Server: SQL authentication

```json
{
  "ConnectionString": "Server=SERVER;Database=DB;User Id=username;Password=password;Connection Timeout=30;TrustServerCertificate=false;Encrypt=true;"
}
```

### PostgreSQL

```json
{
  "ConnectionString": "Host=db.example.com;Port=5432;Database=mydb;Username=portway;Password=your-password;"
}
```

### MySQL / MariaDB

```json
{
  "ConnectionString": "Server=db.example.com;Port=3306;Database=mydb;Uid=portway;Pwd=your-password;SslMode=Preferred;"
}
```

### SQLite

```json
{
  "ConnectionString": "Data Source=environments/WMS/demo.db;"
}
```

Paths are resolved relative to the Portway application working directory. SQLite connection strings carry no credentials and are not subject to automatic encryption or masking.

### Key parameters by provider

**SQL Server**

| Parameter | Description | Default |
|---|---|---|
| `Server` | SQL Server instance or hostname | Required |
| `Database` | Database name | Required |
| `User Id` / `Password` | SQL authentication credentials | - |
| `Trusted_Connection` | Use Windows authentication | `False` |
| `Encrypt` | Encrypt the connection | `False` |
| `TrustServerCertificate` | Skip certificate validation | `False` |
| `Connection Timeout` | Seconds before giving up | `15` |
| `MultipleActiveResultSets` | Enable MARS | `False` |

**PostgreSQL**

| Parameter | Description | Default |
|---|---|---|
| `Host` | Hostname or IP | Required |
| `Port` | Server port | `5432` |
| `Database` | Database name | Required |
| `Username` / `Password` | Credentials | - |
| `SSL Mode` | `Require`, `Prefer`, `Disable` | `Prefer` |

**MySQL / MariaDB**

| Parameter | Description | Default |
|---|---|---|
| `Server` | Hostname or IP | Required |
| `Port` | Server port | `3306` |
| `Database` | Database name | Required |
| `Uid` / `Pwd` | Credentials | - |
| `SslMode` | `Preferred`, `Required`, `None` | `Preferred` |

**SQLite**

| Parameter | Description |
|---|---|
| `Data Source` | Path to `.db` file, or `:memory:` for an in-memory database |

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

## Security Notes

:::warning
Never store passwords or secrets directly in configuration files. Use environment variables, Azure Key Vault, or Portway's automatic `PWENC:` encryption.
:::

The network access policy in `network-access-policy.json` prevents Server-Side Request Forgery (SSRF) by blocking private IP ranges and restricting proxy target hosts. Use separate database credentials per environment, and set `Encrypt=true; TrustServerCertificate=false` on production SQL Server connections.

## Troubleshooting

### Common Issues

1. **Environment Not Found**
   - Check environment name in `AllowedEnvironments`
   - Verify folder structure: `/environments/[name]/settings.json`
   - Ensure correct file permissions

2. **Database Connection Failed**
   - Test connection string with SQL tools
   - Verify server name and database
   - Check firewall rules
   - Validate credentials

3. **Headers Not Applied**
   - Confirm headers in environment settings
   - Check for typos in header names
   - Verify environment is selected correctly

4. **Network Access Denied**
   - Review allowed hosts list
   - Check if IP is in blocked ranges
   - Validate DNS resolution
   - Test with diagnostic tools

## Related Topics

- [Entity Configuration](/reference/entity-config) - Endpoint configuration
- [Security Guide](/guide/security) - Security best practices
- [Deployment Guide](/guide/deployment) - Production deployment
- [Application Settings](/reference/app-settings) - Application configuration