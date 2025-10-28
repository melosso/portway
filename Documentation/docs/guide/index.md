# Guide

Welcome to the Portway documentation! This guide will walk you through everything you need to know to set up, configure, and use Portway API Gateway effectively.

## Quick Links

We've set-up some quick links to get you up-and-running in no time:

- [Getting Started](./getting-started) - Install and configure your first endpoint
- [Deployment](./deployment) - Production deployment with IIS
- [Security](./security) - Authentication, authorization, and best practices

## What is Portway?

TLDR: Portway is a lightweight API gateway designed specifically for Windows environments. It provides a unified interface to:

- **SQL Server databases** - Expose tables and stored procedures as REST APIs
- **Internal services** - Proxy requests to internal web services with authentication
- **Static content** - Serve JSON, XML, CSV files with optional OData filtering
- **Webhook endpoints** - Receive and process webhooks from external systems
- **Composite operations** - Chain multiple operations in a single request

We've optimized the application to be running prodominantly in Windows-environments.

## Key Features

We've built Portway in a way to include commonly requested features, with security and flexibility in mind:

#### Security
- Token-based authentication with scoped access
- Environment-specific access controls
- Always-on Encryption
- Azure Key Vault integration for secrets
- Rate limiting and request validation

#### Environment Awareness
- Route requests to different environments (dev, test, prod)
- Environment-specific configurations
- Isolated connection strings and headers

#### Developer Experience
- Automatic API documentation
- Comprehensive logging and tracing
- Health check endpoints
- Simple JSON-based configuration

#### Flexible Endpoint Types
- SQL endpoints with OData query support
- Proxy endpoints for service forwarding
- Composite endpoints for multi-step operations
- File endpoints for document storage and retrieval
- Webhook endpoints for external integrations
- Static endpoints for serving content files

## Getting Help

We're here to help, but we may require your effort on polishing Portway.

- **Issues**: [GitHub Issues](https://github.com/melosso/portway/issues)
- **Discussions**: [GitHub Discussions](https://github.com/melosso/portway/issues)

## Licensing

Portway is available under two licensing models:

* **Open Source (AGPL-3.0)** — Free for open source projects and personal use
* **Commercial License** — For commercial use without open source requirements

Professional features such as priority support and guaranteed patches require a [commercial license](https://melosso.com/licensing/portway). Feel free to contact us.