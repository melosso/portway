---
title: OpenAPI Documentation Settings
description: "OpenAPI 3.2 document generation and the Scalar reference at /docs"
---

# OpenAPI Documentation Settings

The OpenAPI 3.2 document and the Scalar reference at `/docs` are generated from the loaded endpoint files. SQL endpoints add column names and types read from the database at startup; other endpoint types use the `Documentation` block in `entity.json`.

## Generated content

| Feature | Representation |
|---|---|
| `QUERY` | Native `query` operation |
| `MERGE` | Under `additionalOperations` |
| Namespaces | Tag groups; each endpoint is a tag under its namespace |
| `Deprecated: true` | Operations marked deprecated |
| `Enabled: false` | Operations marked deprecated with a `[Disabled]` summary prefix |
| File uploads | `multipart/form-data` with part media types from `AllowedExtensions` (otherwise `application/octet-stream`) |
| Errors | `ErrorResponse` and `ValidationErrorResponse` (`422`) components |
| Tenant headers | Optional header parameters per `Tenancy` header; tenant columns `readOnly` in request bodies |
| Table-valued function parameters | Query and header parameters from `FunctionParameters` |
| `Hidden: true` | Endpoint omitted |
| Stored procedure names | Never included |

## Global configuration

```json
{
  "OpenApi": {
    "Enabled": true,
    "Title": "Portway: API Gateway",
    "Version": "v1",
    "Description": "This is Portway. A lightweight API gateway that connects your platforms to your data sources and services, with a simple and fast setup.",
    "Contact": {
      "Name": "Your Name",
      "Email": "support@yourcompany.com"
    },
    "Footer": {
      "Text": "Powered by Portway",
      "Target": "_blank",
      "Url": "#"
    },
    "SecurityDefinition": {
      "Name": "Bearer",
      "Description": "Bearer token issued by Portway. Send it as: Authorization: Bearer {token}",
      "In": "Header",
      "Type": "Http",
      "Scheme": "Bearer"
    },
    "ScalarTheme": "default",
    "ScalarLayout": "modern",
    "ScalarShowSidebar": true,
    "ScalarHideDownloadButton": false,
    "ScalarHideModels": true,
    "ScalarHideClientButton": true,
    "ScalarHideTestRequestButton": false
  }
}
```

| Property | Type | Description |
|---|---|---|
| `Enabled` | boolean | Generates the document and `/docs` |
| `Title` | string | Document title |
| `Version` | string | Document version |
| `Description` | string | Document description (Markdown) |
| `Contact.Name` | string | Contact name |
| `Contact.Email` | string | Contact e-mail |
| `Footer.Text` | string | Footer text |
| `Footer.Target` | string | Footer link target (`_blank`, `_self`) |
| `Footer.Url` | string | Footer link |
| `SecurityDefinition.Name` | string | Security scheme name |
| `SecurityDefinition.Description` | string | Security scheme description |
| `SecurityDefinition.In` | string | Key location for `ApiKey` (`Header`, `Query`, `Cookie`) |
| `SecurityDefinition.Type` | string | `ApiKey`, `Http`, `OAuth2`, `OpenIdConnect` |
| `SecurityDefinition.Scheme` | string | HTTP scheme, e.g. `Bearer` |
| `ForceHttpsInProduction` | boolean | HTTPS server URLs in production (default `true`) |
| `ShowNamespaces` | boolean | Groups endpoints by namespace in the sidebar (default `true`); `false` lists every endpoint flat |
| `DefaultGroup` | string | Sidebar group for endpoints without a namespace (default `General`); empty lists them ungrouped |
| `ScalarTheme` | string | Scalar theme |
| `ScalarLayout` | string | `modern` or `classic` |
| `ScalarShowSidebar` | boolean | Sidebar |
| `ScalarHideDownloadButton` | boolean | Hides the document download |
| `ScalarHideModels` | boolean | Hides the schema section |
| `ScalarHideClientButton` | boolean | Hides client generation |
| `ScalarHideTestRequestButton` | boolean | Hides test requests |

The console edits these properties under **Settings → Integrations** (OpenAPI, API Reference and Reference Footer cards), except `SecurityDefinition.Name`, `Type`, `In` and `Scheme`: Portway accepts tokens only in the `Authorization` header. `Enabled`, `Version` and `SecurityDefinition.Description` apply after a restart; the others apply on save. `Version` and `DefaultGroup` accept letters, digits, `.`, `-` and `_`. `Footer.Url` accepts `http`, `https` and `mailto` links or `#`.

## Endpoint documentation

```json
{
  "DatabaseObjectName": "Products",
  "AllowedColumns": ["ItemCode", "Description", "Price"],
  "Documentation": {
    "TagDescription": "**Product Catalog**\n\nAccess the product catalog with detailed item information.",
    "MethodDescriptions": {
      "GET": "Query product catalog with filtering and pagination",
      "POST": "Add new products to the catalog"
    },
    "Examples": {
      "GET": {
        "count": 1,
        "value": [
          { "ItemCode": "ITEM-001", "Description": "Widget", "Price": 9.99 }
        ]
      }
    }
  }
}
```

| Property | Type | Required | Description |
|---|---|---|---|
| `TagDescription` | string | No | Tag description (Markdown) |
| `MethodDescriptions` | object | No | Short summary per method |
| `MethodDocumentation` | object | No | Long description per method (Markdown) |
| `Examples` | object | No | Success response example per method; replaces generated sample data |

Descriptions support GitHub-flavoured Markdown, `<br>` and `<p>`, and Scalar alerts (`> [!tip]`, [Scalar markdown](https://guides.scalar.com/scalar/scalar-api-references/markdown#alerts)). Method keys match the endpoint's methods exactly.

## Deprecated and disabled endpoints

With `Deprecated: true`, the endpoint's operations are marked deprecated; requests are served unchanged.

```json
{
  "DatabaseObjectName": "LegacyOrders",
  "AllowedMethods": ["GET"],
  "Deprecated": true
}
```

With `Enabled: false`, requests return `503` with `Retry-After`, the operations stay in the document as deprecated with a `[Disabled]` prefix, and the endpoint is removed from MCP. The change applies at the next configuration reload. Example: `Static/Production/Machines`.

```json
{
  "success": false,
  "error": "This endpoint is temporarily disabled for scheduled maintenance."
}
```

## Nested tags

Endpoint tags are named by their route path (`WMS/Inbound/StagingBins`) with `parent` set to their group. Group tags are named `ns:{namespace}` (`ns:WMS/Inbound`), carry `kind: nav` and set `parent` to the group one segment up; missing groups are generated. `summary` holds the `DisplayName` or `NamespaceDisplayName`, else the last segment. The `/docs` sidebar nests tags by `parent` and labels them with `summary`. See [Namespaces](/reference/namespaces#openapi-tags).

## Schema discovery

SQL column metadata is read at startup from the first environment in the endpoint's `AllowedEnvironments`.

:::warning
With Windows Authentication (`Trusted_Connection=True`), the IIS Application Pool identity needs access to every environment database used for discovery.
:::

## Troubleshooting

| Symptom | Check |
|---|---|
| Endpoint missing from the document | Valid `entity.json`, not `Hidden`, environment allowed |
| Markdown not rendered | `\n` for line breaks inside JSON strings; closed formatting |
| Method description missing | Method key matches a configured method exactly |

## Related topics

- [Entity Configuration](/reference/entity-config)
- [API Overview](/reference/)
- [Namespaces](/reference/namespaces)
