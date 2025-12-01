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

---

### Docker Compose (Recommended)

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

### Configure on Windows Server 

> [!IMPORTANT]
> This guide assumes you have basic knowledge of IIS configuration and data source connectivity. While we cover the essential steps, some details may require your existing expertise.

Download the latest release from the [Releases page](https://github.com/melosso/portway/releases/).

1. **Install .NET 9 Runtime:**
```powershell
   winget install --id Microsoft.DotNet.HostingBundle.9 -e
```

2. **Set encryption key:**
```powershell
   $bytes = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes); [Environment]::SetEnvironmentVariable("PORTWAY_ENCRYPTION_KEY", [Convert]::ToBase64String($bytes), "Machine")
```

3. **Install Portway:**
Extract the application to your IIS directory (e.g., C:\Portway).

4. **Configure Internet Information Services**:
Configure the webserver accordingly:

1. Open IIS Manager
2. Create a new Application Pool:
   - Name: `PortwayAppPool`
   - .NET CLR version: `No Managed Code`
   - Managed pipeline mode: `Integrated`
   - Optional: User: `Running as a priviledged (NT) user` (if you're going to use NTLM as passthrough)
3. Create a new TLS/SSL certificate or use an existing one (unless you disable TLS/SSL)
4. Create a new Website or Application:
   - Name: `Portway`
   - Application pool: `PortwayAppPool`
   - Physical path: `C:\Portway`
   - Binding: https://localhost:443 (or your preferred port)
   - Certificate: The certificate you created
5. Set Application Pool Identity (for proxy endpoints):
   - Select your Application Pool
   - Advanced Settings > Identity
   - Choose appropriate user account with network access

You should now be ready for running Portway for the first time. Just open your browser on **http://localhost:8080**.

## Initial Configuration

> [!CAUTION]
> On first run, a token file is generated. This contains a secret and poses a significant security risk if left on disk. **Remove this file immediately after securely saving your token elsewhere.** Unauthorized access to this file can comprimise your environment.

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
2. Open OpenAPI UI at https://localhost/docs
3. Click "Authorize" and enter your Bearer token
4. Make your first API call:

```http
GET /api/prod/Products
Authorization: Bearer YOUR_ACCESS_TOKEN
```

You'll be all set now!

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