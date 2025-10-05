# Namespaces

Namespace configuration enables logical grouping and organization of related endpoints. Namespaces provide better API structure, clearer documentation, and enhanced endpoint management across all endpoint types.

## Overview

Namespaces allow you to organize endpoints into logical groups, such as:
- **Functional areas**: `CRM`, `Finance`, `Inventory`
- **Business domains**: `Account`, `Masterdata`, `Sales`
- **System boundaries**: `Internal`, `External`, `Public`

## Directory Structure

Namespaces are implemented through directory organization within each endpoint type:

```
/endpoints/
  ├── SQL/
  │   ├── [Namespace]/
  │   │   └── [EntityName]/
  │   │       └── entity.json
  │   └── [EntityName]/              # Non-namespaced (legacy)
  │       └── entity.json
  ├── Proxy/
  │   ├── [Namespace]/
  │   │   └── [EntityName]/
  │   │       └── entity.json
  │   └── [EntityName]/              # Non-namespaced (legacy)
  │       └── entity.json
  ├── Static/
  │   ├── [Namespace]/
  │   │   └── [EntityName]/
  │   │       ├── entity.json
  │   │       └── [content-file]
  │   └── [EntityName]/              # Non-namespaced (legacy)
  │       ├── entity.json
  │       └── [content-file]
  ├── Files/
  │   ├── [Namespace]/
  │   │   └── [EntityName]/
  │   │       └── entity.json
  │   └── [EntityName]/              # Non-namespaced (legacy)
  │       └── entity.json
  └── Composite/
      ├── [Namespace]/
      │   └── [EntityName]/
      │       └── entity.json
      └── [EntityName]/              # Non-namespaced (legacy)
          └── entity.json
```

## Namespace Configuration

### Explicit Namespace Definition

You can explicitly define namespace properties in any `entity.json` file:

```json
{
  "Namespace": "CRM",
  "NamespaceDisplayName": "Customer Relationship Management",
  "DisplayName": "Account Management",
  
  "Url": "http://internal-service/accounts",
  "Methods": ["GET", "POST", "PUT"],
  "AllowedEnvironments": ["dev", "test", "prod"]
}
```

### Inferred Namespace from Directory

If no explicit `Namespace` is specified, the namespace is inferred from the directory structure:

**Directory**: `/endpoints/Proxy/Account/Contacts/entity.json`
- **Inferred Namespace**: `Account`
- **Endpoint Name**: `Contacts`

### Namespace Priority

The effective namespace follows this priority order:
1. **Explicit `Namespace`** property in `entity.json`
2. **Inferred namespace** from directory structure
3. **No namespace** (legacy behavior)

## Property Reference

### Core Namespace Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Namespace` | string | No | Explicit namespace override |
| `NamespaceDisplayName` | string | No | Human-readable namespace name for documentation |
| `DisplayName` | string | No | Human-readable endpoint name |

### Namespace Properties Examples

```json
{
  "Namespace": "Finance",
  "NamespaceDisplayName": "Financial Management System",
  "DisplayName": "General Ledger Entries"
}
```

## API Routing Patterns

### Namespaced Endpoints

Endpoints with namespaces are accessible via extended URL patterns:

```
GET /api/{env}/{namespace}/{endpoint}
GET /api/{env}/{namespace}/{endpoint}/{id}
POST /api/{env}/{namespace}/{endpoint}
PUT /api/{env}/{namespace}/{endpoint}/{id}
DELETE /api/{env}/{namespace}/{endpoint}/{id}
```

**Examples**:
- `/api/prod/Account/Contacts` - Get all contacts in Account namespace
- `/api/prod/Finance/Transactions/12345` - Get specific transaction
- `/api/dev/CRM/Customers` - Get customers in development environment

### Backward Compatibility

Non-namespaced endpoints continue to work with legacy URL patterns:

```
GET /api/{env}/{endpoint}
GET /api/{env}/{endpoint}/{id}
```

**Example**: `/api/prod/Accounts` (legacy non-namespaced)

### Fallback Behavior

The system attempts namespaced access first, then falls back to non-namespaced:

1. Try: `/api/prod/CRM/Accounts` → `CRM/Accounts`
2. Fallback: `/api/prod/Accounts` → `Accounts`

## Configuration Examples

