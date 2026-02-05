# <img src="https://github.com/melosso/portway/blob/main/Source/logo.webp?raw=true" alt="" width="34" style="vertical-align: middle;">  Portway

[![License](https://img.shields.io/badge/license-AGPL%203.0-blue)](LICENSE)
[![Last commit](https://img.shields.io/github/last-commit/melosso/portway)](https://github.com/melosso/portway/commits/main)
[![Latest Release](https://img.shields.io/github/v/release/melosso/portway)](https://github.com/melosso/portway/releases/latest)

**Portway** is a fast, lightweight **API gateway** optimized for Windows Server that adapts to your infrastructure with secure, high-performance routing. It unifies multiple endpoint types (SQL, Proxy, Static, Webhooks) with built-in OData support, handling critical requirements like environment isolation, token-based authentication (with Azure Key Vault), and granular rate limiting automatically.

Portway bridges internal services with external partners, making it ideal for modernizing legacy systems and unlocking SQL data without rewrites. It ensures reliability through caching, rate limiting, extensive logging & tracing capabilities and automatic documentation. With simple filesystem-based configuration, you gain complete control over service orchestration and data exposure.

> 📍 [Landing Page](https://portway.melosso.com/)   |   📜 [Documentation](https://portway-docs.melosso.com/)  |   🐋 [Docker Compose](https://portway-docs.melosso.com/guide/docker-compose.html)

A quick example to give you an idea of what this is all about:

![Screenshot of Portway](https://github.com/melosso/portway/blob/main/.github/images/example.webp)

---

## Prerequisites

Before deploying Portway, make sure your environment meets the following requirements. These ensure full functionality across all features, especially SQL and authentication.

* [.NET 9+ Hosting Bundle](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
* If you're running on Windows: Internet Information Services (IIS)
* SQL Server access (if you're using SQL endpoints)

Ready to go? Then lets continue:

## Getting Started

Follow these steps to get Portway up and running in your environment. Setup is fast and modular, making it easy to configure just what you need.

### 1. Download & Extract

#### Windows Server (Recommended)

Grab the [latest release](https://github.com/melosso/portway/releases) and extract it to your deployment folder. This build already includes a set of example environment and endpoint configurations. 

Note, before configuring the application in Internet Information Services, make sure to configure your environment-specific secret:

```powershell
$bytes = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes); [Environment]::SetEnvironmentVariable("PORTWAY_ENCRYPTION_KEY", [Convert]::ToBase64String($bytes), "Machine")
```

On containerized environments, this can be done with the identically named `PORTWAY_ENCRYPTION_KEY` variable.

---

#### **Alternative: Docker Compose**

You can quickly deploy Portway using Docker Compose and the official image:

```yaml
services:
  portway:
    image: ghcr.io/melosso/portway:latest
    ports:
      - "8080:8080"
    volumes:
      - portway_app:/app
      - ./environments:/app/environments
      - ./endpoints:/app/endpoints
      - ./tokens:/app/tokens
      - ./log:/app/log
      - ./data:/app/data
    environment:
      - PORTWAY_ENCRYPTION_KEY=YourEncryptionKeyHere
      - ASPNETCORE_URLS=http://+:8080

volumes:
  portway_app:
```

Then run:

```sh
docker compose pull && docker compose up -d
```

This will start Portway on port [8080](#) and mount your configuration folders. Adjust paths and ports as needed for your environment. Before you can start using the API, you'll have to configure your environment settings and endpoint configurations.

### 2. Define Your Environments

Define your server and environment settings to isolate the various environments you may require (e.g. `prod` and `dev`). These configurations are used across the endpoints that you'll configure later on. First configure the allowed environments, after which the individual environment has to be defined:

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

### 3. Define Your Endpoints

Endpoints are configured as JSON files. Each type has its own directory and format, making them easy to manage and extend. These are plain examples, for more advanced configuration you may have to read our extensive documentation on our [documentation page](https://portway-docs.melosso.com/). There are various types that Portway supports:

* **SQL Server**: Direct CRUD access with schema-level control and documentation
* **Proxy**: Forward to internal services; supports complex orchestration
* **File System**: Read/write from local storage or cache (In memory and/or Redis)
* **Webhook**: Receive external calls and persist data to SQL
* **Static**: read static files or set up a mock endpoint

These are handled seperately below: 

<br>

<details>
<summary>SQL Endpoints</summary>
These point straight at your database tables. You choose which columns get exposed and what their public names should be. It keeps the surface area clean and lets you hide internal schemas or naming quirks.

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
<br>
<details>
<summary>Proxy Endpoints</summary>
These just pass the call through to another service. It’s basically a small reverse proxy where you decide which HTTP verbs you want to support.

#### Example — `endpoints/Proxy/Accounts/entity.json`

```json
{
  "Url": "http://localhost:8020/services/Exact.Entity.REST.EG/Account",
  "Methods": ["GET", "POST", "PUT", "DELETE", "MERGE"],
  "AllowedEnvironments": ["prod", "dev"]
}
```

</details>
<br>
<details>
<summary>Composite Endpoints</summary>
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
<br>
<details>
<summary>Static Endpoints</summary>
Sometimes you just want to serve a file. JSON, XML, CSV, whatever. These endpoints expose static content and can still use OData filtering if you turn it on.

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
<br>
<details>
<summary>Files Endpoints</summary>
This is for storing or retrieving actual files rather than rows or JSON. Handy for documents, images, exports.

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
<br>
<details>
<summary>Webhook Endpoints</summary>
When an external service needs to push data into your system, this is the entry point. The payload goes straight into your table of choice.

#### Example — `endpoints/Webhooks/entity.json`

```json
{
  "DatabaseObjectName": "WebhookData",
  "DatabaseSchema": "dbo",
  "AllowedColumns": ["webhook1", "webhook2"]
}
```

</details>

 

### 4. Deploy

When you're ready to host your application in IIS, there are a few important things to keep in mind. If you plan to use a proxy, you'll need to configure the correct user identity to ensure everything works smoothly. Don't forget to double-check that your application pool and security settings are properly configured for production use - we're assuming you already have the fundamentals of website security covered.

> [!TIP] 
> It's worth taking some time to fine-tune your application pool and website settings to maximize uptime and strengthen your security policies. For your primary source of general best practices, consider visiting [this post](https://techcommunity.microsoft.com/blog/coreinfrastructureandsecurityblog/iis-best-practices/1241577) on the [Microsoft Community Hub](https://techcommunity.microsoft.com/blog/coreinfrastructureandsecurityblog/iis-best-practices/1241577). For additional guidance on security best practices, you might find [Security Headers by Probely](https://securityheaders.com/) helpful.

---

## Security

### Token-Based Authentication

Portway uses a lightweight token-based system for authentication. Tokens are machine-bound and stored securely on disk.

```bash
Generated token for SERVER-1: <your-token>
Saved to /tokens/SERVER-1.txt
```

Include the token in request headers, with the Bearer prefix included:

```bash
Authorization: Bearer YOUR_TOKEN_HERE
```

> [!CAUTION] 
> The generated token files are highly sensitive and pose a significant security risk if left on disk. **Remove these files immediately after securely saving your token elsewhere.** Unauthorized access to these files can compromise your environment.

### Azure Key Vault Support

To centralize and secure configuration secrets, use Azure Key Vault. Portway can read secrets automatically by environment.

```powershell
$env:KEYVAULT_URI = "https://your-keyvault-name.vault.azure.net/"
```

Secrets format: `{env}-ConnectionString` and `{env}-ServerName`

### Application Identity

If you're pointing a **Proxy** endpoint at something inside your network, you’ll want to double-check what identity the application will be running under. If the upstream service expects Windows Authentication (NTLM) and you haven't changed the application identity, the call may fail or authenticate as the wrong user. In setups where NTLM is unavoidable, assign the Application Pool to a domain account based on the principle of least privilege.

### Protecting Secrets

Portway automatically encrypts sensitive data in your environment settings files on startup. Connection strings and sensitive headers (containing words like "password", "secret", "token", etc.) are encrypted using RSA + AES hybrid encryption to keep your data safe at rest.

---

## Examples

Here are some common requests you'll make using Portway's endpoints.

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

Upload a file:

```bash
POST /api/prod/files/Documents
Authorization: Bearer YOUR_TOKEN
Content-Type: multipart/form-data
file=@report.pdf
```

List files:

```bash
GET /api/prod/files/Documents/list
Authorization: Bearer YOUR_TOKEN
```

Download a file:

```bash
GET /api/prod/files/Documents/abc123fileId
Authorization: Bearer YOUR_TOKEN
```

</details>

<details>
<summary>Webhooks</summary>

<br>

Receive data from external services:

```bash
POST /api/prod/webhook/webhook1
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

You'll find comprehensive configuration examples in our [documentation page](https://portway-docs.melosso.com/).

## Documentation

We allow you to expose the API with a configurable documentation endpoint. This can be disabled if necessary. 

### Interactive documentation
The application uses [Scalar](https://github.com/scalar/scalar) to render your OpenAPI specification as interactive API documentation. Access it at `/docs` to explore endpoints, test requests, and view response schemas, which are all generated automatically from your endpoint configurations. If necessary, the `/Swagger` (deprecated) route is also available (requires configuration).

### Schema discovery
Portway automatically generates API documentation by reading your **database objects** at startup. It connects to the first allowed environment listed for each SQL endpoint to retrieve column metadata. 
If you're using Windows Authentication with `Trusted_Connection=True`, ensure your IIS Application Pool identity has the appropriate permissions on all environment databases. This isn't necessary when you use SQL Authentication, but make sure each environment uses its own credentials.

### Walkthrough
Our [documentation page](https://portway-docs.melosso.com/) will walk you through setting up Portway. This covers both basic usage, and advanced usage. Feel free to submit a pull request if you'd like to see changes to the documentation.

## License

Free for open source projects and personal use under the **AGPL 3.0** license. For more information, please see the [license](LICENSE) file.

## Contribution 

Contributions are welcome. Please submit a PR if you'd like to help improve Portway.
