# Environments

Portway provides powerful environment isolation capabilities, allowing you to route API requests to different servers, databases, and configurations based on the environment specified in the URL.

## Understanding Environments

In Portway, environments represent different deployment targets such as:
- Development (`dev`)
- Testing (`test`)
- Production (`prod`)

Each environment can have its own:
- Database connection strings
- Server configurations
- Custom HTTP headers
- Access restrictions

## Environment Structure

Each environment is represented by a top-level directory inside the environments/ folder. The name of this directory determines the environment name and must contain a corresponding settings.json configuration file.

### Directory Layout

```
environments/
├── settings.json              # Global settings shared across environments
├── prod/
│   └── settings.json          # Configuration for the production environment
├── test/
│   └── settings.json          # Configuration for the testing environment
├── dev/
│   └── settings.json          # Configuration for the development environment
```

::: info Note
These environment names are examples. You can use descriptive names such as MyCompany, Synergy, or any other meaningful identifier.
:::

### Global Settings

The root `environments/settings.json` file defines which environments are available:

```json
{
  "Environment": {
    "ServerName": "SERVERNAME",
    "AllowedEnvironments": ["prod", "dev", "test"]
  }
}
```

- **ServerName**: Default server name used in requests
- **AllowedEnvironments**: List of valid environment names that can be used in URLs

::: warning Important
You must add environments to `AllowedEnvironments` for them to be accessible through the API.
:::

### Environment-Specific Settings

Each environment has its own `settings.json` file with these properties:

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

::: details Headers Property
The `Headers` property is optional for SQL Server and Webhook endpoints but may be required for Proxy endpoints depending on your backend services.
:::

## Configuration Examples

### Development Environment

```json
{
  "ServerName": "DEV-SQL-01",
  "ConnectionString": "Server=DEV-SQL-01;Database=DevelopmentDB;User Id=dev_user;Password=dev_password;TrustServerCertificate=true;",
  "Headers": {
    "DatabaseName": "DevelopmentDB",
    "ServerName": "DEV-APP-SERVER",
    "Debug": "true"
  }
}
```

### Production Environment

```json
{
  "ServerName": "PROD-SQL-CLUSTER",
  "ConnectionString": "Server=PROD-SQL-CLUSTER;Database=ProductionDB;Integrated Security=True;MultiSubnetFailover=True;ApplicationIntent=ReadOnly;TrustServerCertificate=true;",
  "Headers": {
    "DatabaseName": "ProductionDB",
    "ServerName": "PROD-APP-SERVER",
    "StrictMode": "true"
  }
}
```

## Using Environments in API Calls

Environment names are specified in the URL path:

```http
GET /api/{environment}/{endpoint}
```

Examples:
```http
GET /api/prod/Products
GET /api/dev/Orders
GET /api/test/Customers
```

::: info
The environment name in the URL determines which configuration is used for database connections and request headers.
:::

## Environment Access Control

Access to environments is controlled through the Token Generator tool. When creating or modifying tokens, you can specify which environments a token can access.

### Using Token Generator for Environment Access

1. **Run the Token Generator**:
   ```batch
   TokenGenerator.exe
   ```

2. **Create a token with specific environment access**:
   - Choose option 2 (Generate new token)
   - When prompted for environments, enter:
     - `*` for all environments
     - `prod,dev` for specific environments
     - `pro*` for all environments starting with "pro"

3. **Update existing token environments**:
   - Choose option 5 (Update token environments)
   - Select the token ID
   - Enter new environment restrictions

Example token file with environment restrictions:
```json
{
  "Username": "api-user",
  "Token": "your-token-here",
  "AllowedEnvironments": "prod,dev",
  "AllowedScopes": "*",
  "ExpiresAt": "Never",
  "CreatedAt": "2024-01-01 10:00:00"
}
```

### Endpoint-Level Environment Restrictions

Individual endpoints can also be restricted to specific environments in their configuration:

```json
{
  "DatabaseObjectName": "ServiceRequests",
  "DatabaseSchema": "dbo",
  "AllowedEnvironments": ["prod"], // [!code warning]
  "AllowedMethods": ["GET", "POST", "PUT"]
}
```

