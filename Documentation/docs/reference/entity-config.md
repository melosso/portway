---
title: Entity Configuration
description: "Every endpoint you create starts life as an entity.json file"
---

# Entity Configuration

Every endpoint you create starts life as an `entity.json` file. This page is your dictionary for those files: what each property does, which ones apply to which endpoint type (SQL, Proxy, Static, Composite, Webhook, File), and the patterns that tend to work well in practice.

## File structure

Entity configuration files are JSON files located in the endpoints directory structure:

```
/endpoints/
  ├── SQL/
  │   └── [EntityName]/
  │       └── entity.json
  ├── Proxy/
  │   └── [EntityName]/
  │       └── entity.json
  ├── Static/
  │   └── [EntityName]/
  │       ├── entity.json
  │       └── [content-file]
  ├── Webhooks/
  │   └── [Namespace]/
  │       └── [EntityName]/
  │           └── entity.json
  └── Files/
      └── [EntityName]/
          └── entity.json
```

Composite endpoints have no folder of their own. They live under `Proxy/` with `"Type": "Composite"` in their `entity.json`. Any endpoint folder can also be nested one level deeper to give it a namespace, as `Webhooks/` shows above.

## Endpoint: SQL

SQL entities expose database tables or views through OData endpoints.

### Basic structure

```json
{
  "DatabaseObjectName": "Items",
  "DatabaseSchema": "dbo",
  "PrimaryKey": "ItemCode",
  "AllowedColumns": [
    "ItemCode",
    "Description",
    "Assortment",
    "sysguid"
  ],
  "AllowedEnvironments": ["prod", "dev"]
}
```

### With stored procedures

```json
{
  "DatabaseObjectName": "ServiceRequests",
  "DatabaseSchema": "dbo",
  "AllowedColumns": [
    "RequestId",
    "CustomerCode",
    "Title",
    "Description",
    "Priority",
    "Status",
    "CategoryId",
    "AssignedTo",
    "CreatedBy",
    "CreatedDate",
    "LastModifiedBy",
    "LastModifiedDate",
    "ResolvedDate",
    "ClosedDate",
    "DueDate"
  ],
  "Procedure": "dbo.sp_ManageServiceRequests",
  "AllowedMethods": ["GET", "POST", "PUT"],
  "AllowedEnvironments": ["prod"]
}
```

### With Table-Valued Functions (TVF)

Table-Valued Functions allow you to expose parameterized, read-only endpoints that return dynamic result sets. Use these for endpoints that should execute a SQL function with input parameters, rather than exposing a static table or view.

```json
{
  "DatabaseObjectName": "GenerateSampleUsers",
  "DatabaseSchema": "dbo",
  "DatabaseObjectType": "TableValuedFunction",
  "FunctionParameters": [
    {
      "Name": "DepartmentId",
      "SqlType": "int",
      "Source": "Path",
      "Position": 1,
      "Required": false,
      "DefaultValue": "DEFAULT",
      "ValidationPattern": "^[0-9]+$"
    },
    {
      "Name": "UserCount",
      "SqlType": "int",
      "Source": "Query",
      "Required": false,
      "DefaultValue": "DEFAULT"
    }
  ],
  "AllowedColumns": [
    "user_id;UserId",
    "first_name;FirstName",
    "department_name;DepartmentName"
  ],
  "AllowedMethods": ["GET"],
  "AllowedEnvironments": ["dev", "test"]
}
```

**Key points:**
- Set `DatabaseObjectType` to `"TableValuedFunction"`.
- Use `FunctionParameters` to define the function's input parameters (with type, source, and validation).
- TVF endpoints are always read-only (`AllowedMethods` should only include `GET`).
- No `PrimaryKey` property is needed for TVFs.
- Use column aliases in `AllowedColumns` as with regular endpoints.

### Property reference

