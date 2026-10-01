---
title: SQL Endpoints
description: "Expose SQL tables, views, stored procedures, and table-valued functions as REST endpoints with OData filtering"
---

# SQL Endpoints

SQL endpoints expose a table, view, stored procedure or table-valued function as a REST resource with OData queries. Supported providers are SQL Server, PostgreSQL, MySQL/MariaDB and SQLite. The provider is detected from the connection string in the environment's `settings.json`; endpoint configuration is the same for every provider.

::: tip
Review the database permissions of the connection account and the data in each exposed object. Portway restricts columns only through `AllowedColumns`.
:::

:::info Info
Table-valued functions require SQL Server or PostgreSQL. Stored procedures are not available on SQLite. GET queries work on all four providers. Capability matrix: [SQL Providers](/reference/sql-providers#capability-matrix).
:::

## Configuration

Each endpoint is defined in `endpoints/SQL/{EndpointName}/entity.json`:

```json
{
  "DatabaseObjectName": "Products",
  "DatabaseSchema": "dbo",
  "PrimaryKey": "ProductID",
  "AllowedColumns": [
    "ProductID",
    "ProductName",
    "Category",
    "Price",
    "InStock"
  ],
  "AllowedMethods": ["GET", "POST", "PUT", "DELETE"],
  "AllowedEnvironments": ["dev", "test", "prod"]
}
```

### Configuration properties

All properties, types and defaults: [Entity configuration](/reference/entity-config#endpoint-sql).

## Column aliases

An `AllowedColumns` entry `DbColumn;PublicName` maps a database column to a public name:

```json
{
  "DatabaseObjectName": "Items",
  "AllowedColumns": [
    "ItemCode;ProductNumber",
    "Description;ProductName",
    "Assortment;Category"
  ]
}
```

The API accepts and returns `ProductNumber`, `ProductName` and `Category`; queries use the database column names.

```http
GET /api/prod/Items?$select=ProductNumber,ProductName&$filter=Category eq 'Electronics'
```

## Querying with OData

GET requests accept these OData query options:

| Parameter | Description | Example |
|---|---|---|
| `$select` | Return specific columns | `$select=ProductName,Price` |
| `$filter` | Filter rows | `$filter=Price gt 100` |
| `$orderby` | Sort results | `$orderby=ProductName desc` |
| `$top` | Limit row count | `$top=50` |
| `$skip` | Skip rows (for pagination) | `$skip=20` |
| `$count` | Add the total matching count as `totalCount` | `$count=true` |

Filter operators: `eq`, `ne`, `gt`, `lt`, `ge`, `le`, `and`, `or`, `not`, `contains()`, `startswith()`, `endswith()`.

```http
GET /api/prod/Products?$filter=Price gt 100 and InStock eq true&$orderby=Price desc&$top=25
```

### Response format

```json
{
  "success": true,
  "count": 25,
  "value": [
    { "ProductID": "abc123", "ProductName": "Gadget", "Price": 99.99 }
  ],
  "nextLink": "/api/prod/Products?$top=25&$skip=25"
}
```

### Related data with $expand

A to-one relationship to another SQL endpoint makes the related row available through `$expand`:

```json
{
  "DatabaseObjectName": "Items",
  "DatabaseObjectType": "Table",
  "AllowedColumns": ["ItemCode;ProductNumber", "Assortment;AssortmentID"],
  "Relationships": [
    { "Name": "Category", "Target": "Assortments", "LocalColumn": "Assortment", "TargetColumn": "AssortmentID" }
  ]
}
```

```http
GET /api/prod/Products?$expand=Category
```

The related row is nested under the navigation name and limited to the target endpoint's `AllowedColumns`. Supported on Table and View endpoints with to-one navigations; table-valued functions return `400`. Details: [Expanding Related Data](/reference/expand).

## Write operations

### POST: create a record

```http
POST /api/prod/Products
Content-Type: application/json

{
  "ProductName": "New Gadget",
  "Category": "Electronics",
  "Price": 299.99,
  "InStock": true
}
```

### PUT: update a record

The request body includes the primary key:

```http
PUT /api/prod/Products
Content-Type: application/json

{
  "ProductID": "abc123",
  "ProductName": "Updated Gadget",
  "Price": 249.99
}
```

### DELETE: remove a record

```http
DELETE /api/prod/Products?id=abc123
```

## Stored procedures

Write operations with business logic, validation or audit logging use a stored procedure:

```json
{
  "DatabaseObjectName": "ServiceRequests",
  "DatabaseSchema": "dbo",
  "Procedure": "dbo.sp_ManageServiceRequests",
  "AllowedMethods": ["GET", "POST", "PUT", "DELETE"],
  "AllowedColumns": ["RequestId", "CustomerCode", "Title", "Status"]
}
```

The procedure receives the operation as `@Method` (`INSERT`, `UPDATE`, `PATCH`, `DELETE`). `MERGE` requests arrive as `PATCH`:

```sql
CREATE PROCEDURE [dbo].[sp_ManageServiceRequests]
    @Method      NVARCHAR(10),
    @id          UNIQUEIDENTIFIER = NULL,
    @CustomerCode NVARCHAR(20) = NULL,
    @Title       NVARCHAR(100) = NULL,
    @Status      NVARCHAR(20) = NULL,
    @UserName    NVARCHAR(50) = NULL
AS
BEGIN
    IF @Method = 'INSERT'
        -- insert logic
    ELSE IF @Method = 'UPDATE'
        -- update logic; use ISNULL(@Field, Field) to handle partial updates
    ELSE IF @Method = 'DELETE'
        -- delete logic
END
```

:::info
Stored procedures handle writes only. GET requests query `DatabaseObjectName` through OData.
:::

## Table write mode

With `WriteMode: Table`, writes go directly to the table, without a stored procedure. This is the write path on SQLite.

```json
{
  "DatabaseObjectName": "Bins",
  "WriteMode": "Table",
  "PrimaryKey": "Id",
  "AllowedMethods": ["GET", "POST", "PUT", "PATCH", "DELETE"],
  "AllowedColumns": ["Id", "Code", "Zone", "CapacityUnits"],
  "RequiredColumns": ["Code", "Zone"]
}
```

Portway generates parameterized `INSERT`, `UPDATE` and `DELETE` statements with the OData query compiler. Rules:

* `AllowedColumns` and `PrimaryKey` are required; without either, all writes are refused and a configuration error is logged at startup.
* A payload field outside `AllowedColumns` rejects the request.
* Updates and deletes filter on the primary key; an unmatched key returns `404`.
* `WriteMode` and `Procedure` are mutually exclusive.

Table mode works on every provider. Stored procedures are recommended for business rules, validation chains and audit requirements. Example: `WMS/Bins` in the SQLite demo environment.

## Table-valued functions

Table-valued functions accept parameters, for queries a view cannot express.

```json
{
  "DatabaseObjectName": "fn_GetDepartmentUsers",
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
  "AllowedMethods": ["GET"]
}
```

Parameter sources are `Path`, `Query` and `Header`. `Header` values are client-supplied; parameters named in `Tenancy` receive the token's [tenant value](/guide/security#tenant-headers). Example calls:

```http
GET /api/dev/Departments/5?UserCount=25
GET /api/dev/Departments?UserCount=50&$top=20&$orderby=FirstName
```

:::info
Table-valued function endpoints ignore `PrimaryKey`.
:::

## Column-level access control

Columns missing from `AllowedColumns` are not returned by GET and not accepted in POST or PUT bodies.

```json
{
  "DatabaseObjectName": "Customers",
  "AllowedColumns": [
    "CustomerID",
    "CompanyName",
    "ContactName"
  ]
}
```

Leave credentials, personal identifiers, financial data and internal system fields out of `AllowedColumns`.

## Troubleshooting

| Symptom | Resolution |
|---|---|
| "Column not allowed" | Add the column to `AllowedColumns`; names are case-sensitive. |
| "Method not allowed" | Add the method to `AllowedMethods`; a configured procedure must handle it. |
| No results | Check the filter, the data in the target environment and the connection account's permissions. |
| Slow queries | Index columns used in `$filter` and `$orderby`; limit with `$top`. |

Debug logging:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug"
    }
  }
}
```

## Next steps

- [Proxy Endpoints](/guide/endpoints-proxy)
- [Composite Endpoints](/guide/endpoints-composite)
- [Environments](/guide/environments)
- [Security](/guide/security)
