# SQL Endpoints

> Expose SQL tables, views, stored procedures, and table-valued functions as REST endpoints with OData filtering.

:::warning
Before exposing any table or view through Portway, verify you understand the database permissions in play and the data contained in those objects. Portway enforces column-level restrictions — but only for columns you explicitly configure.
:::

SQL endpoints support four backends — SQL Server, PostgreSQL, MySQL, and SQLite. Portway selects the correct driver automatically from the connection string in the environment's `settings.json`. No changes to endpoint configuration are needed when working across providers.

:::info
Table-valued functions require SQL Server or PostgreSQL. Stored procedures are not available on SQLite. GET queries work across all four providers. See the [SQL Providers reference](/reference/sql-providers#capability-matrix) for the full capability matrix.
:::

## Configuration

Create `endpoints/SQL/{EndpointName}/entity.json`:

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

| Property | Required | Type | Description |
|---|---|---|---|
| `DatabaseObjectName` | Yes | string | Table, view, or function name in the database |
| `DatabaseSchema` | No | string | Database schema. Defaults to `dbo` |
| `PrimaryKey` | No | string | Primary key column name. Defaults to `Id` |
| `AllowedColumns` | No | array | Columns accessible via the API. Empty array exposes all columns |
| `AllowedMethods` | No | array | HTTP methods allowed. Defaults to `["GET"]` |
| `AllowedEnvironments` | No | array | Environments where this endpoint responds |
| `Procedure` | No | string | Stored procedure to call for write operations |
| `DatabaseObjectType` | No | string | Set to `TableValuedFunction` for TVF endpoints |

## Column aliases

Map internal column names to API-facing names using semicolon syntax in `AllowedColumns`:

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

The API accepts and returns `ProductNumber`, `ProductName`, and `Category`. Portway maps them to the underlying column names before querying the database.

```http
GET /api/prod/Items?$select=ProductNumber,ProductName&$filter=Category eq 'Electronics'
```

## Querying with OData

All GET requests support OData query parameters:

| Parameter | Description | Example |
|---|---|---|
| `$select` | Return specific columns | `$select=ProductName,Price` |
| `$filter` | Filter rows | `$filter=Price gt 100` |
| `$orderby` | Sort results | `$orderby=ProductName desc` |
| `$top` | Limit row count | `$top=50` |
| `$skip` | Skip rows (for pagination) | `$skip=20` |

Filter operators: `eq`, `ne`, `gt`, `lt`, `ge`, `le`, `and`, `or`, `contains()`

```http
GET /api/prod/Products?$filter=Price gt 100 and InStock eq true&$orderby=Price desc&$top=25
```

### Response format

```json
{
  "Count": 25,
  "Value": [
    { "ProductID": "abc123", "ProductName": "Gadget", "Price": 99.99 }
  ],
  "NextLink": "/api/prod/Products?$top=25&$skip=25"
}
```

## Write operations

### POST — create a record

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

### PUT — update a record

Include the primary key in the request body:

```http
PUT /api/prod/Products
Content-Type: application/json

{
  "ProductID": "abc123",
  "ProductName": "Updated Gadget",
  "Price": 249.99
}
```

### DELETE — remove a record

```http
DELETE /api/prod/Products?id=abc123
```

## Stored procedures

For write operations that require business logic, validation, or audit logging, configure a stored procedure:

```json
{
  "DatabaseObjectName": "ServiceRequests",
  "DatabaseSchema": "dbo",
  "Procedure": "dbo.sp_ManageServiceRequests",
  "AllowedMethods": ["GET", "POST", "PUT", "DELETE"],
  "AllowedColumns": ["RequestId", "CustomerCode", "Title", "Status"]
}
```

The procedure receives the HTTP method as `@Method` (`INSERT`, `UPDATE`, `PATCH`, `DELETE`):

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
Stored procedures handle write operations only. GET requests use the standard OData query path against `DatabaseObjectName` directly.
:::

## Table-valued functions

TVFs support parameterized queries — useful for reporting, generated datasets, or complex parameterized lookups that views cannot express.

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

Parameters can be sourced from `Path`, `Query`, or `Header`. Example calls:

```http
GET /api/dev/Departments/5?UserCount=25
GET /api/dev/Departments?UserCount=50&$top=20&$orderby=FirstName
```

:::info
`PrimaryKey` is not applicable to TVF endpoints.
:::

## Column-level access control

Use `AllowedColumns` to exclude sensitive fields from API responses and requests. Any column not listed is invisible to callers — it is neither returned in GET results nor accepted in POST/PUT bodies.

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

Columns containing credentials, SSNs, financial data, or internal system fields should be excluded explicitly rather than relying on callers not to request them.

## Troubleshooting

**"Column not allowed"** — The column is not listed in `AllowedColumns`, or the name does not match exactly (case-sensitive).

**"Method not allowed"** — Add the HTTP method to `AllowedMethods`, and ensure the stored procedure handles it if one is configured.

**No results returned** — Verify filter syntax, check that data exists in the target environment, and confirm database permissions for the connection string account.

**Performance issues** — Add indexes on columns used in `$filter` and `$orderby`. Use `$top` to limit result set size. Consider stored procedures for complex multi-table queries.

To increase log verbosity:

```json
{
  "Logging": {
    "LogLevel": {
      "PortwayApi.Classes.EndpointController": "Debug"
    }
  }
}
```

## Next steps

- [Proxy Endpoints](./endpoints-proxy)
- [Composite Endpoints](./endpoints-composite)
- [Environments](./environments)
- [Security](./security)
