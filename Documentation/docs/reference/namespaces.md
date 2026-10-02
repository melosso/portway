---
title: Namespaces
description: "Folder-based grouping of endpoints under /{namespace}/{endpoint} URL paths"
---

# Namespaces

A namespace groups endpoints (e.g. `CRM`, `Finance`, `Account`). It is the folder between the endpoint type folder and the endpoint folder, and it becomes a URL segment. All endpoint types support namespaces; webhooks require one.

## Directory structure

```
/endpoints/
  ├── SQL/
  │   ├── [Namespace]/
  │   │   └── [EntityName]/
  │   │       └── entity.json
  │   └── [EntityName]/              # without namespace
  │       └── entity.json
  ├── Proxy/
  │   ├── [Namespace]/
  │   │   └── [EntityName]/
  │   │       └── entity.json
  │   └── [EntityName]/
  │       └── entity.json
  ├── Static/
  │   ├── [Namespace]/
  │   │   └── [EntityName]/
  │   │       ├── entity.json
  │   │       └── [content-file]
  │   └── [EntityName]/
  │       ├── entity.json
  │       └── [content-file]
  ├── Files/
  │   ├── [Namespace]/
  │   │   └── [EntityName]/
  │   │       └── entity.json
  │   └── [EntityName]/
  │       └── entity.json
  └── Webhooks/                      # namespace required
      └── [Namespace]/
          └── [EntityName]/
              └── entity.json
```

Composite endpoints are stored under `Proxy/` with `"Type": "Composite"`.

## Namespace resolution

| Source | Example | Namespace |
|---|---|---|
| `Namespace` in `entity.json` | `"Namespace": "CRM"` | `CRM` |
| Folder | `/endpoints/Proxy/Account/Contacts/entity.json` | `Account` |
| Nested folders | `/endpoints/SQL/WMS/Inbound/StagingBins/entity.json` | `WMS/Inbound` |
| No folder | `/endpoints/SQL/Products/entity.json` | none |

The explicit `Namespace` takes precedence over the folder. Nested namespaces route as `/api/{env}/WMS/Inbound/StagingBins`, form a tag with `parent: WMS` in the OpenAPI document, and nest in the `/docs` sidebar. The longest matching path wins: with `WMS/Bins` and `WMS/Inbound/StagingBins` configured, `/api/{env}/WMS/Inbound/StagingBins` resolves the nested endpoint. Example: `WMS/Inbound/StagingBins` in the SQLite demo environment.

## Properties

| Property | Type | Required | Description |
|---|---|---|---|
| `Namespace` | string | No | Overrides the folder namespace |
| `NamespaceDisplayName` | string | No | Namespace title in `/docs`; not part of the route or tag name |
| `DisplayName` | string | No | Endpoint title in `/docs`; not part of the route or tag name |

```json
{
  "Namespace": "Finance",
  "NamespaceDisplayName": "Financial Management System",
  "DisplayName": "General Ledger Entries"
}
```

## Routes

```
GET    /api/{env}/{namespace}/{endpoint}
GET    /api/{env}/{namespace}/{endpoint}/{id}
POST   /api/{env}/{namespace}/{endpoint}
PUT    /api/{env}/{namespace}/{endpoint}/{id}
DELETE /api/{env}/{namespace}/{endpoint}/{id}
```

Examples: `/api/prod/Account/Contacts`, `/api/prod/Finance/Transactions/12345`.

Endpoints without a namespace use `/api/{env}/{endpoint}` and `/api/{env}/{endpoint}/{id}`. File endpoints use `/api/{env}/files/{namespace}/{endpoint}`; returned download URLs include the namespace. Composite endpoints are also reachable at `/api/{env}/composite/{endpoint}`.

## Examples

SQL, `/endpoints/SQL/Company/Employees/entity.json`:

