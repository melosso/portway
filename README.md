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

Each endpoint is an `entity.json` file in a directory per type. Advanced configuration is in the <a href="https://melosso.github.io/portway/" target="_blank" rel="noopener noreferrer">documentation</a>. Endpoint types:

* **SQL** (SQL Server, PostgreSQL, MySQL, SQLite): Direct CRUD access with schema-level control and documentation
* **Proxy**: Forward to internal services; supports complex orchestration
* **Composite**: Chain multiple endpoint calls into one transaction
* **File System**: Read/write from local storage or cache (In memory and/or Redis)
* **Webhook**: Receive external calls and persist data to SQL
* **Static**: read static files or set up a mock endpoint

Requests for each type are listed under [Examples](#examples).

<br>

<details>
<summary>SQL Endpoints</summary>

<br> 

Map to a database table or view. `AllowedColumns` sets the exposed columns and their public names.

#### Example — `endpoints/SQL/Products/entity.json`

```json
{
  "DatabaseObjectName": "Items",
  "DatabaseSchema": "dbo",
  "PrimaryKey": "ItemCode",
  "AllowedColumns": [
    "ItemCode;ProductNumber",
    "LongDescription;Description",
    "Assortment;AssortmentCode",
    "sysguid;InternalID"
  ],
  "AllowedEnvironments": ["prod", "dev"]
}
```

</details>

<details>
<summary>Proxy Endpoints</summary>

<br> 

Forward requests to another service, limited to the configured HTTP methods.

#### Example — `endpoints/Proxy/Accounts/entity.json`

```json
{
  "Url": "http://localhost:8020/services/Exact.Entity.REST.EG/Account",
  "Methods": ["GET", "POST", "PUT", "DELETE", "MERGE"],
  "AllowedEnvironments": ["prod", "dev"]
}
```

</details>

<details>
<summary>Composite Endpoints</summary>

<br> 

These help when a single logical action actually means “call a bunch of other endpoints in a specific order.” Think of creating an order with multiple lines and a header. You wire the steps together and the engine handles the sequencing.

#### Example — `endpoints/Proxy/SalesOrder/entity.json`

```json
{
  "Type": "Composite",
  "Url": "http://localhost:8020/services/Exact.Entity.REST.EG",
  "Methods": ["POST"],
  "CompositeConfig": {
    "Name": "SalesOrder",
    "Description": "Creates a complete sales order with multiple lines and header",
    "Steps": [
      {
        "Name": "CreateOrderLines",
        "Endpoint": "SalesOrderLine",
        "Method": "POST",
        "IsArray": true,
        "ArrayProperty": "Lines",
        "TemplateTransformations": {
          "TransactionKey": "$guid"
        }
      },
      {
        "Name": "CreateOrderHeader",
        "Endpoint": "SalesOrderHeader",
        "Method": "POST",
        "SourceProperty": "Header",
        "TemplateTransformations": {
          "TransactionKey": "$prev.CreateOrderLines.0.d.TransactionKey"
        }
      }
    ]
  }
}
```

</details>

<details>
<summary>Static Endpoints</summary>

<br> 

Serve static JSON, XML or CSV content, with optional OData filtering.

#### Example — `endpoints/Static/ProductionMachine/entity.json`

```json
{
  "ContentType": "application/xml",
  "ContentFile": "summary.xml",
  "EnableFiltering": true,
  "AllowedEnvironments": ["prod", "dev"]
}
```

</details>

<details>
<summary>Files Endpoints</summary>

<br> 

Store and retrieve files such as documents, images and exports.

#### Example — `endpoints/Files/Documents/entity.json`

```json
{
  "StorageType": "Local",
  "BaseDirectory": "documents",
  "AllowedExtensions": [".pdf", ".docx", ".xlsx", ".txt"],
  "AllowedEnvironments": ["prod", "dev"]
}
```

</details>

<details>
<summary>Webhook Endpoints</summary>

<br> 

Receive inbound calls from external services and write the payload to a table.

#### Example — `endpoints/Webhooks/Webhooks/Incoming/entity.json`

```json
{
  "DatabaseObjectName": "WebhookData",
  "DatabaseSchema": "dbo",
  "AllowedColumns": ["webhook1", "webhook2"]
}
```

</details>

### Deploy

When you're ready to deploy, follow the <a href="https://melosso.github.io/portway/guide/deployment" target="_blank" rel="noopener noreferrer">deployment guide</a>. It covers most scenario's, from containerization to application pool identity (needed for NTLM proxy scenarios), security settings, and production hardening.

---

## Security

Portway uses a hardened token-based system for authentication. Include the token in request headers, with the Bearer prefix included:

```bash
Authorization: Bearer YOUR_TOKEN_HERE
```

You can find more about this topic in the <a href="https://melosso.github.io/portway/guide/security" target="_blank" rel="noopener noreferrer">security guide</a>.

---

## Examples

Common requests per endpoint type.

<details>
<summary>SQL</summary>

<br>

Query specific data with full OData support:

```bash
GET /api/prod/Products?$filter=Assortment eq 'Books'&$select=ItemCode,Description
````

</details>

<details>
<summary>Proxy</summary>

<br>

Forward calls to internal REST services:

```bash
GET /api/prod/Accounts
POST /api/prod/Accounts
```

</details>

<details>
<summary>Composite</summary>

<br>

Chain together multiple operations into one:

```bash
POST /api/prod/composite/SalesOrder
Content-Type: application/json
{
  "Header": {
    "OrderDebtor": "60093",
    "YourReference": "Connect async"
  },
  "Lines": [
    { "Itemcode": "ITEM-001", "Quantity": 2, "Price": 0 },
    { "Itemcode": "ITEM-002", "Quantity": 4, "Price": 0 }
  ]
}
```

</details>

<details>
<summary>Static</summary>

<br>

Serve static content with optional OData filtering:

```bash
GET /api/prod/ProductionMachine?$top=1&$filter=status eq 'running'
Accept: application/xml
```

</details>

<details>
<summary>Files</summary>

<br>

Upload, list, and download files:

```bash
POST /api/prod/files/Documents
Content-Type: multipart/form-data
file=@report.pdf

GET /api/prod/files/Documents/list
GET /api/prod/files/Documents/abc123fileId
```

</details>

<details>
<summary>Webhooks</summary>

<br>

Receive data from external services:

```bash
POST /api/prod/Webhooks/Incoming/webhook1
Content-Type: application/json
{
  "eventType": "order.created",
  "data": {
    "orderId": "12345",
    "customer": "ACME Corp"
  }
}
```

</details>

More configuration examples are in the <a href="https://melosso.github.io/portway/guide" target="_blank" rel="noopener noreferrer">Endpoint sections</a> in the documentation .

## Documentation

The OpenAPI documentation endpoint is configurable and can be disabled.

### Interactive documentation
The application uses <a href="https://github.com/scalar/scalar" target="_blank" rel="noopener noreferrer">Scalar</a> to render your OpenAPI specification as interactive API documentation. `/docs` lists endpoints, request and response schemas and a request client, generated from the endpoint configuration.

### Model Context Protocol (MCP)
Portway is an MCP server over HTTP. Endpoints with MCP enabled are tools for any MCP client (e.g. Mistral, VS Code Copilot, custom agents or the built-in chat). MCP is opt-in per endpoint. Token authentication and environment scoping apply to every tool call. See the <a href="https://melosso.github.io/portway/guide/mcp" target="_blank" rel="noopener noreferrer">MCP documentation</a>.

### Walkthrough
The <a href="https://melosso.github.io/portway/" target="_blank" rel="noopener noreferrer">documentation</a> covers setup, basic usage and advanced configuration. Documentation changes are accepted as pull requests.

## Contribution 

Contributions are welcome, please submit a PR if you'd like to help improve the project.

## License

Licensed under the **EUPL-1.2**. For more information, please see the [license](LICENSE) file.
