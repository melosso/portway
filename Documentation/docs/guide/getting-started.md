# Getting Started

This guide will help you set up your first API gateway and configure endpoints to connect to your services.

## Prerequisites

Before you begin, make sure you have:

- Windows Server (or Windows 11 for development)
- [.NET 9+ ASP.NET Core Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
- Internet Information Services (IIS) with ASP.NET Core Hosting Bundle
- SQL Server database access (for SQL endpoints)
- Administrative access to configure IIS

## Installation

### Download and Extract

1. Go to the [Releases page](https://github.com/melosso/portway/releases/)
2. Download the latest `Deployment.zip` file
3. Extract it to your IIS directory (e.g., `C:\path\to\your\PortwayApi`)

### Configure IIS

1. Open IIS Manager
2. Create a new Application Pool:
   - Name: `PortwayAppPool`
   - .NET CLR version: `No Managed Code`
   - Managed pipeline mode: `Integrated`
   - User: `Running as a priviledged (NT) user`
3. Create a new TLS/SSL certificate or use an existing one
4. Create a new Website or Application:
   - Name: `Portway`
   - Application pool: `PortwayAppPool`
   - Physical path: `C:\path\to\your\PortwayApi`
   - Binding: https://localhost:80 (or your preferred port)
   - Certificate: The certificate you created
6. Set Application Pool Identity (for proxy endpoints):
   - Select your Application Pool
   - Advanced Settings > Identity
   - Choose appropriate user account with network access

6. Ensure the web.config exists with (a predefined) proper configuration:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet" arguments=".\PortwayApi.dll" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" hostingModel="inprocess" />
    </system.webServer>
  </location>
</configuration>
```

### First Run

1. Browse to your configured URL (e.g., https://localhost)
2. On first run, Portway will:
   - Create required directories (`log`, `tokens`, `endpoints`, `environments`)
   - Initialize the authentication database (`auth.db`)
   - Generate a default access token for your machine

### Verify Installation

Open your browser and navigate to:
```
https://localhost/docs
```

You should see the OpenAPI UI interface with the Portway API documentation.

## Initial Configuration

### Access Token

After the first run, find your authentication token in:
```
tokens/YOUR_SERVER_NAME.txt
```

This JSON file contains your Bearer token for API authentication:

```json
{
  "Username": "SERVER-NAME",
  "Token": "your-bearer-token-here",
  "AllowedScopes": "*",
  "AllowedEnvironments": "*",
  "ExpiresAt": "Never",
  "CreatedAt": "2025-01-01 00:00:00",
  "Usage": "Use this token in the Authorization header as: Bearer your-bearer-token-here"
}
```

### Configure Environments

1. Navigate to the `environments` folder in your deployment directory
2. Create the main `settings.json` file:

```json
{
  "Environment": {
    "ServerName": "localhost",
    "AllowedEnvironments": ["dev", "test", "prod"]
  }
}
```

3. Create environment-specific configurations:

```
environments/
  ├── settings.json
  ├── dev/
  │   └── settings.json
  ├── test/
  │   └── settings.json
  └── prod/
      └── settings.json
```

Example `environments/prod/settings.json`:

```json
{
  "ServerName": "SQLSERVER01",
  "ConnectionString": "Server=SQLSERVER01;Database=ProductionDB;Trusted_Connection=True;TrustServerCertificate=true;",
  "Headers": {
    "Origin": "Portway"
  }
}
```

### Create Your First Endpoint

Create a SQL endpoint by adding `endpoints/SQL/Products/entity.json`:

```json
{
  "DatabaseObjectName": "Products",
  "DatabaseSchema": "dbo",
  "PrimaryKey": "ProductId",
  "AllowedColumns": [
    "ProductId",
    "ProductName",
    "Price",
    "Stock"
  ],
  "AllowedEnvironments": ["dev", "test", "prod"]
}
```

### Test Your API

1. Restart the IIS Application Pool to load new configurations
2. Open OpenAPI UI at http://localhost/docs
3. Click "Authorize" and enter your Bearer token
4. Make your first API call:

```http
GET /api/prod/Products
Authorization: Bearer YOUR_ACCESS_TOKEN
```

## Licensing

Portway is available under two licensing models:

* **Open Source (AGPL-3.0)** — Free for open source projects and personal use
* **Commercial License** — For commercial use without open source requirements

Professional features such as priority support and guaranteed patches require a [commercial license](https://melosso.com/licensing/portway). Feel free to contact us.

## Next Steps

Now that Portway is running in IIS:

- [Configure SQL Endpoints](./endpoints-sql) for database access
- [Set up Proxy Endpoints](./endpoints-proxy) for service forwarding
- [Create Composite Endpoints](./endpoints-composite) for multi-step operations
- [Configure Security](./security) for production deployment
- [Set up Monitoring](./monitoring) with health checks

::: tip Production Deployment
For production environments, ensure you:
- Configure HTTPS bindings in IIS
- Set up proper security headers
- Enable request logging
- Configure Azure Key Vault for secrets management
:::