---
title: Getting Started
description: "Install Portway and make your first authenticated API call"
---

# Getting Started

Portway is an ASP.NET Core application. It runs as a Docker container, or on Windows Server behind IIS, and standalone on Kestrel works just as well. This guide covers the Docker and IIS paths through to a working endpoint.

## Prerequisites

**Docker:**
- Docker Engine with Compose support

Please note you may need additional configuration to mount your configuration to the container, the guide will set the basics up for you.

**Windows Server / IIS:**
- Windows Server (or Windows 11 for development)
- [.NET 11 ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/en-us/download/dotnet/11.0)

> Note: Portway targets .NET 11. Because .NET 11 is currently a preview of the framework, you may prefer to stay on the .NET 10 LTS build of Portway for production deployments until .NET 11 reaches general availability. Both are fully supported; the choice is simply about how conservative you would like your runtime to be.
- Internet Information Services (IIS)

:::warning
Download the **Hosting Bundle**, not the x64 runtime installer. The Hosting Bundle includes the IIS integration module that the runtime package omits.
:::

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

Portway starts on port 8080. Adjust the port mapping and volume paths to suit your environment. For a full walkthrough with configuration options, see [Deploying with Docker](/guide/docker-compose).

### Windows Server (IIS)

Download the latest release from the [Releases page](https://github.com/melosso/portway/releases/), install the .NET 11 ASP.NET Core Hosting Bundle, generate a machine-level `PORTWAY_ENCRYPTION_KEY`, then point an IIS site at the extracted folder using an application pool set to **No Managed Code**.

For the full walkthrough with the encryption key command, the application pool settings, NTLM pass-through and a backup routine, see [Deploying on Windows Server](/guide/deployment-windows).

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

- [Configure SQL Endpoints](/guide/endpoints-sql)
- [Set up Proxy Endpoints](/guide/endpoints-proxy)
- [Manage Environments](/guide/environments)
- [Configure Security](/guide/security)