| Property              | Type    | Required | Description                                                                                  |
|-----------------------|---------|----------|----------------------------------------------------------------------------------------------|
| `DatabaseObjectName`  | string  | Yes      | Name of the table, view, or function                                                         |
| `DatabaseSchema`      | string  | No       | Database schema (default: "dbo")                                                             |
| `PrimaryKey`          | string  | No       | Primary key column (default: "Id"). Not used for TVF endpoints                               |
| `DatabaseObjectType`  | string  | No*      | Set to `"TableValuedFunction"` for TVF endpoints only                                        |
| `FunctionParameters`  | array   | No*      | List of input parameters for TVF endpoints only                                              |
| `AllowedColumns`      | array   | Yes      | List of accessible columns (supports aliases)                                                |
| `ResponseTransforms`  | object  | No       | `Remove`, `Rename` and `Mask` rules applied to query results after alias mapping             |
| `Procedure`           | string  | No       | Stored procedure for data operations                                                         |
| `AllowedMethods`      | array   | No       | HTTP methods (default: ["GET"]). You can also allow `QUERY` for body-carried reads (RFC 10008), or `MERGE` as an alias of `PATCH` |
| `Deprecated`          | boolean | No       | Shows the endpoint's operations as deprecated in the OpenAPI documentation                    |
| `Enabled`             | boolean | No       | Set to `false` to take the endpoint out of service; calls receive `503` (default: `true`)     |
| `AllowedEnvironments` | array   | No       | Allowed environments (default: all)                                                          |

\* Only required for Table-Valued Function (TVF) endpoints.

### Column aliases

The `AllowedColumns` array supports semicolon-separated aliases for user-friendly column names:

```json
{
  "AllowedColumns": [
    "ItemCode;ProductNumber",     // Database column: ItemCode, API alias: ProductNumber
    "Description;ProductName",    // Database column: Description, API alias: ProductName
    "Assortment;Category",        // Database column: Assortment, API alias: Category
    "sysguid;InternalID"          // Database column: sysguid, API alias: InternalID
  ]
}
```

**Format:** `"DatabaseColumn;Alias"`

**Benefits:**
- Create intuitive API column names while preserving database structure
- Backward compatible with existing configurations
- Automatic conversion in all OData operations (`$select`, `$filter`, `$orderby`)

## Endpoint: proxy

Proxy entities forward requests to internal web services.

### Basic example

```json
{
  "Url": "http://localhost:8020/services/Exact.Entity.REST.EG/Account",
  "Methods": ["GET", "POST", "PUT", "DELETE", "MERGE"]
}
```

### With environment restrictions

```json
{
  "Url": "http://localhost:8020/services/Exact.Entity.REST.EG/Classification",
  "Methods": ["GET"],
  "AllowedEnvironments": ["prod", "dev"]
}
```

### Hidden endpoint

```json
{
  "Url": "http://localhost:8020/services/Exact.Entity.REST.EG/SalesOrderHeader",
  "Methods": ["POST"],
  "Hidden": true
}
```

### With HTTP method translation

```json
{
  "Url": "http://localhost:8020/services/Exact.Entity.REST.EG/Account",
  "Methods": ["GET", "POST", "PUT", "DELETE"],
  "CustomProperties": {
    "HttpMethodTranslation": "PUT:MERGE,POST:CREATE"
  }
}
```

### With retry and failover

When an upstream service is occasionally slow to answer or has a standby instance, you can let Portway retry the call and switch to a fallback URL before the caller notices anything:

```json
{
  "Url": "http://erp-primary.company.local/api/orders",
  "Methods": ["GET", "POST"],
  "FallbackUrls": ["http://erp-standby.company.local/api/orders"],
  "Retry": { "Attempts": 2, "DelayMs": 200 }
}
```

Portway tries the primary URL first. A connection failure, a timeout, or a 502, 503, or 504 response triggers the next attempt; other responses pass through unchanged. Each URL gets `Attempts` tries with `DelayMs` milliseconds between them. Without these properties every request makes exactly one attempt, as before.

### With response transforms

When an upstream response carries fields you would rather not expose, you can shape JSON responses declaratively instead of changing the upstream system:

```json
{
  "Url": "http://crm.company.local/api/contacts",
  "Methods": ["GET"],
  "ResponseTransforms": {
    "Remove": ["internalNotes"],
    "Rename": { "cust_nm": "customerName" },
    "Mask": ["ssn"]
  }
}
```

