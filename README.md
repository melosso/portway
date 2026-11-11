![Portway](.github/images/banner.webp)

---

[![License](https://img.shields.io/badge/license-EUPL%201.2-blue)](LICENSE)
[![Last commit](https://img.shields.io/github/last-commit/melosso/portway)](https://github.com/melosso/portway/commits/main)
[![Latest Release](https://img.shields.io/github/v/release/melosso/portway)](https://github.com/melosso/portway/releases/latest)

- Documentation: [Github Pages](https://melosso.github.com/portway)
- Deploy: [Install Portway](https://melosso.github.io/portway/guide/install/)
- Demo: [Visit Instance](https://portway-demo.melosso.com/)

[Portway](https://github.com/melosso/portway/) is an open source API gateway for databases, internal services and webhooks. Configuration is file-based. Built in: caching, audit logging, MCP, OData, Azure Key Vault secrets, rate limiting, and Prometheus or OTLP metrics.

## Getting Started

### Installation

* [Docker](https://melosso.github.io/portway/guide/deployment-docker/)
* [Linux](https://melosso.github.io/portway/guide/deployment-linux/)
* [Windows Server](https://melosso.github.io/portway/guide/deployment-windows/)

### Define Your Environments

Environments (e.g. `prod`, `dev`) isolate server and connection settings for all endpoints. `environments/settings.json` lists the allowed environments; each has its own `settings.json`:

**`environments/settings.json`**

```json
{
  "Environment": {
    "ServerName": "localhost",
    "AllowedEnvironments": ["prod", "dev"]
  }
}
```

**`environments/prod/settings.json`**

```json
{
  "ServerName": "localhost",
  "ConnectionString": "Server=localhost;Database=prod;Trusted_Connection=True;Connection Timeout=5;TrustServerCertificate=true;"
}
```

### Define Your Endpoints

Each endpoint is an `entity.json` file in a directory per type. Advanced configuration is in the [documentation](https://melosso.github.io/portway/). Endpoint types:

* **SQL** (SQL Server, PostgreSQL, MySQL, SQLite): Direct CRUD access with schema-level control and documentation
* **Proxy**: Forward to internal services; supports complex orchestration
* **Composite**: Chain multiple endpoint calls into one transaction
* **File System**: Read/write from local storage or cache (In memory and/or Redis)
* **Webhook**: Receive external calls and persist data to SQL
* **Static**: read static files or set up a mock endpoint

Configuration and usage examples for each type are shown in the [Endpoints](https://melosso.github.io/portway/guide/endpoints/).

### Deploy

When you're ready to deploy, follow the [deployment guide](https://melosso.github.io/portway/guide/deployment). It covers most scenario's, from containerization to application pool identity (needed for NTLM proxy scenarios), security settings, and production hardening.

---

## Security

Portway uses a hardened token-based system for authentication. Include the token in request headers, with the Bearer prefix included:

```bash
Authorization: Bearer YOUR_TOKEN_HERE
```

You can find more about this topic in the [security guide](https://melosso.github.io/portway/guide/security).

## Documentation

The OpenAPI documentation endpoint is configurable and can be disabled.

### Interactive documentation
The application uses [Scalar](https://github.com/scalar/scalar) to render your OpenAPI specification as interactive API documentation. `/docs` lists endpoints, request and response schemas and a request client, generated from the endpoint configuration.

### Model Context Protocol (MCP)
Portway is an MCP server over HTTP. Endpoints with MCP enabled are tools for any MCP client (e.g. Mistral, VS Code Copilot, custom agents or the built-in chat). MCP is opt-in per endpoint. Token authentication and environment scoping apply to every tool call. See the [MCP documentation](https://melosso.github.io/portway/guide/mcp).

### Walkthrough
The [documentation](https://melosso.github.io/portway/) covers setup, basic usage and advanced configuration. Documentation changes are accepted as pull requests.

## Contribution 

Contributions are welcome, please submit a PR if you'd like to help improve the project.

## License

Licensed under the **EUPL-1.2**. For more information, please see the [license](LICENSE) file.
