---
title: OpenAPI Documentation Settings
description: "Configuration reference for OpenAPI schema generation and the Scalar documentation UI"
---

# OpenAPI Documentation Settings

Your endpoint definitions do double duty: besides routing requests, they feed the OpenAPI documentation that Scalar serves at `/docs`. SQL endpoints even get schema discovery for free, with column names and types read from the database at startup. Other endpoint types describe themselves through the `Documentation` block in `entity.json`. This page covers the settings you can adjust.

Portway builds on **OpenAPI 3.2**, which gives the reference room to describe things earlier versions of the format could not:

- QUERY endpoints appear as native `query` operations
- Namespaces become the tag structure
- `Deprecated` endpoints are shown as such
- File uploads describe their multipart encoding
- Every error points at one shared schema

All of it follows from your endpoint definitions, so there is usually nothing extra to configure.

## Global OpenAPI Configuration

Configure the title, contact details, and Scalar UI behaviour in `appsettings.json`:

```json
{
  "OpenApi": {
    "Enabled": true,
    "BaseProtocol": "https",
    "Title": "Portway: API Gateway",
    "Version": "v1",
    "Description": "This is Portway. A lightweight API gateway that connects your platforms to your data sources and services, with a simple and fast setup.",
    "Contact": {
      "Name": "Your Name",
      "Email": "support@yourcompany.com"
    },
    "Footer": {
      "Text": "Powered by Scalar",
      "Target": "_blank",
      "Url": "#"
    },
    "SecurityDefinition": {
      "Name": "Bearer",
      "Description": "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
      "In": "Header",
      "Type": "ApiKey",
      "Scheme": "Bearer"
    },
    "EnableFilter": false,
    "EnableValidator": true,
    "ScalarTheme": "default",
    "ScalarShowSidebar": true,
    "ScalarHideDownloadButton": true,
    "ScalarHideModels": true
  }
}
```

### Configuration Properties
| Property | Type | Description |
|----------|------|-------------|
| `Enabled` | boolean | Enable/disable API documentation generation |
| `BaseProtocol` | string | Protocol for API base URLs (http/https) |
| `Title` | string | Main title shown in documentation header |
| `Version` | string | API version displayed in documentation |
| `Description` | string | Main API description (supports markdown formatting) |
| `Contact.Name` | string | Contact person or team name |
| `Contact.Email` | string | Support email address |
| `Footer.Text` | string | Text displayed in the documentation footer |
| `Footer.Target` | string | Link target behavior (`_blank` for new tab, `_self` for same tab) |
| `Footer.Url` | string | URL for the footer link |
| `SecurityDefinition.Name` | string | Name of the security scheme (e.g., "Bearer") |
| `SecurityDefinition.Description` | string | Description of the authentication method |
| `SecurityDefinition.In` | string | Location of the API key (`Header`, `Query`, `Cookie`) |
| `SecurityDefinition.Type` | string | Type of security scheme (`ApiKey`, `Http`, `OAuth2`, `OpenIdConnect`) |
| `SecurityDefinition.Scheme` | string | Authentication scheme (e.g., "Bearer", "Basic") |
| `ForceHttpsInProduction` | boolean | Force HTTPS URLs in production environments |
| `ScalarTheme` | string | Scalar UI color theme |
| `ScalarLayout` | string | Scalar UI layout style (`modern`, `classic`) |
| `ScalarShowSidebar` | boolean | Show/hide the navigation sidebar |
| `ScalarHideDownloadButton` | boolean | Hide the OpenAPI spec download button |
| `ScalarHideModels` | boolean | Hide the Models/Schemas section |
| `ScalarHideClientButton` | boolean | Hide the client generation button |
| `ScalarHideTestRequestButton` | boolean | Hide the test request button |

## Documentation Configuration

Each entity can include a `Documentation` section to customize its OpenAPI representation:

