---
title: Getting Started
description: "Install Portway and make your first authenticated API call"
---

# Getting Started

Portway is an ASP.NET Core application. It runs as a Docker container, on Windows Server behind IIS, or standalone on Kestrel.

## Prerequisites

### Docker

- Docker Engine with Compose support

### Windows Server / IIS

- Windows Server (or Windows 11 for development)
- [.NET 11 ASP.NET Core Hosting Bundle](https://get.dot.net/11)
- Internet Information Services (IIS)

:::warning
Install the Hosting Bundle, not the x64 runtime. Only the Hosting Bundle includes the IIS integration module.
:::

## Installation

### Docker Compose

Minimal compose file:

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

Start:

```sh
docker compose pull && docker compose up -d
```

Portway listens on port 8080. Configuration options: [Deploying with Docker](/guide/deployment-docker).

### Windows Server (IIS)

1. Download the latest release from the [Releases page](https://github.com/melosso/portway/releases/).
2. Install the .NET 11 ASP.NET Core Hosting Bundle.
3. Set a machine-level `PORTWAY_ENCRYPTION_KEY`.
4. Create an IIS site for the extracted folder with an application pool set to **No Managed Code**.

Key generation, application pool settings, NTLM pass-through and backups: [Deploying on Windows Server](/guide/deployment-windows).

## Initial configuration

### Retrieve your access token

The first start generates an access token and writes it to:

```
tokens/YOUR_SERVER_NAME.txt
```

File contents:

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
This file contains a plaintext token with full access. Delete it after recording the token.
:::

### Configure environments

Routable environments are listed in `environments/settings.json`:

```json
{
  "Environment": {
    "ServerName": "localhost",
    "AllowedEnvironments": ["dev", "test", "prod"]
  }
}
```

Each environment has a folder with its own `settings.json`:

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

An endpoint is a JSON file, e.g. `endpoints/SQL/Products/entity.json`:

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

New and changed endpoint files are loaded without a restart.

### Test your API

The API reference at `https://localhost/docs` accepts the Bearer token for test calls:

```http
GET /api/prod/Products
Authorization: Bearer YOUR_ACCESS_TOKEN
```

## Next steps

- [Configure SQL Endpoints](/guide/endpoints-sql)
- [Set up Proxy Endpoints](/guide/endpoints-proxy)
- [Manage Environments](/guide/environments)
- [Configure Security](/guide/security)
