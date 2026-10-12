---
title: Namespaces
description: "Folder-based grouping of endpoints under /{namespace}/{endpoint} URL paths"
---

# Namespaces

A namespace groups endpoints (e.g. `CRM`, `Finance`, `Account`). It is the folder between the endpoint type folder and the endpoint folder, and it becomes a URL segment. All endpoint types support namespaces; webhooks require one.

## Directory structure

Folder layout per endpoint type: [Entity configuration](/reference/entity-config). Composite endpoints are stored under `Proxy/` with `"Type": "Composite"`.

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
| `NamespaceDescription` | string | No | Namespace description in `/docs` (Markdown) |
| `DisplayName` | string | No | Endpoint title in `/docs`; not part of the route or tag name |

```json
{
  "Namespace": "Finance",
  "NamespaceDisplayName": "Financial Management System",
  "NamespaceDescription": "Ledgers, invoices and payments.",
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

## Versions

A `v{n}` folder (`v1` to `v999`) below an endpoint folder holds that version of the endpoint. Without a `v1` folder, the endpoint folder's own `entity.json` is `v1`.

```
endpoints/SQL/Inventory/Products/
├── entity.json        → /api/{env}/Inventory/Products  (also /api/{env}/v1/Inventory/Products)
└── v2/
    └── entity.json    → /api/{env}/v2/Inventory/Products
```

| Rule | Behavior |
|---|---|
| Configuration | Each version is a complete endpoint: columns, methods, environments, `Mcp`, `Enabled` and `Deprecated` are set per version, nothing is inherited |
| Route | The version segment follows the environment; file endpoints use `/api/{env}/v2/files/{endpoint}` |
| Unknown version | `404`, the same response as a missing endpoint |
| `v1` folder | Optional. `v1/entity.json` defines v1 and takes precedence over the `entity.json` beside it, which is skipped with a warning |
| Endpoint types | SQL, Proxy, Composite, Static, Webhook and Files |
| Token scope | `Inventory/Products` grants v1, `Inventory/Products@v2` grants v2, `Inventory/Products*` grants every version ([Endpoint scopes](/guide/tokens#endpoint-scopes)) |
| Composite steps | A step names a version with `@v2`, e.g. `"Endpoint": "Sales/OrderLine@v2"` |
| MCP | Each exposed version registers its own tool, with the version in the tool name |
| Namespace named `v2` | Resolves as before; a versioned endpoint with the same path takes precedence, logged at load |

The OpenAPI document lists every version under the endpoint's tag, with the version appended to the operation summary and `operationId`. `/docs/openapi/v{n}/openapi.json` holds one version; `/docs` offers a version switch when more than one version exists.

### Retiring a version

| Property | Response header |
|---|---|
| `Deprecated: true` with `DeprecatedSince` | `Deprecation: @{unix time}` ([RFC 9745](https://www.rfc-editor.org/rfc/rfc9745)) |
| `Sunset` | `Sunset: {HTTP date}` ([RFC 8594](https://www.rfc-editor.org/rfc/rfc8594)) |
| `Deprecated: true` and a higher version exists | `Link: </api/{env}/v2/...>; rel="successor-version"` |

```json
{
  "Deprecated": true,
  "DeprecatedSince": "2026-10-01T00:00:00Z",
  "Sunset": "2027-04-01T00:00:00Z"
}
```

The console's **New version** action copies `entity.json` and its sibling files into the next `v{n}` folder. A base endpoint with versions is deleted after its versions; a version is renamed through its base endpoint.

## Example

`/endpoints/SQL/Company/Employees/entity.json`:

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

A composite step names a namespaced endpoint by its path, e.g. `"Endpoint": "Account/Customers"`.

## Naming rules

Per segment of a namespace:

| Rule | Valid | Invalid |
|---|---|---|
| Starts with a letter | `Account` | `123Account` |
| Letters, digits and underscores | `Finance_Module` | `Account-Management`, `Account Management` |
| At most 50 characters for the whole namespace | | |

Reserved names: `api`, `docs`, `openapi`, `health`, `admin`, `system`, `composite`, `webhook`, `files`. An endpoint with a reserved or invalid namespace is not loaded, and the console refuses to save it.

## OpenAPI tags

Endpoint tags are named by the route below `/api/{env}`: `{namespace}/{endpoint}` for a namespaced endpoint, `{endpoint}` without a namespace, `files/{endpoint}` for a file endpoint. Group tags are named `ns:{namespace}` and have `kind: nav`. Endpoint tags have no `kind`. Each endpoint tag sets `parent` to its group, and a nested group sets `parent` to the group one segment up. The `ns:` prefix separates a group from an endpoint of the same name. Labels set the sidebar title (`summary`) and never change a tag name.

An endpoint without a namespace is placed in the `ns:{DefaultGroup}` group (`OpenApi:DefaultGroup`, default `General`), a file endpoint without a namespace in `ns:Files`. With an empty `DefaultGroup` an endpoint without a namespace has no `parent`. With `OpenApi:ShowNamespaces` set to `false` no group tags are declared and no tag has a `parent`.

| Tag | Title | Description |
|---|---|---|
| Group (`ns:CRM`) | `NamespaceDisplayName`, else the last namespace segment | `NamespaceDescription` |
| Endpoint (`CRM/Accounts`) | `DisplayName`, else the endpoint folder | `Documentation.TagDescription` |

Endpoints in one namespace should share one `NamespaceDisplayName` and one `NamespaceDescription`. Set them on every endpoint in the namespace or on one. With conflicting values the ordinal first value is used, and startup logs a warning naming the namespace, the setting and its values.

Within each group, endpoints are listed before subgroups, each ordered by title. A namespace named like `DefaultGroup` shares its group.

```json
{
  "tags": [
    { "name": "ns:CRM", "summary": "Customer Relationship Management", "kind": "nav", "description": "Customers, contacts and suppliers." },
    { "name": "CRM/Accounts", "summary": "Accounts", "parent": "ns:CRM", "description": "Account Management" },
    { "name": "CRM/Suppliers", "summary": "Vendors", "parent": "ns:CRM" }
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
