---
title: Guide
description: "Portway concepts: endpoint types, environments, security and configuration files"
outline: [2, 3]
keywords: [API Gateway, Docker, Windows, SQL Server, REST, OData]
---

# What is Portway?

Portway is an API gateway that exposes SQL databases, internal HTTP services, static content, files and inbound webhooks through one authenticated REST interface, configured with JSON files.

## Concepts

### Security

API requests require a Bearer token, restricted to endpoints, environments and optionally tenant values. Details: [Security](/guide/security).

### Environment awareness

The environment segment in `/api/{environment}/{endpoint}` selects the connection string, headers and access rules of that environment. Details: [Environments](/guide/environments).

### Endpoint types

| Type | Behavior |
|---|---|
| SQL | Tables, views, stored procedures and table-valued functions on SQL Server, PostgreSQL, MySQL/MariaDB and SQLite, with OData queries |
| Proxy | Forwards requests to internal HTTP/HTTPS services with URL rewriting |
| Composite | Calls several proxy endpoints in sequence |
| File | File upload, download, delete and listing |
| Webhook | Stores POST payloads in a SQL table |
| Static | Serves JSON, XML or CSV content with optional OData queries |

### Configuration and reloading

Endpoints and environments are JSON files on disk. Changes are reloaded without a restart, and the OpenAPI document at `/docs` follows them.

## Next steps

- [Getting Started](/guide/getting-started)
- [Deployment](/guide/deployment)
- [Security](/guide/security)
- [Issues](https://github.com/melosso/portway/issues) and [Discussions](https://github.com/melosso/portway/discussions)
