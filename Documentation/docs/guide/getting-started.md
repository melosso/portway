# Getting Started

This guide will help you set up your first API gateway and configure endpoints to connect to your services.

## Prerequisites

Before you begin, make sure you have:

- Windows Server (or Windows 11 for development)
- [.NET 9+ ASP.NET Core Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
- Internet Information Services (IIS) with ASP.NET Core Hosting Bundle
- SQL Server database access (for SQL endpoints)
- Administrative access to configure IIS

> [!WARNING]
> There's a slight difference between the **x64-installer** and the **Hosting Bundle that ASP.NET Core 9.0** provides. Make sure to choose the last option ("Hosting Bundle").

## Installation

We offer two methods, beginning with installation on Windows Server.

### Download and Extract

1. Go to the [Releases page](https://github.com/melosso/portway/releases/)
2. Download the latest `*-Deployment.zip` file
3. Extract it to your IIS directory (e.g., `C:\path\to\your\PortwayApi`)

---

### Alternative: Docker Compose (Recommended for Home Lab Users)

You can quickly deploy Portway using Docker Compose. For a complete setup guide with detailed configuration options, see our [Docker Installation Guide](docker-compose.md).

Quick start:

```yaml
services:
  portway:
    image: ghcr.io/melosso/portway:latest
    ports:
      - "8080:8080"
    volumes:
      - portway_app:/app
      - ./environments:/app/environments
      - ./endpoints:/app/endpoints
      - ./tokens:/app/tokens
      - ./log:/app/log
      - ./data:/app/data
    environment:
      - PORTWAY_ENCRYPTION_KEY=YourEncryptionKeyHere
      - ASPNETCORE_URLS=http://+:8080

volumes:
  portway_app:
```

Then run:

```sh
docker compose pull && docker compose up -d
```

This will start Portway on port [8080](#) and mount your configuration folders. Adjust paths and ports as needed for your environment. 

### Configure IIS

> [!IMPORTANT]
> This guide assumes you have basic knowledge of IIS configuration and data source connectivity. While we cover the essential steps, some details may require your existing expertise.

1. Open IIS Manager
2. Create a new Application Pool:
   - Name: `PortwayAppPool`
   - .NET CLR version: `No Managed Code`
   - Managed pipeline mode: `Integrated`
   - User: `Running as a priviledged (NT) user`
3. Create a new TLS/SSL certificate or use an existing one (unless you disable TLS/SSL)
4. Create a new Website or Application:
   - Name: `Portway`
   - Application pool: `PortwayAppPool`
   - Physical path: `C:\path\to\your\PortwayApi`
   - Binding: https://localhost:443 (or your preferred port)
   - Certificate: The certificate you created
5. Set Application Pool Identity (for proxy endpoints):
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
7. Secure your environment with an environment varriable `ORTWAY_ENCRYPTION_KEY`:

```powershell
# Stores the encryption key to your System Environment Variables
$bytes = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes); [Environment]::SetEnvironmentVariable("PORTWAY_ENCRYPTION_KEY", [Convert]::ToBase64String($bytes), "Machine")
``` 

You should now be ready for running Portway for the first time.

### First Run

1. Browse to your configured URL (e.g., https://localhost)
2. On first run, Portway will:
   - Create required directories (`log`, `tokens`, `endpoints`, `environments`)
   - Initialize the authentication database (`auth.db`)
   - Generate a default access token for your machine

### Verify Installation

Open your browser and navigate to:
```
https://localhost
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

> [!CAUTION]
> The generated token files are highly sensitive and pose a significant security risk if left on disk. **Remove these files immediately after securely saving your token elsewhere.** Unauthorized access to these files can compromise your environment.

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
2. Open OpenAPI UI at https://localhost/docs
3. Click "Authorize" and enter your Bearer token
4. Make your first API call:

```http
GET /api/prod/Products
Authorization: Bearer YOUR_ACCESS_TOKEN
```

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