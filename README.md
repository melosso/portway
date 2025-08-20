# <img src="https://github.com/melosso/portway/blob/main/Source/logo.webp?raw=true" alt="" width="34" style="vertical-align: middle;">  Portway

[![License](https://img.shields.io/badge/license-AGPL%203.0-blue)](LICENSE)
[![Last commit](https://img.shields.io/github/last-commit/melosso/portway)](https://github.com/melosso/portway/commits/main)
[![Latest Release](https://img.shields.io/github/v/release/melosso/portway)](https://github.com/melosso/portway/releases/latest)

**Portway** is a fast, lightweight API gateway optimized for Windows Server environments. It offers fine-grained access to SQL Server data and flexible service proxying, with full environment-awareness, secure authentication, and automatic (developer friendly) documentation.

Applications that benefit from Portway are businesses looking to unlock their SQL Server data through modern APIs, companies modernizing legacy systems without costly rewrites, and organizations needing integration between internal services and external partners or software.

> 📍 [Landing Page](https://portway.melosso.com/)   |  📜 [Documentation](https://portway-docs.melosso.com/)  |  🧪 [Live Demo](https://portway-demo.melosso.com/)

A quick example to give you an idea of what this is all about:

![Screenshot of OpenAPI UI](https://github.com/melosso/portway/blob/main/Source/example.png?raw=true)

## 🧩 Key Features

Portway is built with flexibility and control in mind. Whether you're proxying services or exposing SQL endpoints, Portway adapts to your infrastructure with secure, high-performance routing.

* **Multiple endpoint types**:

  * **SQL Server (OData)** — direct CRUD access with schema-level control
  * **Proxy** — forward to internal services; supports complex orchestration
  * **File System** — read/write from local storage or cache
  * **Webhook** — receive external calls and persist data to SQL
  * **Static** — read static files or set-up a mock-up endpoint
* **Auth system**: Token-based, with Azure Key Vault integration
* **Environment-aware routing**: Dev, staging, production — all isolated and configurable
* **Built-in Documentation**: Every endpoint is documented out-of-the-box
* **Comprehensive logging**: Request/response tracing, including live monitoring
* **Rate limiting**: Easy to configure; protects downstream systems

## ⚙️ Requirements

Before deploying Portway, make sure your environment meets the following requirements. These ensure full functionality across all features, especially SQL and authentication.

* [.NET 9+ Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
* IIS (production hosting) if hosting on Windows (preferred)
* SQL Server access (for SQL endpoints)
* Local filesystem access for configuring/running the application

Ready to go? Then lets continue:

## 🚀 Getting Started

Follow these steps to get Portway up and running in your environment. Setup is fast and modular, making it easy to configure just what you need.


### 1. Download & Extract

Grab the [latest release](https://github.com/melosso/portway/releases) and extract it to your deployment folder. It already includes a set of example environment and endpoint configurations.

---

#### **Alternative: Docker Compose (Recommended for Home Lab)**

You can quickly deploy Portway using Docker Compose and the official image:

```yaml
services:
  portway:
    image: ghcr.io/melosso/portway:latest
    ports:
      - "8080:8080"
    volumes:
      - ./environments:/app/environments
      - ./endpoints:/app/endpoints
      - ./tokens:/app/tokens
      - ./log:/app/log
      - ./data:/app/data
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
```

Then run:

```sh
docker compose pull && docker compose up -d
```

This will start Portway on port [8080](#) and mount your configuration folders. Adjust paths and ports as needed for your environment.

### 2. Configure Your Environments

Define your server and environment settings to isolate dev/staging/prod as needed. These configs are used across endpoints and logging.

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

Endpoints are configured as JSON files. Each type has its own directory and format, making them easy to manage and extend.

#### SQL Endpoint — `endpoints/SQL/Products/entity.json`

Exposes a SQL table with restricted columns and CRUD operations.

```json
{
  "DatabaseObjectName": "Items",
  "DatabaseSchema": "dbo",
  "PrimaryKey": "ItemCode",
  "AllowedColumns": ["ItemCode", "Description", "Assortment", "sysguid"],
  "AllowedEnvironments": ["prod", "dev"]
}
```

#### Proxy Endpoint — `endpoints/Proxy/Accounts/entity.json`

Acts as a reverse proxy for internal services with full method control.

```json
{
  "Url": "http://localhost:8020/services/Exact.Entity.REST.EG/Account",
  "Methods": ["GET", "POST", "PUT", "DELETE", "MERGE"],
  "AllowedEnvironments": ["prod", "dev"]
}
```

#### Composite Endpoint — `endpoints/Proxy/SalesOrder/entity.json`

Combines multiple calls into a single logical transaction for APIs requiring sequential or nested operations.

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

#### Static Endpoint — `endpoints/Static/ProductionMachine/entity.json`

Serves static content (JSON, XML, CSV, etc.) with optional OData filtering support.

```json
{
  "ContentType": "application/xml",
  "ContentFile": "summary.xml",
  "EnableFiltering": true,
  "AllowedEnvironments": ["prod", "dev"]
}
```

#### Files Endpoint — `endpoints/Files/Documents/entity.json`

Stores and serves files such as documents, images, or data files.

```json
{
  "StorageType": "Local",
  "BaseDirectory": "documents",
  "AllowedExtensions": [".pdf", ".docx", ".xlsx", ".txt"],
  "AllowedEnvironments": ["prod", "dev"]
}
```

#### Webhook Endpoint — `endpoints/Webhooks/entity.json`

Used for receiving and storing webhook payloads directly into your database.

```json
{
  "DatabaseObjectName": "WebhookData",
  "DatabaseSchema": "dbo",
  "AllowedColumns": ["webhook1", "webhook2"]
}
```

### 4. Deploy

When you're ready to host your application in IIS, there are a few important things to keep in mind. If you plan to use a proxy, you'll need to configure the correct user identity to ensure everything works smoothly. Don't forget to double-check that your application pool and security settings are properly configured for production use - we're assuming you already have the fundamentals of website security covered.

> [!TIP] 
>  If you're using a **Proxy** setup, make sure you explicitly change the Application Identity. It's also worth taking some time to fine-tune your application pool and website settings to maximize uptime and strengthen your security policies. For additional guidance on security best practices, you might find [Security Headers by Probely](https://securityheaders.com/) helpful.

## 🔐 Auth & Security

### Token-Based Authentication

Portway uses a lightweight token-based system for authentication. Tokens are machine-bound and stored securely on disk.

```bash
🗝️ Generated token for SERVER-1: <your-token>
💾 Saved to /tokens/SERVER-1.txt
```

Include the token in request headers, with the Bearer prefix included:

```http
Authorization: **Bearer** YOUR_TOKEN
```

> [!CAUTION] 
> The generated token files are highly sensitive and pose a significant security risk if left on disk. **Remove these files immediately after securely saving your token elsewhere.** Unauthorized access to these files can compromise your environment.

### Azure Key Vault Support

To centralize and secure configuration secrets, use Azure Key Vault. Portway can read secrets automatically by environment.

```powershell
$env:KEYVAULT_URI = "https://your-keyvault-name.vault.azure.net/"
```

Secrets format: `{env}-ConnectionString` and `{env}-ServerName`

## 📡 API Examples

Here are some common requests you'll make using Portway's endpoints.

### SQL

Query specific data with full OData support:

```http
GET /api/prod/Products?$filter=Assortment eq 'Books'&$select=ItemCode,Description
```

### Proxy

Forward calls to internal REST services:

```http
GET /api/prod/Accounts
POST /api/prod/Accounts
```

### Composite

Chain together multiple operations into one:

```http
POST /api/prod/composite/SalesOrder
Content-Type: application/json
{
  "Header": {
    "OrderDebtor": "60093",
    "YourReference": "Connect async"
  },
  "Lines": [
    { "Itemcode": "BEK0001", "Quantity": 2, "Price": 0 },
    { "Itemcode": "BEK0002", "Quantity": 4, "Price": 0 }
  ]
}
```

### Static

Serve static content with optional OData filtering:

```http
GET /api/prod/ProductionMachine?$top=1&$filter=status eq 'running'
Accept: application/xml
```

### Files

Depending on your configuration, you could upload, list, and download files.

```http
POST /api/prod/files/Documents
Authorization: Bearer YOUR_TOKEN
Content-Type: multipart/form-data
file=@report.pdf
```

List files:
```http
GET /api/prod/files/Documents/list
Authorization: Bearer YOUR_TOKEN
```

Download a file:
```http
GET /api/prod/files/Documents/abc123fileId
Authorization: Bearer YOUR_TOKEN
```

### Webhooks

Receive data from external services:

```http
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

You'll find comprehensive configuration examples in our [documentation page](https://portway-docs.melosso.com/).

## 📊 Logging & Monitoring

Portway provides visibility into its operations with detailed logs and health check endpoints.

* Logs stored under `/log` with daily rotation
* Auth logs included for auditing
* Health endpoints:

  ```http
  GET /health
  GET /health/live
  GET /health/details
  ```

## 🤝 Credits

Thanks to the open source tools that make Portway possible:

* [ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/)
* [DynamicODataToSQL](https://github.com/DynamicODataToSQL/DynamicODataToSQL)
* [Scalar](https://github.com/ScalaR/ScalaR)
* [Serilog](https://serilog.net/)
* [SQLite](https://www.sqlite.org/index.html)

## License

Portway is available under two licensing models:

* **Open Source (AGPL-3.0)** — Free for open source projects and personal use
* **Commercial License** — For commercial use with full transparency of the open source project

The commercial variant solely focusses on Business/Enterprise that requrire: Priority support, guaranteed patches, DTAP environments require a [commercial license](https://melosso.com/licensing/portway). Activation is simple with a license key from your account portal.

[Get your license →](https://melosso.com/licensing/portway)

## Contribution 

Contributions are welcome. Please submit a PR if you'd like to help improve Portway.