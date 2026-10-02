---
title: Entity Configuration
description: "entity.json properties for SQL, Proxy, Static, Composite, Webhook and File endpoints"
---

# Entity Configuration

Each endpoint is an `entity.json` in its own folder under `endpoints/`. The type folder sets the endpoint type; a folder between the type folder and the endpoint folder sets the [namespace](/reference/namespaces).

```
endpoints/
  ├── SQL/[Namespace]/[Name]/entity.json
  ├── Proxy/[Namespace]/[Name]/entity.json      # Proxy and Composite
  ├── Static/[Namespace]/[Name]/entity.json     # plus the content file
  ├── Files/[Namespace]/[Name]/entity.json
  └── Webhooks/[Namespace]/[Name]/entity.json   # namespace required
```

## Common properties

| Property | Type | Default | Description |
|---|---|---|---|
| `AllowedEnvironments` | string[] | All | Environments that serve the endpoint |
| `Enabled` | boolean | `true` | `false` returns `503` and removes the endpoint from MCP |
| `Hidden` | boolean | `false` | Omits the endpoint from the OpenAPI document; requests are served |
| `Deprecated` | boolean | `false` | Marks the operations deprecated in the OpenAPI document |
| `Namespace` | string | Folder | [Namespaces](/reference/namespaces) |
| `NamespaceDisplayName` | string | | Namespace label in the documentation |
| `DisplayName` | string | | Endpoint label |
| `Documentation` | object | | [OpenAPI settings](/reference/openapi-settings#endpoint-documentation) |
| `Mcp` | object | | [MCP exposure](/guide/mcp) |
| `Tenancy` | object | | [Tenant headers](/guide/tenant-headers) |

## SQL

```json
{
  "DatabaseObjectName": "Items",
  "DatabaseSchema": "dbo",
  "PrimaryKey": "ItemCode",
  "AllowedColumns": ["ItemCode;ProductNumber", "Description;ProductName", "Assortment"],
  "AllowedMethods": ["GET", "POST", "PUT"],
  "Procedure": "dbo.sp_ManageItems",
  "AllowedEnvironments": ["prod", "dev"]
}
```

| Property | Type | Default | Description |
|---|---|---|---|
| `DatabaseObjectName` | string | Required | Table, view or function |
| `DatabaseSchema` | string | Provider default | [Schemas](/reference/sql-providers#schemas) |
| `DatabaseObjectType` | string | `Table` | `Table`, `View` or `TableValuedFunction` |
| `AllowedColumns` | string[] | Required | Exposed columns; `"DbColumn;PublicName"` sets an alias used in responses and OData |
| `PrimaryKey` | string | `Id` | Key for `/{id}` requests |
| `AllowedMethods` | string[] | `["GET"]` | `GET`, `QUERY`, `POST`, `PUT`, `PATCH`, `MERGE` (alias of `PATCH`), `DELETE` |
| `Procedure` | string | | Stored procedure for writes |
| `WriteMode` | string | | `Table` writes directly to the table instead of a procedure |
| `RequiredColumns` | string[] | | Columns required on create |
| `ColumnValidation` | object | | Validation rules per column |
| `Relationships` | array | | To-one navigations for [`$expand`](/reference/expand) |
| `FunctionParameters` | array | | Table-valued function parameters |
| `ResponseTransforms` | object | | `Remove`, `Rename` and `Mask` rules on result fields |
| `Properties` | object | | `MaxPageSize` (upper limit for `$top`), `DefaultSort` (`$orderby` when none is given), `CacheEnabled` (`false` bypasses the response cache) |

Writes, validation and table-valued functions: [SQL Endpoints](/guide/endpoints-sql).

### Table-valued function parameters

```json
{
  "DatabaseObjectName": "GenerateSampleUsers",
  "DatabaseObjectType": "TableValuedFunction",
  "FunctionParameters": [
    { "Name": "DepartmentId", "SqlType": "int", "Source": "Path", "Position": 1, "ValidationPattern": "^[0-9]+$" },
    { "Name": "UserCount", "SqlType": "int", "Source": "Query", "Required": false, "DefaultValue": "DEFAULT" }
  ],
  "AllowedColumns": ["user_id;UserId", "first_name;FirstName"]
}
```

Table-valued functions are read-only and use no `PrimaryKey`.

## Proxy

```json
{
  "Url": "http://erp-primary.company.local/api/orders",
  "Methods": ["GET", "POST", "PUT", "DELETE"],
  "FallbackUrls": ["http://erp-standby.company.local/api/orders"],
  "Retry": { "Attempts": 2, "DelayMs": 200 },
  "ResponseTransforms": {
    "Remove": ["internalNotes"],
    "Rename": { "cust_nm": "customerName" },
    "Mask": ["ssn"]
  }
}
```

| Property | Type | Default | Description |
|---|---|---|---|
| `Url` | string | Required | Upstream URL |
| `Methods` | string[] | Required | Allowed methods |
| `FallbackUrls` | string[] | | Tried in order after the primary fails |
| `Retry` | object | 1 attempt | `Attempts` per URL, `DelayMs` between attempts (default `200`) |
| `ResponseTransforms` | object | | `Remove`, `Rename` and `Mask` rules on JSON fields |
| `SupportsOData` | boolean | `false` | Documents OData parameters for the upstream |
| `DeletePatterns` | array | `PathParameter` | Id format on `DELETE` |
| `CustomProperties` | object | | Method translation and content type |

A connection failure, timeout, `502`, `503` or `504` moves to the next attempt. Transforms apply to top-level fields of objects, array elements and items in a `value` wrapper; `Remove` wins over other rules, masked values return `***`, non-JSON responses are unchanged, and cached responses are stored after transformation.

| `DeletePatterns` style | Upstream URL |
|---|---|
| `PathParameter` | `/customers/{id}` |
| `QueryParameter` (with `Parameter`) | `/customers?id={id}` |
| `ODataGuid` | `/customers(guid'{id}')` |
| `ODataKey` | `/orders({id})` |

| `CustomProperties` key | Example | Effect |
|---|---|---|
| `ContentType` | `application/xml` | Request Content-Type and Accept |
| `HttpMethodTranslation` | `PUT:MERGE,POST:CREATE` | Method sent upstream |
| `HttpMethodAppendHeaders` | `PUT:X-HTTP-Method={ORIGINAL_METHOD}` | Headers added per method |

## Static

```json
{
  "ContentType": "application/json",
  "ContentFile": "countries.json",
  "EnableFiltering": true
}
```

| Property | Type | Default | Description |
|---|---|---|---|
| `ContentFile` | string | `content.txt` | File in the endpoint folder |
| `ContentType` | string | `text/plain` | Response media type |
| `EnableFiltering` | boolean | `false` | OData on JSON and XML content |

## Composite

```json
{
  "Type": "Composite",
  "Url": "http://localhost:8020/services/Exact.Entity.REST.EG",
  "Methods": ["POST"],
  "CompositeConfig": {
    "Name": "SalesOrder",
    "Steps": [
      {
        "Name": "CreateOrderLines",
        "Endpoint": "SalesOrderLine",
        "Method": "POST",
        "IsArray": true,
        "ArrayProperty": "Lines",
        "TemplateTransformations": { "TransactionKey": "$guid" }
      },
      {
        "Name": "CreateOrderHeader",
        "Endpoint": "SalesOrderHeader",
        "Method": "POST",
        "SourceProperty": "Header",
        "TemplateTransformations": { "TransactionKey": "$prev.CreateOrderLines.0.d.TransactionKey" }
      }
    ]
  }
}
```

Steps call Proxy endpoints and use their `FallbackUrls` and `Retry`; their `ResponseTransforms` apply to the final response only. Steps run in order and are not a transaction.

| Step property | Description |
|---|---|
| `Name` | Step name, referenced by `$prev` and `DependsOn` |
| `Endpoint` | Proxy endpoint name |
| `Method` | HTTP method |
| `IsArray` / `ArrayProperty` | One call per element of the request property |
| `SourceProperty` | Request property used as the body |
| `DependsOn` | Step whose result is the body |
| `TemplateTransformations` | Field values set from variables |

| Variable | Value |
|---|---|
| `$guid` | New GUID |
| `$requestid` | Request id |
| `$prev.{step}.{path}` | Value from an earlier step result |
| `$context.{name}` | Context variable |

## Webhook

```json
{
  "DatabaseObjectName": "WebhookData",
  "DatabaseSchema": "dbo",
  "AllowedColumns": ["webhook1", "webhook2"]
}
```

| Property | Type | Description |
|---|---|---|
| `DatabaseObjectName` | string | Target table |
| `DatabaseSchema` | string | Schema |
| `AllowedColumns` | string[] | Accepted webhook ids |

## File

```json
{
  "StorageType": "Local",
  "BaseDirectory": "customer-files/{year}",
  "AllowedExtensions": [".jpg", ".png", ".pdf"]
}
```

| Property | Type | Default | Description |
|---|---|---|---|
| `StorageType` | string | `Local` | Storage provider |
| `BaseDirectory` | string | Storage root | Folder below `FileStorage:StorageDirectory/{env}`, or an absolute path; supports placeholders |
| `AllowedExtensions` | string[] | All not blocked | Accepted extensions; also the documented upload media types |

Placeholders and tenant folders: [File Endpoints](/guide/endpoints-file). Upload limits and blocked extensions: [Application Settings](/reference/app-settings#filestorage).

## Related topics

- [Environment Settings](/reference/environment-settings)
- [Namespaces](/reference/namespaces)
- [SQL Endpoints](/guide/endpoints-sql)
- [Composite Endpoints](/guide/endpoints-composite)
- [File Endpoints](/guide/endpoints-file)
