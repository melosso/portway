---
title: Getting Started
description: "Install Portway, sign in to the console and make the first authenticated API call"
---

# Getting Started

Portway ships as a container image, as `.deb` and `.rpm` packages, and as Linux and Windows archives. Each release targets x64 and arm64. Endpoint and environment configuration is identical for every installation method.

## 1. Install

::: cards 3
- [Deploying with Docker](/guide/deployment-docker)
  Docker Compose, from a first container to production.
- [Deploying on Linux](/guide/deployment-linux)
  `.deb` or `.rpm` package, or release archive, as a systemd service.
- [Deploying on Windows Server](/guide/deployment-windows)
  IIS website with HTTPS and a dedicated Application Pool.
:::

Each installation guide ends at a running instance. The steps below apply to every method.

## 2. Record the access token

If the console is enabled (`Console:Enabled`, default `true`): the first start creates no token. Tokens are created in the console under **Access Tokens** and are displayed once.

If the console is disabled: the first start generates a full-access token and writes it to `tokens/{SERVER_NAME}.txt`:

```json [tokens/{SERVER_NAME}.txt]
{
  "Username": "SERVER-NAME",
  "Token": "your-bearer-token-here",
  "AllowedScopes": "*",
  "AllowedEnvironments": "*",
  "AllowedTenants": {},
  "ExpiresAt": "Never",
  "CreatedAt": "2026-01-01 00:00:00"
}
```

:::warning
The file contains a plaintext token with full access. Record the token value and delete the file.
:::

## 3. Sign in to the console

The first start logs an account name (`admin-` plus eight characters) and a one-time password. The console at `/ui` requires a new password at first sign-in. Accounts and recovery: [Console Accounts](/guide/accounts).

## 4. Add an environment

Environments and endpoints are configured in the console or in files. The console writes the same files. File changes are loaded without a restart.

**Console**: **Environments → Add Environment**. The form sets the name (e.g. `prod`), server name and connection string, and adds the name to the routable environments.

**Files**: the global file lists the routable environments:

```json [environments/settings.json]
{
  "Environment": {
    "ServerName": "localhost",
    "AllowedEnvironments": ["prod"]
  }
}
```

The environment file contains the connection string of `prod`:

```json [environments/prod/settings.json]
{
  "ServerName": "SQLSERVER01",
  "ConnectionString": "Server=SQLSERVER01;Database=ProductionDB;Trusted_Connection=True;TrustServerCertificate=true;"
}
```

Provider detection, headers and authentication: [Environments](/guide/environments).

## 5. Add an endpoint

**Console**: **Endpoints → Add Endpoint → SQL**. The form sets the name (`Products`), schema (`dbo`), object name (`Products`), allowed columns and environments.

**Files**: one `entity.json` per endpoint, here for the `dbo.Products` table:

```json [endpoints/SQL/Products/entity.json]
{
  "DatabaseObjectName": "Products",
  "DatabaseSchema": "dbo",
  "PrimaryKey": "ProductId",
  "AllowedColumns": ["ProductId", "ProductName", "Price", "Stock"],
  "AllowedEnvironments": ["prod"]
}
```

Folder layout and routes: [Folders and Routes](/guide/layout).

## 6. Call the API

```http
GET /api/prod/Products
Authorization: Bearer YOUR_ACCESS_TOKEN
```

The API reference at `/docs` lists the endpoint and accepts the token for test calls.
