# Entity Configuration

Entity configuration files define how endpoints behave and what data they expose. Each endpoint type (SQL, Proxy, Composite, Webhook, File) has specific configuration options.

## File Structure

Entity configuration files are JSON files located in the endpoints directory structure:

```
/endpoints/
  ├── SQL/
  │   └── [EntityName]/
  │       └── entity.json
  ├── Proxy/
  │   └── [EntityName]/
  │       └── entity.json
  ├── Composite/
  │   └── [EntityName]/
  │       └── entity.json
  ├── Webhooks/
  │   └── entity.json
  └── Files/
      └── [EntityName]/
          └── entity.json
```

## SQL Entity Configuration

SQL entities expose database tables or views through OData endpoints.

### Basic Structure

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

### With Stored Procedures

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

### Property Reference

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `DatabaseObjectName` | string | Yes | Name of the table or view |
| `DatabaseSchema` | string | No | Database schema (default: "dbo") |
| `PrimaryKey` | string | No | Primary key column (default: "Id") |
| `AllowedColumns` | array | Yes | List of accessible columns |
| `Procedure` | string | No | Stored procedure for data operations |
| `AllowedMethods` | array | No | HTTP methods (default: ["GET"]) |
| `AllowedEnvironments` | array | No | Allowed environments (default: all) |

## Proxy Entity Configuration

Proxy entities forward requests to internal web services.

### Basic Example

```json
{
  "Url": "http://localhost:8020/services/Exact.Entity.REST.EG/Account",
  "Methods": ["GET", "POST", "PUT", "DELETE", "MERGE"]
}
```

### With Environment Restrictions

```json
{
  "Url": "http://localhost:8020/services/Exact.Entity.REST.EG/Classification",
  "Methods": ["GET"],
  "AllowedEnvironments": ["prod", "dev"]
}
```

### Private Endpoint

```json
{
  "Url": "http://localhost:8020/services/Exact.Entity.REST.EG/SalesOrderHeader",
  "Methods": ["POST"],
  "IsPrivate": true
}
```

### Property Reference

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Url` | string | Yes | Target service URL |
| `Methods` | array | Yes | Allowed HTTP methods |
| `IsPrivate` | boolean | No | Hide from API documentation |
| `AllowedEnvironments` | array | No | Allowed environments |

## Composite Entity Configuration

Composite entities orchestrate multiple operations in a single transaction.

### Sales Order Example

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

### Property Reference

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Type` | string | Yes | Must be "Composite" |
| `Url` | string | Yes | Base URL for all steps |
| `Methods` | array | Yes | Allowed HTTP methods |
| `CompositeConfig` | object | Yes | Composite configuration |
| `AllowedEnvironments` | array | No | Allowed environments |

### CompositeConfig Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Name` | string | Yes | Composite endpoint name |
| `Description` | string | No | Endpoint description |
| `Steps` | array | Yes | Execution steps |

### Step Properties

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

### Template Transformation Variables

| Variable | Description | Example |
|----------|-------------|---------|
| `$guid` | New GUID value | Generates fresh GUID |
| `$requestid` | Request ID | Current request ID |
| `$prev.[step].[path]` | Previous step value | `$prev.CreateOrderLines.0.d.TransactionKey` |
| `$context.[variable]` | Context variable | `$context.customerId` |

## Webhook Entity Configuration

Webhook entities receive and store external webhook data.

### Example Configuration

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

### Property Reference

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `DatabaseObjectName` | string | Yes | Target table name |
| `DatabaseSchema` | string | No | Database schema |
| `AllowedColumns` | array | Yes | Allowed webhook IDs |

## File Entity Configuration

File entities enable storage and retrieval of files through dedicated endpoints.

### Basic Structure

```json
{
  "StorageType": "Local",
  "BaseDirectory": "documents",
  "AllowedExtensions": [".pdf", ".docx", ".xlsx", ".txt"],
  "IsPrivate": false,
  "AllowedEnvironments": ["600", "700"]
}
```

### With Directory Organization

```json
{
  "StorageType": "Local",
  "BaseDirectory": "customer-files/{env}",
  "AllowedExtensions": [".jpg", ".png", ".pdf", ".xlsx"],
  "IsPrivate": false,
  "AllowedEnvironments": ["600", "700"]
}
```

### Security-Restricted Endpoint

```json
{
  "StorageType": "Local",
  "BaseDirectory": "secure-documents",
  "AllowedExtensions": [".pdf", ".xlsx"],
  "IsPrivate": true,
  "AllowedEnvironments": ["600"]
}
```

### Property Reference

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `StorageType` | string | Yes | Storage provider type (currently only "Local") |
| `BaseDirectory` | string | No | Base directory for file storage (default: endpoint name) |
| `AllowedExtensions` | array | No | List of allowed file extensions (empty = allow all) |
| `IsPrivate` | boolean | No | Whether endpoint is hidden from documentation (default: false) |
| `AllowedEnvironments` | array | No | Environments that can access this endpoint |

## Common Configuration Patterns

### Environment-Specific Access

Control which environments can access an endpoint:

```json
{
  "AllowedEnvironments": ["prod", "dev", "test"]
}
```

### Method Restrictions

Limit HTTP methods for security:

