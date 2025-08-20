# SQL Endpoints

SQL endpoints in Portway provide secure, controlled access to your SQL Server databases through a RESTful API interface. These endpoints support OData queries and enable CRUD operations on your database tables with fine-grained column-level access control.

## Overview

SQL endpoints allow you to:
- Expose specific tables or views as REST endpoints
- Control which columns are accessible
- Enable specific HTTP methods (GET, POST, PUT, DELETE)
- Execute stored procedures for complex operations
- Apply environment-specific configurations

## Configuration

### Basic SQL Endpoint Configuration

Create a JSON file in the `endpoints/SQL/{EndpointName}/entity.json` directory:

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

### Configuration Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `DatabaseObjectName` | string | Yes | The table or view name in the database |
| `DatabaseSchema` | string | No | Database schema (defaults to "dbo") |
| `PrimaryKey` | string | No | Primary key column name (defaults to "Id") |
| `AllowedColumns` | array | No | List of accessible columns (empty = all columns) |
| `AllowedMethods` | array | No | HTTP methods allowed (defaults to ["GET"]) |
| `AllowedEnvironments` | array | No | Environments where endpoint is available |
| `Procedure` | string | No | Stored procedure for POST/PUT/DELETE operations |

## Using SQL Endpoints

### GET Requests (Query Data)

SQL endpoints support OData query parameters for filtering, sorting, and pagination:

```http
GET /api/prod/Products?$filter=Category eq 'Electronics'&$orderby=Price desc&$top=10
```

#### OData Query Parameters

| Parameter | Description | Example |
|-----------|-------------|---------|
| `$select` | Choose specific columns | `$select=ProductName,Price` |
| `$filter` | Filter results | `$filter=Price gt 100` |
| `$orderby` | Sort results | `$orderby=ProductName desc` |
| `$top` | Limit results | `$top=50` |
| `$skip` | Skip records | `$skip=20` |

#### Filter Operators

```
eq    - Equals
ne    - Not equals
gt    - Greater than
lt    - Less than
ge    - Greater than or equal
le    - Less than or equal
and   - Logical and
or    - Logical or
```

Example filters:
```http
# Simple equality
$filter=Category eq 'Books'

# Numeric comparison
$filter=Price gt 50

# String contains
$filter=contains(ProductName, 'Phone')

# Multiple conditions
$filter=Price gt 100 and InStock eq true
```

### POST Requests (Create Data)

Create new records by sending JSON data:

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

### PUT Requests (Update Data)

Update existing records. The ID must be included in the request body:

```http
PUT /api/prod/Products
Content-Type: application/json

{
  "id": "abc123",
  "ProductName": "Updated Gadget",
  "Price": 249.99
}
```

### DELETE Requests

Delete records by ID:

```http
DELETE /api/prod/Products?id=abc123
```

## Stored Procedure Integration

For complex operations, configure a stored procedure:

```json
{
  "DatabaseObjectName": "ServiceRequests",
  "DatabaseSchema": "dbo",
  "Procedure": "dbo.sp_ManageServiceRequests",
  "AllowedMethods": ["GET", "POST", "PUT", "DELETE"],
  "AllowedColumns": [
    "RequestId",
    "CustomerCode",
    "Title",
    "Status"
  ]
}
```

The stored procedure should handle different operations based on the `@Method` parameter:

```sql
CREATE PROCEDURE [dbo].[sp_ManageServiceRequests]
    @Method NVARCHAR(10),
    @id UNIQUEIDENTIFIER = NULL,
    @CustomerCode NVARCHAR(20) = NULL,
    @Title NVARCHAR(100) = NULL,
    @Status NVARCHAR(20) = NULL,
    @UserName NVARCHAR(50) = NULL
AS
BEGIN
    IF @Method = 'INSERT'
        -- Insert logic
    ELSE IF @Method = 'UPDATE'
        -- Update logic
    ELSE IF @Method = 'DELETE'
        -- Delete logic
END
```

