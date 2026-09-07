---
title: Namespaces
description: "Directory-based grouping for SQL, Proxy, Static, and Composite endpoints that exposes them under /{namespace}/{endpoint} URL paths"
---

# Namespaces

Namespaces let you organise related endpoints into logical groups, for example, `CRM`, `Finance`, or `Account`, using the directory structure under each endpoint type folder. The folder name becomes the namespace segment in the request URL.

Every endpoint type supports namespaces. Webhooks are the one type that requires one, since each webhook lives at `endpoints/Webhooks/{Namespace}/{Name}/entity.json`.

## Directory structure

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
  └── Webhooks/                      # namespace is required here
      └── [Namespace]/
          └── [EntityName]/
              └── entity.json
```

Composite endpoints have no folder of their own. They live under `Proxy/` with `"Type": "Composite"` in their `entity.json`.

## Namespace configuration

### Explicit namespace definition

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

### Inferred namespace from directory

If no explicit `Namespace` is specified, the namespace is inferred from the directory structure:

**Directory**: `/endpoints/Proxy/Account/Contacts/entity.json`
- **Inferred Namespace**: `Account`
- **Endpoint Name**: `Contacts`

### Nested namespaces

Directories may nest more than one level. Every folder above the endpoint becomes part of the namespace:

**Directory**: `/endpoints/SQL/WMS/Inbound/StagingBins/entity.json`
- **Inferred Namespace**: `WMS/Inbound`
- **Endpoint Name**: `StagingBins`
- **Request path**: `/api/{env}/WMS/Inbound/StagingBins`

Each segment is validated on its own, so the naming rules below apply per segment rather than to the joined namespace. A working example ships as `WMS/Inbound/StagingBins` in the SQLite demo environment.

In the OpenAPI document the nested namespace becomes a tag that names `WMS` as its parent. Scalar does not act on that relationship yet, so the `/docs` sidebar currently lists `WMS` and `WMS/Inbound` side by side. Routing, grouping and the document itself are unaffected.

Longer paths win when they match: with both `WMS/Bins` and `WMS/Inbound/StagingBins` configured, a request to `/api/{env}/WMS/Inbound/StagingBins` resolves the nested endpoint rather than treating `Inbound` as a record id.

### Namespace priority

The effective namespace follows this priority order:
1. **Explicit `Namespace`** property in `entity.json`
2. **Inferred namespace** from directory structure
3. **No namespace** (legacy behavior)

## Property reference

### Core namespace properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Namespace` | string | No | Explicit namespace override |
| `NamespaceDisplayName` | string | No | Human-readable namespace name for documentation |
| `DisplayName` | string | No | Human-readable endpoint name |

### Namespace properties examples

```json
{
  "Namespace": "Finance",
  "NamespaceDisplayName": "Financial Management System",
  "DisplayName": "General Ledger Entries"
}
```

## API routing patterns

### Namespaced endpoints

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

### Backward compatibility

Non-namespaced endpoints continue to work with legacy URL patterns:

```
GET /api/{env}/{endpoint}
GET /api/{env}/{endpoint}/{id}
```

**Example**: `/api/prod/Accounts` (legacy non-namespaced)

### Fallback behavior

The system attempts namespaced access first, then falls back to non-namespaced:

1. Try: `/api/prod/CRM/Accounts` → `CRM/Accounts`
2. Fallback: `/api/prod/Accounts` → `Accounts`

## Configuration examples

### SQL endpoint with namespace

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

### Proxy endpoint with namespace

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

### Static endpoint with namespace

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

### File endpoint with namespace

**File**: `/endpoints/Files/Archive/Documents/entity.json`

```json
{
  "StorageDirectory": "documents",
  "AllowedExtensions": [".pdf", ".docx", ".txt"],
  "MaxFileSizeBytes": 10485760,
  "Hidden": false,
  "Namespace": "Archive",
  "NamespaceDisplayName": "Document Archive",
  "AllowedEnvironments": ["dev", "test", "prod"]
}
```

::: tip
The namespace appears in every file route, so this endpoint is served at `/api/{env}/files/Archive/Documents`. Download URLs returned by the API include it as well, which keeps them usable as-is.
:::

### Composite endpoint with namespace

**File**: `/endpoints/Proxy/Sales/OrderProcessing/entity.json`

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

::: tip
Composite endpoints are stored in the `/endpoints/Proxy/` directory with `"Type": "Composite"`. They support both namespaced access (`/api/{env}/{namespace}/{endpoint}`) and legacy access (`/api/{env}/composite/{endpoint}`).
:::

## Naming conventions

### Namespace naming rules

Namespace names follow these conventions, applied to each segment of a nested namespace:

- **Start with a letter** (A-Z, a-z)
- **Contain only** letters, numbers, and underscores, with `/` separating nested segments
- **Maximum length** of 50 characters across the whole namespace
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

### Reserved namespaces

The following namespace names are reserved and cannot be used:

- `api`
- `docs`
- `openapi`
- `health`
- `admin`
- `system`
- `composite`
- `webhook`
- `files`

An endpoint that claims one of these is skipped when the loader reads it at startup; the Web UI validator turns it down before the file is written.

## OpenAPI documentation

### Documentation tag organization

Namespaces automatically organize endpoints in the documentation UI using tags:

- **With NamespaceDisplayName**: `"Customer Relationship Management"`
- **With Namespace only**: `"CRM"`
- **Inferred**: Uses directory name as tag

### Documentation grouping

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

## Migration from non-Namespaced

### Gradual migration

1. **Keep existing endpoints** in root directories
2. **Create namespaced versions** in subdirectories
3. **Update clients gradually** to use namespaced URLs
4. **Remove legacy endpoints** when migration is complete

### Backward compatibility

During migration, both URL patterns work:

```
/api/prod/Accounts        # Legacy (still works)
/api/prod/CRM/Accounts    # New namespaced version
```

## Troubleshooting

### Common issues

#### 1. Namespace validation errors

**Error**: `Namespace segment 'X' must start with a letter and contain only letters, numbers, and underscores`

**Solution**: Check namespace naming follows conventions:
```json
{
  "Namespace": "Account_Mgmt"  // Valid
  // "Namespace": "Account-Mgmt"  // Invalid (hyphen)
}
```

#### 2. Reserved namespace names

**Error**: `'api' is a reserved namespace name`

**Solution**: Choose a different namespace name:
```json
{
  "Namespace": "ApiProxy"  // Valid alternative
  // "Namespace": "api"     // Reserved
}
```

#### 3. Conflicting directory structure

**Issue**: Inferred namespace doesn't match explicit namespace

**Solution**: Ensure directory structure aligns with explicit namespace:
```
# Directory: /endpoints/Proxy/Account/Contacts/
{
  "Namespace": "Account"  // Matches directory
  // "Namespace": "CRM"   // Conflicts with directory
}
```

#### 4. Missing endpoints in documentation

**Issue**: Namespaced endpoints not appearing in documentation

**Solution**: Check that `NamespaceDisplayName` is set for proper grouping:
```json
{
  "Namespace": "Account",
  "NamespaceDisplayName": "Account Management"  // Required for documentation tags
}
```

## Related topics

- [Entity Configuration](/reference/entity-config)
- [Environment Settings](/reference/environment-settings)
- [API Overview](/reference/)
- [SQL Endpoints](/guide/endpoints-sql)
- [Proxy Endpoints](/guide/endpoints-proxy)
- [Composite Endpoints](/guide/endpoints-composite)