```json
{
  "DatabaseObjectName": "Employees",
  "DatabaseSchema": "hr",
  "PrimaryKey": "EmployeeID",
  "AllowedColumns": ["EmployeeID", "FirstName", "LastName", "Department", "HireDate"],
  "Namespace": "Company",
  "NamespaceDisplayName": "Company Management",
  "DisplayName": "Employee Records",
  "AllowedEnvironments": ["dev", "test", "prod"]
}
```

Proxy, `/endpoints/Proxy/Account/Contacts/entity.json`:

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

Static, `/endpoints/Static/Reports/SalesReport/entity.json`:

```json
{
  "ContentType": "application/json",
  "ContentFile": "sales-data.json",
  "EnableFiltering": true,
  "Namespace": "Reports",
  "NamespaceDisplayName": "Business Reports",
  "DisplayName": "Monthly Sales Report",
  "AllowedEnvironments": ["dev", "test", "prod"]
}
```

File, `/endpoints/Files/Archive/Documents/entity.json`, served at `/api/{env}/files/Archive/Documents`:

```json
{
  "StorageType": "Local",
  "BaseDirectory": "documents",
  "AllowedExtensions": [".pdf", ".docx", ".txt"],
  "Namespace": "Archive",
  "NamespaceDisplayName": "Document Archive",
  "AllowedEnvironments": ["dev", "test", "prod"]
}
```

Composite, `/endpoints/Proxy/Sales/OrderProcessing/entity.json`:

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
      { "Name": "ValidateCustomer", "Endpoint": "Account/Customers", "Method": "GET" },
      { "Name": "CreateOrder", "Endpoint": "Sales/Orders", "Method": "POST" }
    ]
  },
  "AllowedEnvironments": ["test", "prod"]
}
```

## Naming rules

Per segment of a namespace:

| Rule | Valid | Invalid |
|---|---|---|
| Starts with a letter | `Account` | `123Account` |
| Letters, digits and underscores | `Finance_Module` | `Account-Management`, `Account Management` |
| At most 50 characters for the whole namespace | | |

Reserved names: `api`, `docs`, `openapi`, `health`, `admin`, `system`, `composite`, `webhook`, `files`. An endpoint with a reserved or invalid namespace is not loaded, and the console refuses to save it.

## OpenAPI tags

Tag names follow the route: `{namespace}/{endpoint}` for a namespaced endpoint, `{endpoint}` otherwise, and `Files/{endpoint}` for a file endpoint without a namespace. Each tag sets `parent` to the tag one segment up, so `/docs` lists `CRM` with `Accounts` and `Suppliers` beneath it. Labels set the sidebar title (`summary`) and never change a tag name.

| Tag | Title |
|---|---|
| Namespace (`CRM`) | `NamespaceDisplayName`, else the last namespace segment |
| Endpoint (`CRM/Accounts`) | `DisplayName`, else the endpoint folder |

The endpoint's `TagDescription` describes its own tag. Endpoints in one namespace should share one `NamespaceDisplayName`. With conflicting values the title is the ordinal first value, and startup logs a warning naming the namespace and its values.

```json
{
  "tags": [
    { "name": "CRM", "summary": "Customer Relationship Management", "kind": "nav" },
    { "name": "CRM/Accounts", "summary": "Accounts", "parent": "CRM", "kind": "nav", "description": "Account Management" },
    { "name": "CRM/Suppliers", "summary": "Vendors", "parent": "CRM", "kind": "nav" }
  ]
}
```

## Troubleshooting

| Error | Resolution |
|---|---|
| `Namespace segment 'X' must start with a letter and contain only letters, numbers, and underscores` | Rename the segment, e.g. `Account_Mgmt` instead of `Account-Mgmt` |
| `'api' is a reserved namespace name` | Choose another name |
| Endpoint served under an unexpected namespace | An explicit `Namespace` overrides the folder; align both |

## Related topics

- [Entity Configuration](/reference/entity-config)
- [Folders and Routes](/guide/layout)
- [API Overview](/reference/)