```json
{
  "DatabaseObjectName": "Products",
  "AllowedColumns": ["ItemCode", "Description", "Price"],
  "Documentation": {
    "TagDescription": "**Product Catalog**\n\nAccess the product catalog with detailed item information.",
    "MethodDescriptions": {
      "GET": "Query product catalog with filtering and pagination",
      "POST": "Add new products to the catalog",
      "PUT": "Update existing product information",
      "DELETE": "Remove products from catalog"
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

### Documentation Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `TagDescription` | string | Yes | Main description for the endpoint group |
| `MethodDescriptions` | object | No | Specific descriptions for each HTTP method |
| `MethodDocumentation` | object | No | Longer per-method descriptions (Markdown supported) |
| `Examples` | object | No | A response example per HTTP method, shown verbatim in the reference instead of generated sample data |

When you provide an example under `Examples`, Portway shows exactly that payload for the method's successful response. It is a friendly way to make sure the reference reflects the shape your integration really returns, rather than a generated approximation of it. Leaving it out is perfectly fine too, in which case Portway falls back to sample data as before. Every endpoint type accepts `Examples`, so this works equally well for SQL, Proxy, Composite, Static, Webhook, and Files.

## Retiring an Endpoint

Endpoints rarely disappear overnight. Usually you want to tell people an endpoint is on its way out well before you delete it, and sometimes you need to take one out of service for an afternoon. Portway gives you two separate flags for those two situations, and it can be helpful to think of them as a signal and a switch.

`Deprecated` is the signal. Adding it to any `entity.json` marks every operation that endpoint contributes as deprecated in the document, which Scalar then renders with a strikethrough:

```json
{
  "DatabaseObjectName": "LegacyOrders",
  "AllowedMethods": ["GET"],
  "Deprecated": true
}
```

Your callers keep working exactly as before, because the flag only touches the documentation. That makes it a comfortable way to announce a planned retirement while you give integrations time to migrate. Every endpoint type understands it: SQL, Proxy, Composite, Static, Webhook, and Files.

## Switching an Endpoint Off

`Enabled` is the switch. When you set it to `false`, the endpoint stops serving:

```json
{
  "DatabaseObjectName": "LegacyOrders",
  "AllowedMethods": ["GET"],
  "Enabled": false
}
```

Calls then receive `503 Service Unavailable` in the shared error envelope, together with a `Retry-After` header so clients and caches know to wait rather than retry immediately:

```json
{
  "success": false,
  "error": "This endpoint is temporarily disabled for scheduled maintenance."
}
```

You will notice the endpoint is still listed in the OpenAPI document, marked deprecated with a `[Disabled]` prefix on its summary. This is deliberate: if it vanished from the reference, a temporary outage would be indistinguishable from a deletion, and your callers would have no way to tell which one they were looking at. Disabled endpoints are left out of the MCP tool list as well, and the change is picked up on the next configuration reload, so a restart is not needed.

`Enabled` defaults to `true`, which means leaving it out keeps your existing configuration exactly as it is. If you would like to see the behaviour first-hand, the `Static/Production/Machines` sample ships switched off as a worked example.

## File Upload Encoding

File endpoints describe their upload as `multipart/form-data` and document how the `file` part itself is encoded. The media types come from the endpoint's `AllowedExtensions`. A reports endpoint limited to `.pdf`, `.xlsx`, and `.csv` therefore advertises exactly those three types, rather than a generic binary blob.

Leaving `AllowedExtensions` out is fine too. The part then falls back to `application/octet-stream`, which is still perfectly valid and simply tells callers less about what you accept.

## Error Responses

One of the nicer things about generating the reference from your configuration is that errors only have to be described once. The document registers two component schemas: `ErrorResponse`, which is the familiar `{ success, error }` envelope, and `ValidationErrorResponse`, which adds a per-field `details` array for `422` responses. Every documented `4xx` and `5xx` response then points at one of those two.

Which status codes turn up on a given operation still depends on what that endpoint type can actually return, so a Static endpoint and a Files upload will not show the same list. Only the shape is shared. If you would like to see the envelope itself, the [reference index](/reference/) walks through it.

## Namespaces and Tags

Namespaces do double duty in the reference: each one becomes a tag, and your operations are grouped underneath the namespace they belong to. `NamespaceDisplayName` sets the label you see in the sidebar, and `Documentation.TagDescription` fills in the text below it.

OpenAPI 3.2 also allows one tag to be nested under another, and Portway emits that relationship whenever a tag name contains a `/`. In practice you are unlikely to see it yet, because namespaces are a single directory level today and routing does not resolve deeper nesting. The support is in place for when that changes.

## Schema Discovery

For SQL endpoints, Portway reads column metadata from the database at startup. It connects to the first allowed environment listed in the endpoint's `AllowedEnvironments`. Non-SQL endpoints are not queried.

:::warning
If you're using Windows Authentication (`Trusted_Connection=True`) in your Environments, the IIS Application Pool identity needs permissions on every environment database. With SQL Authentication, each environment uses its own credentials instead.
:::

## Tag Descriptions

### Formatting Guidelines

Use **bold titles** and descriptive content:

```json
"TagDescription": "**Service Management**\n\nComprehensive service request lifecycle management. Track customer issues, assign technicians, and monitor progress."
```

### Include Context and Purpose

Provide clear information about what the endpoint does:

```json
"TagDescription": "**Financial Data**\n\nRetrieve outstanding debtor information and payment tracking. Access critical financial data for accounts receivable management and cash flow analysis."
```

## Method Descriptions

### Standard CRUD Operations

Provide clear, action-oriented descriptions:

```json
"MethodDescriptions": {
  "GET": "Query and retrieve records with OData filtering support",
  "POST": "Create new records with validation and business rules",
  "PUT": "Update existing records with partial or complete data",
  "DELETE": "Remove records with referential integrity checks"
}
```

### Specialized Operations

For stored procedures or custom operations:

```json
"MethodDescriptions": {
  "GET": "Retrieve service requests with status and assignment filtering",
  "POST": "Create new service requests with automatic assignment logic",
  "PUT": "Update service request status, priority, and assignment"
}
```

### Composite Endpoints

For complex operations:

```json
"MethodDescriptions": {
  "POST": "Create complete sales orders with header and multiple order lines in a coordinated transaction"
}
```

## Documentation Structure

All entity types support the same OpenAPI documentation structure through the `Documentation` section:

```json
{
  // ... entity configuration ...
  "Documentation": {
    "TagDescription": "**Tag Name**\n\nDescription of what this endpoint group does.",
    "MethodDescriptions": {
      "GET": "Description for GET operations",
      "POST": "Description for POST operations",
      "PUT": "Description for PUT operations",
      "DELETE": "Description for DELETE operations"
    }
  }
}
```

## Markdown Support

### Supported Elements

OpenAPI descriptions support the Github-flavoured markdown. It also allows for limited HTML-support (`<br>`, `<p>`).

- **Bold text** with `**text**`
- *Italic text* with `*text*`
- `Code blocks` with backticks
- Line breaks with `\n`
- Links with `[text](url)`

### Admonitions

Use special formatting for callouts:

```json
"TagDescription": "**Product Catalog**\n\nAccess the product catalog with basic item information.\n> [!tip]> This endpoint doesn't and will never include complex price information."
```

See the [Scalar markdown reference](https://guides.scalar.com/scalar/scalar-api-references/markdown#alerts) for supported alert types.

## Hidden Endpoint Handling

Endpoints you mark `Hidden` are left out of the OpenAPI document while continuing to serve requests as normal. This is handy for internal endpoints you would rather not advertise:

```json
{
  "Hidden": true
  // The Documentation section is ignored for hidden endpoints
}
```

## Environment-Specific Documentation

Documentation is automatically filtered by environment. Only endpoints available in the current environment appear in the OpenAPI documentation:

```json
{
  "AllowedEnvironments": ["prod", "dev"]
  // Only appears in documentation for prod and dev environments
}
```

## Troubleshooting

### Documentation Not Appearing

1. Verify JSON syntax in entity.json
2. Check that `Documentation` section is properly formatted
3. Ensure endpoint is not marked as `Hidden: true`
4. Confirm endpoint is allowed in current environment

### Markdown Not Rendering

1. Use `\n` for line breaks in JSON strings
2. Escape special characters properly
3. Test markdown formatting in a separate viewer
4. Check for unclosed formatting tags

### Missing Method Descriptions

1. Ensure method names match exactly (case-sensitive)
2. Verify methods are listed in `AllowedMethods` or `Methods`
3. Check that methods are supported for the endpoint type

## Related Topics

- [Entity Configuration](/reference/entity-config) - Complete entity configuration guide
- [API Overview](/reference/) - API endpoint patterns and usage
- [Environment Settings](/reference/environment-settings) - Environment configuration
