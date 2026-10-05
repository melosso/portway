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
| Namespaces | Tag groups; each endpoint is a tag under its namespace ([OpenAPI tags](/reference/namespaces#openapi-tags)) |
| `Deprecated: true` | Operations marked deprecated; `2xx` responses declare the `Deprecation`, `Sunset` and `Link` headers the endpoint sends |
| `Enabled: false` | Operations marked deprecated with a `[Disabled]` summary prefix |
| File uploads | `multipart/form-data` with part media types from `AllowedExtensions` (otherwise `application/octet-stream`) |
| Errors | One shared response per status code under `components/responses` (`BadRequest`, `Unauthorized`, ...), each with the `ErrorResponse` schema (`ValidationErrorResponse` for `422`); `Unauthorized` declares `WWW-Authenticate` |
| Security | Bearer requirement declared once on the document root |
| `operationId` | `{method}_{namespace}_{endpoint}` (`get_WMS_Bins`, `merge_WMS_Bins`); `_byId` for `GET {endpoint}({id})`; composites `composite_...`, files `uploadFile_...`, `listFiles_...`, `downloadFile_...`, `deleteFile_...`. Derived from the route. Adding an endpoint changes no other id |
| SQL key lookup | `GET /api/{env}/{endpoint}({id})` for tables and views that allow `GET` |
| Tenant headers | Optional header parameters per `Tenancy` header; tenant columns `readOnly` in request bodies |
| Table-valued function parameters | Query and header parameters from `FunctionParameters` |
| `Hidden: true` | Endpoint omitted |
| `Mcp.Exposed: true` | `MCP` badge (`x-badges`) on the endpoint's operations |
| OData parameters | `OData` badge on operations that accept `$filter` |
| `ExternalDocs` | Document-level `externalDocs` |
| Endpoint versions | `/api/{env}/v{n}/...` paths under the endpoint's tag; `(v{n})` summary and `operationId` suffix ([Versions](/reference/namespaces#versions)) |
| Stored procedure names | Never included |

## Global configuration

```json
{
  "OpenApi": {
    "Enabled": true,
    "Title": "Portway: API Gateway",
    "Version": "v1",
    "Description": "Integration API for ERP and warehouse data.",
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
    "ShowBadges": true,
    "ExternalDocs": {
      "Url": "https://example.com/api-guide",
      "Description": "Integration guide"
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
| `Version` | string | Document version label (`info.version`); not part of any URL and unrelated to [endpoint versions](/reference/namespaces#versions) |
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
| `ShowBadges` | boolean | `MCP` and `OData` badges on operations (default `true`) |
| `MarkdownEnabled` | boolean | Serves the reference as Markdown at `/docs/openapi.md` (default `false`, `PORTWAY_OPENAPI_MARKDOWN`) |
| `ExternalDocs.Url` | string | Link to an external API guide, shown with the document description; empty omits it |
| `ExternalDocs.Description` | string | Label for that link |
| `ScalarTheme` | string | Scalar theme; `portway` applies the console colours |
| `ScalarLayout` | string | `modern` or `classic` |
| `ScalarShowSidebar` | boolean | Sidebar |
| `ScalarHideDownloadButton` | boolean | Hides the document download |
| `ScalarHideModels` | boolean | Hides the schema section |
| `ScalarHideClientButton` | boolean | Hides client generation |
| `ScalarHideTestRequestButton` | boolean | Hides test requests |

The console edits these properties under **Settings → OpenAPI** (`/ui/settings#openapi`: OpenAPI, API Reference and Reference Footer cards), except `SecurityDefinition.Name`, `Type`, `In` and `Scheme`: Portway accepts tokens only in the `Authorization` header. `Enabled` and `SecurityDefinition.Description` apply after a restart; the others apply on save. `Version` and `DefaultGroup` accept letters, digits, `.`, `-` and `_`. `Footer.Url` accepts `http`, `https` and `mailto` links or `#`. `ExternalDocs.Url` accepts `http` and `https` links or empty.

The document is served at `/docs/openapi.json`; `/docs/openapi/v{n}/openapi.json` holds the operations of one endpoint version. Other names under `/docs/openapi/{name}/openapi.json` redirect to `/docs/openapi.json`.

Every operation has a shareable URL under `/docs` (for example `/docs/tag/...`). Code samples cover curl (default), C#, JavaScript, Python and PowerShell.

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

With `Deprecated: true`, the endpoint's operations are marked deprecated and requests are served. `DeprecatedSince` and `Sunset` add response headers ([Retiring a version](/reference/namespaces#retiring-a-version)).

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
