---
title: Getting Started
description: "Install Portway and make your first authenticated API call"
---

# Getting Started

Portway ships as a container image, as `.deb` and `.rpm` packages, and as Linux and Windows archives. Each release targets x64 and arm64.

## Installation

Install Portway with one of the deployment guides, then continue with [Initial configuration](#initial-configuration).

::: cards 3
- [Deploying with Docker](/guide/deployment-docker)
  Docker Compose, from a first container to production.
- [Deploying on Linux](/guide/deployment-linux)
  `.deb` or `.rpm` package, or release archive, as a systemd service.
- [Deploying on Windows Server](/guide/deployment-windows)
  IIS website with HTTPS and a dedicated Application Pool.
:::

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

What's your next step? You can read more below:

## Next steps

- [Configure SQL Endpoints](/guide/endpoints-sql)
- [Set up Proxy Endpoints](/guide/endpoints-proxy)
- [Manage Environments](/guide/environments)
- [Configure Security](/guide/security)