### SQL Endpoint with Namespace

**File**: `/endpoints/SQL/Company/Employees/entity.json`

```json
{
  "DatabaseObjectName": "Employees",
  "DatabaseSchema": "hr",
  "PrimaryKey": "EmployeeID",
  "AllowedColumns": [
    "EmployeeID",
    "FirstName", 
    "LastName",
    "Department",
    "HireDate"
  ],
  "Namespace": "Company",
  "NamespaceDisplayName": "Company Management",
  "DisplayName": "Employee Records",
  "AllowedEnvironments": ["dev", "test", "prod"]
}
```

### Proxy Endpoint with Namespace

**File**: `/endpoints/Proxy/Account/Contacts/entity.json`

```json
{
  "Url": "http://crm-service:8080/api/contacts",
  "Methods": ["GET", "POST", "PUT", "DELETE"],
  "Namespace": "Account",
  "NamespaceDisplayName": "Account Management",
  "DisplayName": "Contact Management",
  "AllowedEnvironments": ["dev", "test", "prod"],
  "Documentation": {
    "TagDescription": "**Contact Management**\n\nManage customer and vendor contact information.",
    "MethodDescriptions": {
      "GET": "Retrieve contact records",
      "POST": "Create new contact",
      "PUT": "Update existing contact",
      "DELETE": "Remove contact"
    }
  }
}
```

### Static Endpoint with Namespace

**File**: `/endpoints/Static/Reports/SalesReport/entity.json`

```json
{
  "ContentType": "application/json",
  "ContentFile": "sales-data.json",
  "EnableFiltering": true,
  "Namespace": "Reports",
  "NamespaceDisplayName": "Business Reports",
  "DisplayName": "Monthly Sales Report",
  "AllowedEnvironments": ["dev", "test", "prod"],
  "Documentation": {
    "TagDescription": "**Business Reports**\n\nAccess standardized business reporting data.",
    "MethodDescriptions": {
      "GET": "Download sales report data"
    }
  }
}
```

### File Endpoint with Namespace

**File**: `/endpoints/Files/Documents/Contracts/entity.json`

```json
{
  "StorageDirectory": "contracts",
  "AllowedExtensions": [".pdf", ".docx", ".txt"],
  "MaxFileSizeBytes": 10485760,
  "IsPrivate": false,
  "Namespace": "Documents",
  "NamespaceDisplayName": "Document Management",
  "DisplayName": "Contract Files",
  "AllowedEnvironments": ["dev", "test", "prod"]
}
```

### Composite Endpoint with Namespace

**File**: `/endpoints/Composite/Sales/OrderProcessing/entity.json`

```json
{
  "Url": "http://order-service:8080",
  "Methods": ["POST"],
  "Type": "Composite",
  "Namespace": "Sales",
  "NamespaceDisplayName": "Sales Operations",
  "DisplayName": "Order Processing Workflow",
  "CompositeConfig": {
    "Name": "OrderProcessing",
    "Description": "Complete order processing workflow",
    "Steps": [
      {
        "Name": "ValidateCustomer",
        "Endpoint": "Account/Customers",
        "Method": "GET"
      },
      {
        "Name": "CreateOrder",
        "Endpoint": "Sales/Orders",
        "Method": "POST"
      }
    ]
  },
  "AllowedEnvironments": ["test", "prod"]
}
```

## Naming Conventions

### Namespace Naming Rules

Namespaces must follow these naming conventions:

- **Start with a letter** (A-Z, a-z)
- **Contain only** letters, numbers, and underscores
- **Maximum length** of 50 characters
- **Case-sensitive** (but URLs are case-insensitive)

**Valid Examples**:
- `Account`
- `CRM`
- `Finance_Module`
- `External_APIs`

**Invalid Examples**:
- `123Account` (starts with number)
- `Account-Management` (contains hyphen)
- `Account Management` (contains space)

### Reserved Namespace Names

The following namespace names are reserved and cannot be used:

- `api`
- `docs`
- `swagger`
- `health`
- `admin`
- `system`
- `composite`
- `webhook`
- `files`

## OpenAPI Documentation

### Swagger Tag Organization

Namespaces automatically organize endpoints in the Swagger UI using tags:

- **With NamespaceDisplayName**: `"Customer Relationship Management"`
- **With Namespace only**: `"CRM"`
- **Inferred**: Uses directory name as tag

### Documentation Grouping

In the generated OpenAPI specification:

```json
{
  "tags": [
    {
      "name": "Account",
      "description": "Account Management - Contact and customer operations"
    },
    {
      "name": "Finance", 
      "description": "Financial Management System"
    }
  ]
}
```

## Best Practices

### 1. Consistent Naming

Use consistent namespace naming across your organization:

```
Account/     # Customer/vendor management
Finance/     # Financial operations  
Product/     # Product catalog
Order/       # Order management
Report/      # Business reports
```

### 2. Logical Grouping

Group related endpoints together:

```
/endpoints/Proxy/CRM/
  ├── Accounts/
  ├── Contacts/
  ├── Opportunities/
  └── Activities/
```

### 3. Clear Display Names

Provide descriptive display names for documentation:

```json
{
  "Namespace": "CRM",
  "NamespaceDisplayName": "Customer Relationship Management",
  "DisplayName": "Customer Account Management"
}
```

### 4. Environment Consistency

Ensure namespace structure is consistent across environments:

```
dev/CRM/Accounts    ← Matches
test/CRM/Accounts   ← Matches  
prod/CRM/Accounts   ← Matches
```

## Migration from Non-Namespaced

### Gradual Migration

1. **Keep existing endpoints** in root directories
2. **Create namespaced versions** in subdirectories
3. **Update clients gradually** to use namespaced URLs
4. **Remove legacy endpoints** when migration is complete

### Backward Compatibility

During migration, both URL patterns work:

```
/api/prod/Accounts        # Legacy (still works)
/api/prod/CRM/Accounts    # New namespaced version
```

## Troubleshooting

### Common Issues

#### 1. Namespace Validation Errors

**Error**: `Namespace must start with a letter and contain only letters, numbers, and underscores`

**Solution**: Check namespace naming follows conventions:
```json
{
  "Namespace": "Account_Mgmt"  // ✓ Valid
  // "Namespace": "Account-Mgmt"  // ✗ Invalid (hyphen)
}
```

#### 2. Reserved Namespace Names

**Error**: `'api' is a reserved namespace name`

**Solution**: Choose a different namespace name:
```json
{
  "Namespace": "ApiProxy"  // ✓ Valid alternative
  // "Namespace": "api"     // ✗ Reserved
}
```

#### 3. Conflicting Directory Structure

**Issue**: Inferred namespace doesn't match explicit namespace

**Solution**: Ensure directory structure aligns with explicit namespace:
```
# Directory: /endpoints/Proxy/Account/Contacts/
{
  "Namespace": "Account"  // ✓ Matches directory
  // "Namespace": "CRM"   // ⚠ Conflicts with directory
}
```

#### 4. Missing Endpoints in Swagger

**Issue**: Namespaced endpoints not appearing in documentation

**Solution**: Check that `NamespaceDisplayName` is set for proper grouping:
```json
{
  "Namespace": "Account",
  "NamespaceDisplayName": "Account Management"  // Required for Swagger tags
}
```

### Validation Checklist

- [ ] Namespace name follows naming conventions
- [ ] Namespace is not a reserved word
- [ ] Directory structure matches namespace organization
- [ ] `NamespaceDisplayName` provided for documentation
- [ ] URLs accessible with namespaced patterns
- [ ] Backward compatibility maintained for existing clients
- [ ] Environment consistency across dev/test/prod

## Server Configuration

### Directory Creation

The server automatically creates namespace directories when needed. You can also create them manually using the helper methods.

### Namespace Validation

Namespace validation occurs at:
- **Startup**: During endpoint loading
- **Runtime**: When processing requests
- **Configuration**: When validating entity definitions

## Related Topics

- [Entity Configuration](/reference/entity-config) - Complete endpoint configuration reference
- [Environment Settings](/reference/environment-settings) - Environment configuration
- [API Overview](/reference/api/overview) - API endpoint patterns
- [SQL Endpoints](/guide/endpoints/sql) - SQL endpoint guide
- [Proxy Endpoints](/guide/endpoints/proxy) - Proxy endpoint guide
- [Composite Endpoints](/guide/endpoints/composite) - Composite endpoint guide