Rules apply to top level fields of JSON objects, to each element of JSON arrays, and to items inside an OData style `value` wrapper. Masked fields return `***`. When rules overlap, `Remove` wins. Responses that are not JSON pass through untouched, and transforms run before caching so cached entries are already shaped.

### Property reference

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Url` | string | Yes | Target service URL |
| `FallbackUrls` | array | No | Standby URLs tried in order when the primary fails |
| `Retry` | object | No | `Attempts` per URL (default 1) and `DelayMs` between tries (default 200) |
| `ResponseTransforms` | object | No | `Remove`, `Rename` and `Mask` rules for JSON response fields |
| `Methods` | array | Yes | Allowed HTTP methods |
| `SupportsOData` | boolean | No | Set to `true` when the proxied service understands OData query parameters, so the documentation advertises them (default: `false`) |
| `Hidden` | boolean | No | Leaves the endpoint out of the OpenAPI documentation; it keeps serving (default: `false`) |
| `Enabled` | boolean | No | Set to `false` to take the endpoint out of service; calls receive `503` (default: `true`) |
| `Deprecated` | boolean | No | Shows the endpoint's operations as deprecated in the OpenAPI documentation |
| `AllowedEnvironments` | array | No | Allowed environments |
| `CustomProperties` | object | No | Extended functionality settings |

#### CustomProperties options

| Property | Type | Description | Example |
|----------|------|-------------|---------|
| `ContentType` | string | Sets the primary Content-Type for requests and Accept header for responses. Overrides the default `application/json` | `"application/xml"` |
| `HttpMethodTranslation` | string | Translate HTTP methods before proxying | `"PUT:MERGE,POST:CREATE"` |
| `HttpMethodAppendHeaders` | string | Auto-append headers based on HTTP method | `"PUT:X-HTTP-Method={ORIGINAL_METHOD}"` |

### With HTTP method translation and header appending

```json
{
  "Url": "http://api.example.com/accounts",
  "Methods": ["GET", "POST", "PUT", "DELETE"],
  "CustomProperties": {
    "HttpMethodTranslation": "PUT:POST",
    "HttpMethodAppendHeaders": "PUT:X-HTTP-Method={ORIGINAL_METHOD},Content-Type=application/merge-patch+json"
  }
}
```

### Configuring DELETE operations

Different internal services expect DELETE request IDs in different formats. Use `DeletePatterns` to tell the gateway how to format the ID when forwarding to your target service.

#### Why configure this?

When you receive:
```
DELETE /api/prod/customers/a7f3c8e1-4b2d-4d91-8c5a-9e2b1f6d8a4c
```

The gateway needs to know whether your internal service expects:
- `http://service/customers/a7f3c8e1...` (path style)
- `http://service/customers?id=a7f3c8e1...` (query style)
- `http://service/customers(guid'a7f3c8e1...')` (OData style)

#### Available styles

| Style | Use Case | Example Output |
|-------|----------|----------------|
| **PathParameter** (default) | Standard REST APIs | `http://service/customers/a7f3c8e1...` |
| **QueryParameter** | Legacy systems using query strings | `http://service/customers?id=a7f3c8e1...` |
| **ODataGuid** | OData services with GUID keys | `http://service/customers(guid'a7f3c8e1...')` |
| **ODataKey** | OData services with numeric keys | `http://service/orders(10248)` |

**Configuration examples:**

```json
// PathParameter (or omit DeletePatterns entirely)
{ "DeletePatterns": [{ "Style": "PathParameter" }] }

// QueryParameter
{ "DeletePatterns": [{ "Style": "QueryParameter", "Parameter": "id" }] }

// ODataGuid
{ "DeletePatterns": [{ "Style": "ODataGuid" }] }

// ODataKey
{ "DeletePatterns": [{ "Style": "ODataKey" }] }
```

#### Quick examples

**Modern REST microservice** (most common):
```json
{
  "Url": "http://order-service.company.local/api/orders",
  "Methods": ["GET", "POST", "PUT", "DELETE"]
  // No DeletePatterns needed - PathParameter is the default
}
```

