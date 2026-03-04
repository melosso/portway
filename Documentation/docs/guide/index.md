---
title: Portway Overview
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

Portway is an **API gateway built for Windows environments**. It sits in front of SQL Server, internal services, and static content, exposing them through a consistent REST interface. If you’re running a mix of legacy systems and new services, Portway allows you to surface them **without rewriting anything**.

Portway works with:
- SQL Server tables and stored procedures
- Internal web services that require authentication
- JSON, XML, and CSV files
- Incoming webhooks
- Multi-step composite operations

There are some foundations you do need to know before continuing:

## Core Concepts

These are the foundational ideas that shape how Portway works. Understanding these concepts will help you as you configure endpoints later.

### Security

Portway includes built-in mechanisms to control access and manage how requests move between environments.

**Security Features:**
- **Token Authentication**: Secure your endpoints with tokens.
- **Scoped Permissions**: Define granular access control.
- **Environment-Aware Controls**: Manage access based on environment.
- **Encryption**: Protect sensitive data.
- **Azure Key Vault Integration**: Securely manage secrets.
- **Rate Limiting and Request Validation**: Prevent abuse and ensure valid requests.

### Environment Awareness

Portway treats environments as independent spaces, keeping configurations clean and preventing cross-contamination between development, testing, and production.

**Environment Awareness Features:**
- **Routing Based on Environment**: Direct requests based on the environment.
- **Environment-Specific Configuration**: Customize settings for each environment.
- **Isolated Headers and Connection Strings**: Ensure environment-specific configurations remain separate.

### Developer Experience

Portway includes tools to help you understand what’s happening at runtime and debug issues without additional setup.

**Developer Tools:**
- **Auto-Generated API Documentation**: Always up-to-date documentation.
- **Logging and Tracing**: Track and debug requests.
- **Health Endpoints**: Monitor the status of your gateway.
- **JSON-Based Configuration**: Easy-to-manage configuration files.

### Endpoint Types

Portway exposes a variety of endpoint types. Knowing these will help you choose the right one for your use case.

**Endpoint Types:**
- **SQL Endpoints**: Support OData queries for SQL Server.
- **Proxy Endpoints**: Forward requests to internal services.
- **Composite Endpoints**: Handle multi-step workflows.
- **File Endpoints**: Manage storage and retrieval of files.
- **Webhook Receivers**: Process incoming webhooks.
- **Static File Endpoints**: Serve static content.

## What’s next?

If you’re ready to dive deeper, the following sections will guide you through installation, endpoint configuration, and deployment to production. If you encounter any issues, the GitHub links below are the fastest way to get help or report a problem.

- **Issues**: [GitHub Issues](https://github.com/melosso/portway/issues)
- **Discussions**: [GitHub Discussions](https://github.com/melosso/portway/discussions)

Star the project on GitHub to show your support, stay updated with new releases, and help the maintainers grow their community!