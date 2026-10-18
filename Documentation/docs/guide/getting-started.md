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

The first start generates a full-access token and writes it to `tokens/{SERVER_NAME}.txt`:

```json
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

`environments/settings.json` lists the routable environments:

```json
{
  "Environment": {
    "ServerName": "localhost",
    "AllowedEnvironments": ["prod"]
  }
}
```

`environments/prod/settings.json` contains the connection string of the `prod` environment:

```json
{
  "ServerName": "SQLSERVER01",
  "ConnectionString": "Server=SQLSERVER01;Database=ProductionDB;Trusted_Connection=True;TrustServerCertificate=true;"
}
```

Provider detection, headers and authentication: [Environments](/guide/environments).

## 5. Add an endpoint

`endpoints/SQL/Products/entity.json` exposes the `dbo.Products` table:

```json
{
  "DatabaseObjectName": "Products",
  "DatabaseSchema": "dbo",
  "PrimaryKey": "ProductId",
  "AllowedColumns": ["ProductId", "ProductName", "Price", "Stock"],
  "AllowedEnvironments": ["prod"]
}
```

New and changed endpoint files are loaded without a restart. Folder layout and routes: [Folders and Routes](/guide/layout).

## 6. Call the API

```http
GET /api/prod/Products
Authorization: Bearer YOUR_ACCESS_TOKEN
```

The API reference at `/docs` lists the endpoint and accepts the token for test calls.
