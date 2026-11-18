---
title: SQL Endpoints
description: "Expose SQL tables, views, stored procedures, and table-valued functions as REST endpoints with OData filtering"
---

# SQL Endpoints

SQL endpoints expose a table, view, stored procedure or table-valued function as a REST resource. A plain `GET` returns rows (default `$top=10`); OData query options are optional on every SQL endpoint and need no configuration.

Supported providers are:

- SQL Server
- PostgreSQL
- MySQL/MariaDB
- SQLite

The provider is detected from the connection string in the environment's `settings.json`; endpoint configuration is the same for every provider. Provider differences: [SQL Providers](/reference/sql-providers#capabilities).

::: tip
`AllowedColumns` defines the exposed columns. Columns outside the list are not returned and not accepted in request bodies. Leave credentials, personal identifiers and internal system fields out of the list, and grant the connection account only the permissions the endpoints use ([SQL Server permissions](/guide/security#sql-server-permissions)).
:::

## Configuration

Each endpoint is defined in its own `entity.json`:

```json [endpoints/SQL/{EndpointName}/entity.json]
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

All properties, types and defaults: [Entity configuration](/reference/entity-config#sql).

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

GET requests accept `$select`, `$filter`, `$orderby`, `$top`, `$skip`, `$count` and `$expand`. Column names are the public names from `AllowedColumns`. Syntax: [OData](/reference/odata), [Filter operations](/reference/filters).

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

Write operations with business logic, validation or audit logging use a stored procedure. Stored procedures require SQL Server, PostgreSQL or MySQL/MariaDB; SQLite uses [table write mode](#table-write-mode).

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
        -- update logic; use ISNULL(@Field, Field) for partial updates
    ELSE IF @Method = 'DELETE'
        -- delete logic
END
```

:::info
Stored procedures run writes. GET requests query `DatabaseObjectName` through OData.
:::

## Table write mode

`WriteMode: Table` writes to the table without a stored procedure. SQLite endpoints use this mode for writes.

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

* `AllowedColumns` and `PrimaryKey` are required. Without either, Portway refuses every write and logs a configuration error at startup.
* A payload field outside `AllowedColumns` rejects the request.
* Values and keys are converted to the column type from the schema metadata before the statement runs. A value that does not fit a numeric column returns `400 Invalid column value`; a key that does not fit returns `400 Invalid record ID`. Date and time strings are written as date values, in UTC for time-zone-aware columns. SQLite stores date strings as sent.
* Updates and deletes filter on the primary key. An unmatched key returns `404`.
* `WriteMode` and `Procedure` are mutually exclusive.

Table mode works on every provider. Example: `WMS/Bins` in the SQLite demo environment.

## Table-valued functions

Table-valued functions accept parameters, for queries a view cannot express. They require SQL Server or PostgreSQL.

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

Parameter sources are `Path`, `Query` and `Header`. `Header` values are client-supplied; parameters named in `Tenancy` receive the token's [tenant value](/guide/tenant-headers). Example calls:

```http
GET /api/dev/Departments/5?UserCount=25
GET /api/dev/Departments?UserCount=50&$top=20&$orderby=FirstName
```

:::info
Table-valued function endpoints ignore `PrimaryKey`.
:::

## Caching

GET and QUERY responses are cached per endpoint. A write through any SQL endpoint invalidates the cached reads of every endpoint on the same table. View, table-valued function and procedure endpoints list the tables they read or write in `Properties.CacheInvalidatedBy`:

```json
"Properties": {
  "CacheDurationMinutes": 5,
  "CacheInvalidatedBy": ["dbo.Items"]
}
```

Settings and invalidation rules: [Caching](/reference/caching#cache-invalidation).

## Troubleshooting

| Symptom | Resolution |
|---|---|
| "Column not allowed" | Add the column to `AllowedColumns`. Names are case-sensitive. |
| "Invalid column value" or "Invalid record ID" | The value does not match the column type. Send numbers for numeric columns and ISO 8601 dates for date columns. |
| "Method not allowed" | Add the method to `AllowedMethods`. A configured procedure must accept the method. |
| No results | Check the filter, the data in the target environment and the connection account's permissions. |
| Slow queries | Index columns used in `$filter` and `$orderby`; limit with `$top`. |

Debug logging: [Log levels](/guide/monitoring#log-levels).
