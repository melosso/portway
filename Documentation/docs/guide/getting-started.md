---
title: Getting Started
description: "Install Portway and make your first authenticated API call"
---

# Getting Started

Portway is an ASP.NET Core application. On Windows Server it is typically hosted behind IIS, though running it standalone on Kestrel works just as well; on any other platform it runs as a Docker container. This guide covers the IIS and Docker paths through to a working endpoint.

## Prerequisites

**Windows Server / IIS:**
- Windows Server (or Windows 11 for development)
- [.NET 10 ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- Internet Information Services (IIS)

:::warning
Download the **Hosting Bundle**, not the x64 runtime installer. The Hosting Bundle includes the IIS integration module that the runtime package omits.
:::

**Docker:**
- Docker Engine with Compose support

A SQL database only comes into play if you plan to use SQL endpoints, so it's fine to skip that for now.

## Installation

### Docker Compose

The quickest way to a running gateway is a small compose file:

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

volumes:
  portway_app:
```

Start the container:

```sh
docker compose pull && docker compose up -d
```

Portway starts on port 8080. Adjust the port mapping and volume paths to suit your environment. For a full walkthrough with configuration options, see [Docker Installation](docker-compose.md).

### Windows Server (IIS)

The steps assume working knowledge of IIS and your data sources; the essentials are all covered here, though some details will depend on your existing environment. Start by downloading the latest release from the [Releases page](https://github.com/melosso/portway/releases/).

**1. Install the .NET 10 Hosting Bundle:**

```powershell
winget install --id Microsoft.DotNet.HostingBundle.10 -e
```

**2. Generate an encryption key:**

```powershell
$bytes = New-Object byte[] 48
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
[Environment]::SetEnvironmentVariable("PORTWAY_ENCRYPTION_KEY", [Convert]::ToBase64String($bytes), "Machine")
```

**3. Extract Portway to your IIS directory** (e.g. `C:\Portway`).

**4. Configure IIS:**

1. Open IIS Manager
2. Create a new Application Pool:
   - Name: `PortwayAppPool`
   - .NET CLR version: `No Managed Code`
   - Managed pipeline mode: `Integrated`
3. Create a TLS/SSL certificate or import an existing one
4. Create a new Website:
   - Application pool: `PortwayAppPool`
   - Physical path: `C:\Portway`
   - Binding: your preferred port and certificate
5. Set the Application Pool identity to a domain user with network access if you need NTLM pass-through for proxy endpoints

Portway is now accessible at the configured binding address.

## Initial configuration

### Retrieve your access token

Portway greets you on first run by generating an access token and writing it to:

```
tokens/YOUR_SERVER_NAME.txt
```

The file contains your Bearer token:

```json
{
  "Username": "SERVER-NAME",
  "Token": "your-bearer-token-here",
  "AllowedScopes": "*",
  "AllowedEnvironments": "*",
  "ExpiresAt": "Never",
  "CreatedAt": "2025-01-01 00:00:00"
}
```

:::warning
This file contains a plaintext secret. Remove it from disk immediately after recording the token. Unauthorized access to this file compromises your gateway.
:::

### Configure environments

Environments are how Portway keeps your targets separate (think `dev`, `test`, `prod`). Start by declaring which ones are active in `environments/settings.json`:

```json
{
  "Environment": {
    "ServerName": "localhost",
    "AllowedEnvironments": ["dev", "test", "prod"]
  }
}
```

Then create a folder and `settings.json` for each environment:

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

### Create your first endpoint

With an environment in place, you're ready for the fun part. An endpoint is just a JSON file. Create `endpoints/SQL/Products/entity.json`:

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

Portway notices the new file and loads it without a restart. Saving the file is the deployment.

### Test your API

Time to see it respond. Open the OpenAPI UI at `https://localhost/docs`, authorize with your Bearer token, and make your first call:

```http
GET /api/prod/Products
Authorization: Bearer YOUR_ACCESS_TOKEN
```

## Next steps

From here, a few natural directions:

- [Configure SQL Endpoints](./endpoints-sql)
- [Set up Proxy Endpoints](./endpoints-proxy)
- [Manage Environments](./environments)
- [Configure Security](./security)