```json
{
  "AllowedMethods": ["GET", "POST"]
}
```

### Private Endpoints

Hide endpoints from API documentation:

```json
{
  "IsPrivate": true
}
```

### Stored Procedure Integration

Use stored procedures for data operations:

```json
{
  "Procedure": "dbo.sp_ManageServiceRequests",
  "AllowedMethods": ["GET", "POST", "PUT", "DELETE"]
}
```

### File Organization Patterns

Organize files by environment and type:

```json
{
  "BaseDirectory": "reports/{env}/monthly",
  "AllowedExtensions": [".pdf", ".xlsx"]
}
```

## Best Practices

### 1. Column Security

Only expose necessary columns:

```json
{
  "AllowedColumns": [
    "PublicId",
    "DisplayName",
    "PublicData"
    // Exclude sensitive columns like passwords, internal IDs
  ]
}
```

### 2. Environment Restrictions

Limit access by environment:

```json
{
  "AllowedEnvironments": ["prod"],  // Production only
  "AllowedEnvironments": ["dev", "test"]  // Non-production only
}
```

### 3. Method Limitations

Follow REST principles:

```json
{
  // Read-only endpoint
  "AllowedMethods": ["GET"],
  
  // Full CRUD
  "AllowedMethods": ["GET", "POST", "PUT", "DELETE"],
  
  // Create-only
  "AllowedMethods": ["POST"]
}
```

### 4. Composite Transaction Safety

Ensure atomic operations:

```json
{
  "Steps": [
    {
      "Name": "ValidateData",
      "Endpoint": "ValidationService",
      "Method": "POST"
    },
    {
      "Name": "CreateRecord",
      "Endpoint": "MainService",
      "Method": "POST",
      "DependsOn": "ValidateData"
    }
  ]
}
```

### 5. File Type Restrictions

Restrict file types for security:

```json
{
  "AllowedExtensions": [
    ".pdf",      // ✓ Document format
    ".xlsx",     // ✓ Spreadsheet
    ".jpg",      // ✓ Image format
    ".txt",      // ✓ Text format
    // Avoid executable or script files
  ]
}
```

### 6. File Storage Segmentation

Separate files by purpose:

```json
// Documents endpoint
{
  "BaseDirectory": "documents",
  "AllowedExtensions": [".pdf", ".docx", ".txt"]
}

// Images endpoint
{
  "BaseDirectory": "images",
  "AllowedExtensions": [".jpg", ".png", ".gif"]
}

// Data endpoint
{
  "BaseDirectory": "data",
  "AllowedExtensions": [".csv", ".xlsx", ".json"]
}
```

## Troubleshooting

### Common Issues

1. **Endpoint Not Found**
   - Verify file location: `/endpoints/[Type]/[EntityName]/entity.json`
   - Check JSON syntax
   - Ensure file permissions

2. **Method Not Allowed**
   - Check `AllowedMethods` array
   - Verify method name spelling
   - Consider environment restrictions

3. **Environment Access Denied**
   - Verify `AllowedEnvironments` includes target environment
   - Check environment name spelling
   - Ensure environment is configured in settings

4. **Composite Step Failures**
   - Verify endpoint names match exactly
   - Check step dependencies
   - Validate transformation syntax
   - Review step order

5. **File Upload Failures**
   - Check file extension against `AllowedExtensions`
   - Verify file size is within limits
   - Ensure base directory exists and is writable
   - Check disk space availability

6. **File Download Issues**
   - Validate file ID format
   - Check file existence in storage
   - Verify environment matches upload environment
   - Ensure permissions on storage location

### Validation Checklist

- [ ] Valid JSON syntax
- [ ] Required properties present
- [ ] Endpoint names match folder names
- [ ] URLs are accessible
- [ ] Methods are properly capitalized
- [ ] Environment names match configuration
- [ ] Column names match database schema
- [ ] Stored procedure exists in database
- [ ] File extensions in correct format (e.g., ".pdf" not "pdf")
- [ ] Storage directories exist and are writable

## Server Configuration Options

### File Storage Configuration

Additional options can be set in the server's `appsettings.json`:

```json
"FileStorage": {
  "StorageDirectory": "files",          // Root directory for all files
  "MaxFileSizeBytes": 52428800,         // 50MB default
  "UseMemoryCache": true,               // Enable memory caching
  "MemoryCacheTimeSeconds": 60,         // Cache duration
  "MaxTotalMemoryCacheMB": 200,         // Memory cache limit
  "BlockedExtensions": [                // Globally blocked extensions
    ".exe", ".dll", ".bat", ".sh", 
    ".cmd", ".msi", ".vbs"
  ]
}
```

### Environment Configuration

Configure allowed environments in `environments/settings.json`:

```json
{
  "Environment": {
    "ServerName": "VM2K22",
    "AllowedEnvironments": ["600", "700", "Synergy"]
  }
}
```

## Related Topics

- [Environment Settings](/reference/configuration/environment-settings) - Environment configuration
- [API Overview](/reference/api/overview) - API endpoint patterns
- [SQL Endpoints](/guide/endpoints/sql) - SQL endpoint guide
- [Composite Endpoints](/guide/endpoints/composite) - Composite endpoint guide
- [File Operations](/guide/endpoints/files) - File handling guide