# Environment Settings

Environment settings control database connections, allowed environments, and environment-specific configurations. These settings ensure proper isolation between development, testing, and production environments.

## File Structure

Environment configuration files are organized in the following structure:

```
/environments/
  ├── [EnvironmentName]/             # Environment-specific folders
  │   └── settings.json              # Environment-specific settings
  ├── settings.json                  # Global environment settings
  └── network-access-policy.json     # Network security policy
```

## Global Environment Settings

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
| `ServerName` | string | Yes | SQL Server instance name |
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

## Environment Examples

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

### SQL Server Authentication

```json
{
  "ConnectionString": "Server=SERVER;Database=DB;User Id=username;Password=password;Connection Timeout=30;TrustServerCertificate=false;Encrypt=true;"
}
```

### Windows Authentication

```json
{
  "ConnectionString": "Server=SERVER;Database=DB;Trusted_Connection=True;Connection Timeout=15;TrustServerCertificate=true;"
}
```

### Connection String Parameters

| Parameter | Description | Default |
|-----------|-------------|---------|
| `Server` | SQL Server instance | Required |
| `Database` | Database name | Required |
| `User Id` | SQL authentication username | - |
| `Password` | SQL authentication password | - |
| `Trusted_Connection` | Use Windows authentication | False |
| `Connection Timeout` | Connection timeout in seconds | 15 |
| `TrustServerCertificate` | Trust server certificate | False |
| `Encrypt` | Encrypt connection | False |
| `Application Name` | Application identifier | "Portway API" |
| `MultipleActiveResultSets` | Enable MARS | False |

## Environment Variables

Sensitive values can use environment variables:

```json
{
  "ConnectionString": "Server=SQLPROD;Database=ProdDB;User Id=svc_portway;Password=${PROD_DB_PASSWORD};"
}
```

Supported variables:
- `${VARIABLE_NAME}` - Replaced at runtime
- Azure Key Vault integration (if configured)

## Security Considerations

### Connection String Security

:::warning Sensitive Information
Never store passwords or secrets directly in configuration files. Use:
- Environment variables
- Azure Key Vault
- Secure configuration providers
:::

### Network Access Policy

The network access policy prevents Server-Side Request Forgery (SSRF) attacks:

1. **Allowed Hosts**: Only whitelisted hosts can be accessed
2. **Blocked IP Ranges**: Internal/private networks are blocked by default
3. **DNS Resolution**: All hostnames are resolved and checked

### Environment Isolation

Each environment should have:
- Separate database credentials
- Unique connection strings
- Environment-specific headers
- Appropriate timeout values

## Best Practices

### 1. Environment Naming

Use consistent naming conventions:
- Production: `prod`, `production`
- Development: `dev`, `development`
- Testing: `test`, `staging`
- Numeric: `prod`, `dev` (legacy systems)

### 2. Connection String Management

```json
{
  // Development (relaxed security)
  "ConnectionString": "Server=DEV;Database=DevDB;Trusted_Connection=True;TrustServerCertificate=true;",
  
  // Production (strict security)
  "ConnectionString": "Server=PROD;Database=ProdDB;User Id=svc_portway;Password=${PROD_PASSWORD};Encrypt=true;TrustServerCertificate=false;"
}
```

### 3. Header Configuration

```json
{
  "Headers": {
    // Identify environment
    "Environment": "Production",
    
    // Track requests
    "X-Request-Source": "Portway",
    
    // Environment-specific behavior
    "X-Strict-Mode": "true"
  }
}
```

### 4. Network Security

```json
{
  "allowedHosts": [
    "api.internal.company.com",
    "legacy-system.local"
  ],
  "blockedIpRanges": [
    "10.0.0.0/8",      // Private network
    "172.16.0.0/12",   // Private network
    "192.168.0.0/16",  // Private network
    "169.254.0.0/16"   // Link-local
  ]
}
```

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

## Environment Setup Checklist

- [ ] Create environment folder
- [ ] Add `settings.json` with connection details
- [ ] Configure headers if needed
- [ ] Add environment to `AllowedEnvironments`
- [ ] Test database connectivity
- [ ] Verify header application
- [ ] Update network access policy if needed
- [ ] Document environment-specific settings

## Related Topics

- [Entity Configuration](/reference/configuration/entity-configuration) - Endpoint configuration
- [Security Guide](/guide/security) - Security best practices
- [Deployment Guide](/guide/deployment/production) - Production deployment
- [Application Settings](/reference/configuration/application-settings) - Application configuration