:::tip
Stored procedures provide better control over complex business logic and can include audit logging, validation, and transaction management.
:::

## Security Considerations

### Column-Level Access Control

Restrict access to sensitive columns:

```json
{
  "DatabaseObjectName": "Customers",
  "AllowedColumns": [
    "CustomerID",
    "CompanyName",
    "ContactName"
    // Excludes sensitive fields like SSN, CreditCard, etc.
  ]
}
```

### Environment Restrictions

Limit endpoints to specific environments:

```json
{
  "AllowedEnvironments": ["prod", "staging"]
  // Not available in dev or test
}
```

:::warning
Always use the `AllowedColumns` property to prevent exposure of sensitive data. Never expose columns containing passwords, SSNs, or other PII without proper security measures.
:::

## Database Object Requirements

### Required SQL Objects

For endpoints using stored procedures, create the following:

1. **Main table**:
```sql
CREATE TABLE [dbo].[YourTable] (
    [Id] UNIQUEIDENTIFIER PRIMARY KEY,
    [Field1] NVARCHAR(100),
    [Field2] INT,
    -- other fields
);
```

2. **Stored procedure** (if using):
```sql
CREATE PROCEDURE [dbo].[sp_ManageYourTable]
    @Method NVARCHAR(10),
    -- parameters matching your fields
AS
BEGIN
    -- CRUD operations based on @Method
END
```

3. **Indexes** for performance:
```sql
CREATE INDEX IX_YourTable_CommonQueryField 
ON [dbo].[YourTable] (CommonQueryField);
```

## Response Format

SQL endpoints return JSON responses with the following structure:

### Collection Response
```json
{
  "Count": 25,
  "Value": [
    {
      "ProductID": "abc123",
      "ProductName": "Gadget",
      "Price": 99.99
    }
    // ... more items
  ],
  "NextLink": "/api/prod/Products?$top=10&$skip=10"
}
```

### Single Item Response
```json
{
  "ProductID": "abc123",
  "ProductName": "Gadget",
  "Category": "Electronics",
  "Price": 99.99,
  "InStock": true
}
```

### Error Response
```json
{
  "error": "Invalid request",
  "detail": "The Price field must be a positive number",
  "success": false
}
```

## Troubleshooting

### Common Issues

1. **"Column not allowed" error**
   - Check `AllowedColumns` in your configuration
   - Ensure the column name matches exactly (case-sensitive)

2. **"Method not allowed" error**
   - Verify `AllowedMethods` includes the HTTP method you're using
   - Check that the stored procedure handles the method

3. **No results returned**
   - Verify your filter syntax
   - Check if data exists in the specified environment
   - Ensure proper permissions on the database

4. **Performance issues**
   - Add appropriate indexes to frequently queried columns
   - Use `$top` to limit result sets
   - Consider implementing stored procedures for complex queries

### Logging

Enable detailed logging to troubleshoot issues:

```json
{
  "Logging": {
    "LogLevel": {
      "PortwayApi.Classes.EndpointController": "Debug"
    }
  }
}
```

## Best Practices

1. **Use Stored Procedures for Write Operations**
   - Provides better control and validation
   - Enables audit logging
   - Supports complex business logic

2. **Implement Proper Indexing**
   - Index columns used in filters and sorting
   - Consider composite indexes for multi-column queries

3. **Limit Result Sets**
   - Always use `$top` for large tables
   - Implement default limits in your configuration

4. **Secure Sensitive Data**
   - Use `AllowedColumns` to restrict access
   - Consider separate endpoints for sensitive operations
   - Implement row-level security when needed

## Next Steps

- Learn about [Proxy Endpoints](/guide/endpoints/proxy) for forwarding requests
- Explore [Composite Endpoints](/guide/endpoints/composite) for complex operations
- Review [Security Best Practices](/guide/security) for protecting your data
- Set up [Environment Configuration](/guide/environments) for multi-stage deployments