This creates a two-layer security model:
1. Token must have access to the environment
2. Endpoint must allow the environment

## Azure Key Vault Integration

For enhanced security, store connection strings in Azure Key Vault:

1. Set the environment variable:
   ```powershell
   $env:KEYVAULT_URI = "https://your-keyvault.vault.azure.net/"
   ```

2. Create secrets in Key Vault:
   - `{environment}-ConnectionString` - Database connection string
   - `{environment}-ServerName` - Server name
   - `{environment}-Headers` - JSON string of custom headers

3. The system will automatically fetch these values instead of reading from local files

::: warning Security Note
Never commit connection strings with passwords to source control. Use Azure Key Vault or environment variables for sensitive data.
:::

## Environment Headers

Headers defined in environment settings are automatically added to all requests:

```json
{
  ...
  "Headers": {
    "DatabaseName": "prod",
    "ServerName": "PROD-APP-SERVER",
    "X-Environment": "Production",
    "X-Client-Version": "1.0.0"
  }
}
```

These headers can be used by:
- Proxy endpoints to route requests
- SQL Server for auditing
- Internal services for environment detection

## Best Practices

### 1. Environment Naming
- Use meaningful names (`dev`, `test`, `prod`) or your organization's standard (`prod`, `dev`)
- Avoid special characters or spaces
- Keep names short but descriptive

### 2. Connection Strings
- Use Windows Authentication when possible
- Enable `TrustServerCertificate=true` for development only
- Use connection pooling settings for performance
- Store sensitive credentials in Azure Key Vault

### 3. Security
- Use the Token Generator to create environment-specific access tokens
- Implement endpoint-level environment restrictions where needed
- Regularly audit and rotate tokens
- Use Azure Key Vault for production environments

### 4. Headers
- Include environment indicators in headers
- Add version information for tracking
- Use headers for request correlation
- Keep header names consistent across environments

## Troubleshooting

### Common Issues

1. **Environment Not Found**
   ```
   Error: Environment 'staging' is not in the allowed list
   ```
   Solution: Add the environment to `AllowedEnvironments` in `environments/settings.json`

2. **Connection Failed**
   ```
   Error: A network-related or instance-specific error occurred
   ```
   Solution: Verify the connection string and network connectivity

3. **Access Denied**
   ```
   Error: Access denied to environment 'prod'
   ```
   Solution: Check if the token has access to this environment using Token Generator

4. **Missing Configuration**
   ```
   Error: Settings.json not found for environment: prod
   ```
   Solution: Create `environments/prod/settings.json` with required configuration

### Debugging

Enable detailed logging to troubleshoot environment issues:

```json
{
  "Logging": {
    "LogLevel": {
      "PortwayApi.Classes.EnvironmentSettings": "Debug"
    }
  }
}
```

Check logs for environment loading information:
```
✅ Loaded environments: prod, dev, test
✅ Using server: SERVERNAME
🔒 Token lacks permission for environment prod. Available environments: prod,dev
```

## Environment vs Token Security Matrix

| Token Environments | Endpoint AllowedEnvironments | API Request Environment | Result |
|-------------------|----------------------------|----------------------|---------|
| `*` (all) | Not specified | Any | ✅ Allowed |
| `*` (all) | `["prod"]` | `prod` | ✅ Allowed |
| `*` (all) | `["prod"]` | `dev` | ❌ Blocked |
| `prod,dev` | Not specified | `prod` | ✅ Allowed |
| `prod,dev` | Not specified | `prod` | ❌ Blocked |
| `prod,dev` | `["prod"]` | `prod` | ✅ Allowed |
| `prod,dev` | `["prod"]` | `dev` | ❌ Blocked |

## Next Steps

- Learn how to use the [Token Generator](./security#managing-tokens) for environment access control
- [Configure SQL Endpoints](./endpoints-sql) to use your environments
- [Set up Proxy Endpoints](./endpoints-proxy) with environment-specific routing
- [Deploy to Production](./deployment) with proper environment isolation