**Legacy system with query parameters**:
```json
{
  "Url": "http://crm-legacy.company.local/api/contacts",
  "Methods": ["GET", "DELETE"],
  "DeletePatterns": [{ 
    "Style": "QueryParameter",
    "Parameter": "contact_id"
  }]
}
```

**Internal OData service**:
```json
{
  "Url": "http://inventory-api.company.local/api/products",
  "Methods": ["GET", "POST", "PUT", "DELETE"],
  "DeletePatterns": [{ "Style": "ODataGuid" }]
}
```

The gateway automatically recognizes IDs in any format (plain GUIDs, OData wrapped, numeric, string keys) and forwards them correctly to your service.

## Endpoint: static

Static entities serve pre-defined content files with optional OData filtering capabilities.

### Basic example

```json
{
  "ContentType": "application/xml",
  "ContentFile": "summary.xml",
  "EnableFiltering": true,
  "Hidden": false,
  "AllowedEnvironments": ["prod", "dev"]
}
```

### With documentation

```json
{
  "ContentType": "application/json",
  "ContentFile": "countries.json",
  "EnableFiltering": true,
  "AllowedEnvironments": ["prod", "dev"],
  "Documentation": {
    "TagDescription": "Country reference data for application forms and validation",
    "MethodDescriptions": {
      "GET": "Retrieve country list with optional filtering"
    }
  }
}
```

### Property reference

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `ContentType` | string | No | MIME type (auto-detected if not specified) |
| `ContentFile` | string | Yes | Content filename relative to endpoint directory |
| `EnableFiltering` | boolean | No | Enable OData query parameters (default: false) |
| `Hidden` | boolean | No | Leaves the endpoint out of the OpenAPI documentation; it keeps serving (default: `false`) |
| `Enabled` | boolean | No | Set to `false` to take the endpoint out of service; calls receive `503` (default: `true`) |
| `Deprecated` | boolean | No | Shows the endpoint's operations as deprecated in the OpenAPI documentation |
| `AllowedEnvironments` | array | No | Environments where endpoint is available. Omit to allow all |
| `Documentation` | object | No | OpenAPI documentation metadata |

### Supported content types

- **JSON** (`application/json`) - With full OData filtering support
- **XML** (`application/xml`) - With OData filtering support  
- **CSV** (`text/csv`) - Raw file serving
- **Text** (`text/plain`) - Raw file serving
- **Images** (`image/*`) - Raw file serving

## Endpoint: composite

Composite entities orchestrate multiple operations in a single transaction. It's important to know that the composite request relies on the **Proxy** endpoint layer (meaning no other endpoint types can be used here). This also means each step inherits the `FallbackUrls` and `Retry` settings of the proxy endpoint it references. `ResponseTransforms` from referenced endpoints apply to the final composite response only, so data passed between steps stays complete for templating.

### Sales order example

```json
{
  "Type": "Composite",
  "Url": "http://localhost:8020/services/Exact.Entity.REST.EG",
  "Methods": ["POST"],
  "CompositeConfig": {
    "Name": "SalesOrder",
    "Description": "Creates a complete sales order with multiple order lines and a header",
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
  },
  "AllowedEnvironments": ["prod", "dev"]
}
```

### Property reference

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Type` | string | Yes | Must be "Composite" |
| `Url` | string | Yes | Base URL for all steps |
| `Methods` | array | Yes | Allowed HTTP methods |
| `CompositeConfig` | object | Yes | Composite configuration |
| `Deprecated` | boolean | No | Shows the endpoint's operations as deprecated in the OpenAPI documentation |
| `Hidden` | boolean | No | Leaves the endpoint out of the OpenAPI documentation; it keeps serving (default: `false`) |
| `Enabled` | boolean | No | Set to `false` to take the endpoint out of service; calls receive `503` (default: `true`) |
| `AllowedEnvironments` | array | No | Allowed environments |

### CompositeConfig properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Name` | string | Yes | Composite endpoint name |
| `Description` | string | No | Endpoint description |
| `Steps` | array | Yes | Execution steps |

