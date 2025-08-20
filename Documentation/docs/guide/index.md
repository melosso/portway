# Guide

Welcome to the Portway documentation! This guide will walk you through everything you need to know to set up, configure, and use Portway API Gateway effectively.

## Quick Links

- [Getting Started](./getting-started) - Install and configure your first endpoint
- [Deployment](./deployment) - Production deployment with IIS
- [Security](./security) - Authentication, authorization, and best practices

## What is Portway?

Portway is a lightweight API gateway designed specifically for Windows environments. It provides a unified interface to:

- **SQL Server databases** - Expose tables and stored procedures as REST APIs
- **Internal services** - Proxy requests to internal web services with authentication
- **Static content** - Serve JSON, XML, CSV files with optional OData filtering
- **Webhook endpoints** - Receive and process webhooks from external systems
- **Composite operations** - Chain multiple operations in a single request

## Key Features

We've built Portway in a way to include commonly requested features, such as:

### 🔐 Enterprise Security
- Token-based authentication with scoped access
- Environment-specific access controls
- Azure Key Vault integration for secrets
- Rate limiting and request validation

### 🌍 Environment Awareness
- Route requests to different environments (dev, test, prod)
- Environment-specific configurations
- Isolated connection strings and headers

### 📊 Developer Experience
- Automatic API documentation
- Comprehensive logging and tracing
- Health check endpoints
- Simple JSON-based configuration

### 🔄 Flexible Endpoint Types
- SQL endpoints with OData query support
- Proxy endpoints for service forwarding
- Composite endpoints for multi-step operations
- File endpoints for document storage and retrieval
- Webhook endpoints for external integrations
- Static endpoints for serving content files

## Getting Help

- **Issues**: [GitHub Issues](https://github.com/melosso/portway/issues)
- **Discussions**: [GitHub Discussions](https://github.com/melosso/portway/discussions)

## License

Portway is released under the Open Source (AGPL-3.0) license. See the [LICENSE](https://github.com/melosso/portway/blob/main/LICENSE) file for details.