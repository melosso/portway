---
title: Guide
description: A high-level guide to Portway, its core concepts, and how to get started.
outline: [2, 3]
keywords: [API Gateway, Windows, SQL Server, REST, OData]
---

# Guide

This page provides the practical context you need before setting up your first Portway endpoint. If you’re new to Portway, this guide gives you the high-level picture so you know what to expect as you move through the documentation.


## Quick Links

Most users start with installation, deployment, or security configuration. Use these shortcuts to jump straight to the relevant sections:

- [Getting Started](./getting-started)
- [Deployment](./deployment)
- [Security](./security)

## What Is Portway?

Portway is an **API gateway built for Windows environments**. It sits in front of your SQL databases, internal services, and static content, exposing them through a consistent REST interface. If you’re running a mix of legacy systems and new services, Portway allows you to surface them **without rewriting anything**.

Portway works with:
- SQL databases (SQL Server, PostgreSQL, MySQL, SQLite): tables, views, and stored procedures
- Internal web services that require authentication
- JSON, XML, and CSV files
- Incoming requests as webhook endpoint
- Multi-step composite operations

There are some foundations you do need to know before continuing:

## Concepts

These are the foundational ideas that shape how Portway works. Understanding these concepts will help you as you configure endpoints later.

### Security

Portway includes built-in mechanisms to control access and manage how requests move between environments. Some of the key features to mention:

- **Token Authentication**: Secure your endpoints with tokens.
- **Scoped Permissions**: Define granular access control.
- **Environment-Aware Controls**: Manage access based on environment.
- **Encryption**: Protect sensitive data.
- **Azure Key Vault Integration**: Securely manage secrets.
- **Rate Limiting and Request Validation**: Prevent abuse and ensure valid requests.

Please read the documentation for more insight in how we harden our gateway.

### Environment Awareness

Portway treats environments as independent spaces, keeping configurations clean and preventing cross-contamination between development, testing, and production.

- **Routing**: Direct requests based on the environment.
- **Isolated Headers and Connection Strings**: Ensure environment-specific configurations remain separate.

Environments combined with the endpoint configuration, allow full segmentation and granular control for each token.

### Developer Experience

Portway includes tools to help you understand what’s happening at runtime and debug issues without additional setup. To enhance this experience:

- **Auto-Generated API Documentation**: Always up-to-date documentation.
- **Logging and Tracing**: Track and debug requests.
- **Health Endpoints**: Monitor the status of your gateway.
- **JSON-Based Configuration**: Easy-to-manage configuration files.

In any case, you're able to saturate your documentation by configuring your endpoints.

### Endpoint Types

Portway exposes a variety of endpoint types. Knowing these will help you choose the right one for your use case.

- **SQL Endpoints**: OData queries against SQL Server, PostgreSQL, MySQL, or SQLite.
- **Proxy Endpoints**: Forward requests to internal services.
- **Composite Endpoints**: Handle multi-step workflows.
- **File Endpoints**: Manage storage and retrieval of files.
- **Webhook Receivers**: Process incoming webhooks.
- **Static File Endpoints**: Serve static content (e.g. for static data, local files or mock-ups).

## What’s next?

If you’re ready to dive deeper, the following sections will guide you through installation, endpoint configuration, and deployment to production. If you encounter any issues, the GitHub links below are the fastest way to get help or report a problem.

- **Issues**: [GitHub Issues](https://github.com/melosso/portway/issues)
- **Discussions**: [GitHub Discussions](https://github.com/melosso/portway/discussions)

Star the project on GitHub to show your support, stay updated with new releases, and help the maintainers grow their community!