### Step properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Name` | string | Yes | Step identifier |
| `Endpoint` | string | Yes | Target endpoint |
| `Method` | string | Yes | HTTP method |
| `IsArray` | boolean | No | Process as array |
| `ArrayProperty` | string | No | Array source property |
| `SourceProperty` | string | No | Input data property |
| `DependsOn` | string | No | Previous step dependency |
| `TemplateTransformations` | object | No | Dynamic value mappings |

### Template transformation variables

| Variable | Description | Example |
|----------|-------------|---------|
| `$guid` | New GUID value | Generates fresh GUID |
| `$requestid` | Request ID | Current request ID |
| `$prev.[step].[path]` | Previous step value | `$prev.CreateOrderLines.0.d.TransactionKey` |
| `$context.[variable]` | Context variable | `$context.customerId` |

## Endpoint: webhook

Webhook entities receive and store external webhook data.

### Example configuration

```json
{
  "DatabaseObjectName": "WebhookData",
  "DatabaseSchema": "dbo",
  "AllowedColumns": [
    "webhook1",
    "webhook2"
  ]
}
```

### Property reference

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `DatabaseObjectName` | string | Yes | Target table name |
| `DatabaseSchema` | string | No | Database schema |
| `AllowedColumns` | array | Yes | Allowed webhook IDs |
| `Deprecated` | boolean | No | Shows the endpoint's operations as deprecated in the OpenAPI documentation |
| `Hidden` | boolean | No | Leaves the endpoint out of the OpenAPI documentation; it keeps serving (default: `false`) |
| `Enabled` | boolean | No | Set to `false` to take the endpoint out of service; calls receive `503` (default: `true`) |
| `Documentation` | object | No | OpenAPI documentation metadata |

## Endpoint: files

File entities enable storage and retrieval of files through dedicated endpoints.

### Basic structure

```json
{
  "StorageType": "Local",
  "BaseDirectory": "documents",
  "AllowedExtensions": [".pdf", ".docx", ".xlsx", ".txt"],
  "Hidden": false,
  "AllowedEnvironments": ["prod", "dev"]
}
```

### With directory organization

```json
{
  "StorageType": "Local",
  "BaseDirectory": "customer-files/{env}",
  "AllowedExtensions": [".jpg", ".png", ".pdf", ".xlsx"],
  "Hidden": false,
  "AllowedEnvironments": ["prod", "dev"]
}
```

### Security-Restricted endpoint

```json
{
  "StorageType": "Local",
  "BaseDirectory": "secure-documents",
  "AllowedExtensions": [".pdf", ".xlsx"],
  "Hidden": true,
  "AllowedEnvironments": ["prod"]
}
```

### Property reference

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `StorageType` | string | Yes | Storage provider type (currently only "Local") |
| `BaseDirectory` | string | No | Base directory for file storage (default: endpoint name) |
| `AllowedExtensions` | array | No | Extensions accepted on upload, and the media types documented for the multipart part (empty allows all) |
| `Hidden` | boolean | No | Leaves the endpoint out of the OpenAPI documentation; it keeps serving (default: `false`) |
| `Enabled` | boolean | No | Set to `false` to take the endpoint out of service; calls receive `503` (default: `true`) |
| `Deprecated` | boolean | No | Shows the endpoint's operations as deprecated in the OpenAPI documentation |
| `AllowedEnvironments` | array | No | Environments that can access this endpoint |
| `Documentation` | object | No | OpenAPI documentation metadata |

## Troubleshooting

See the [troubleshooting guide](/guide/troubleshooting) for endpoint, environment, and file operation failures.

## Server configuration options

Endpoint files describe individual endpoints. The server-wide settings they depend on live elsewhere: file storage limits and blocked extensions in [Application settings](/reference/app-settings#file-storage-configuration), and the allowed environment list in [Environment settings](/reference/environment-settings#global-settings).

## Related topics

- [Environment Settings](/reference/environment-settings)
- [API Overview](/reference/)
- [SQL Endpoints](/guide/endpoints-sql)
- [Composite Endpoints](/guide/endpoints-composite)
- [File Operations](/guide/endpoints-file)
