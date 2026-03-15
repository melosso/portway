---
title: Guide
description: A high-level guide to Portway, its core concepts, and how to get started.
outline: [2, 3]
keywords: [API Gateway, Windows, SQL Server, REST, OData]
---

# Guide

> Practical context for setting up Portway — what it is, how it works, and where to start.

Portway is an **API gateway built for Windows environments**. It sits in front of your SQL databases, internal services, and static content, exposing them through a consistent REST interface. If you're running a mix of legacy systems and newer services, Portway surfaces them without rewriting anything.

## Quick links

- [Getting Started](./getting-started)
- [Deployment](./deployment)
- [Security](./security)

## What Portway works with

- SQL databases (SQL Server, PostgreSQL, MySQL, SQLite) — tables, views, and stored procedures
- Internal HTTP/HTTPS services
- JSON, XML, and CSV files
- Incoming webhook payloads stored to a SQL table
- Multi-step composite operations across proxy endpoints

## Concepts

### Security

All requests to Portway require a Bearer token. Tokens are scoped to specific environments and endpoints, so a token issued for `dev` cannot reach `prod` unless explicitly permitted. Rate limiting, request validation, and optional Azure Key Vault integration are built in.

See [Security](./security) for token management, encryption, and network configuration.

### Environment awareness

Each request URL includes an environment segment — `/api/{environment}/{endpoint}`. Portway routes the request to the connection string, headers, and access rules defined for that environment. Development, testing, and production configurations stay completely separate.

See [Environments](./environments) for configuration details.

### Endpoint types

| Type | What it does |
|---|---|
| **SQL** | Exposes tables, views, or stored procedures as REST endpoints with OData filtering |
| **Proxy** | Forwards requests to internal HTTP/HTTPS services with URL rewriting |
| **Composite** | Orchestrates multiple proxy steps into a single transaction |
| **File** | Stores and retrieves files via upload/download API calls |
| **Webhook** | Receives HTTP POST payloads and persists them to a SQL table |
| **Static** | Serves pre-defined JSON, XML, or CSV content with optional OData filtering |

### Configuration and reloading

Endpoints and environments are defined as JSON files on disk. Portway watches these files and reloads configuration automatically — no restart required. The OpenAPI documentation at `/docs` updates immediately after a configuration change.

## Next steps

- [Getting Started](./getting-started) — install and run Portway for the first time
- [Deployment](./deployment) — deploy to Windows Server with IIS
- [Security](./security) — configure tokens, scopes, and network access
- [Issues](https://github.com/melosso/portway/issues) / [Discussions](https://github.com/melosso/portway/discussions) — report bugs or ask questions
