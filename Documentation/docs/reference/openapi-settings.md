# OpenAPI Documentation Settings

This guide focuses on configuring OpenAPI documentation for entities and tags in Portway. All endpoints automatically generate OpenAPI documentation based on their configuration, which is then exposed through the Swagger UI.

## Overview

Portway automatically generates comprehensive OpenAPI documentation for all configured endpoints. The documentation includes:

- **Schema discovery** - Automatically determine the schema (on SQL endpoints)
- **Tag descriptions** - Group related endpoints with descriptive categories
- **Method descriptions** - Specific documentation for each HTTP method
- **Parameter documentation** - Automatic schema generation from entity configuration
- **Response schemas** - Based on database columns or proxy endpoint responses

## Global OpenAPI Configuration

The main OpenAPI documentation configuration is defined in `appsettings.json`. This controls the overall appearance, behavior, and content of your API documentation.

**What you can configure:**
- **Branding**: Custom title, description, and contact information
- **UI Theme**: Modern Scalar interface with customizable themes
- **Authentication**: Security schemes and authentication methods
- **Behavior**: Default expansions, filtering, and user interactions

```json
{
  "Swagger": {
    "Enabled": true,
    "BaseProtocol": "https",
    "Title": "Portway: API Gateway",
    "Version": "v1",
    "Description": "This is Portway. A lightweight API gateway designed to integrate your platforms with your Windows environment. It provides a simple, fast and efficient way to connect various data sources and services.",
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
    "RoutePrefix": "docs",
    "DocExpansion": "List",
    "DefaultModelsExpandDepth": -1,
    "DisplayRequestDuration": true,
    "EnableFilter": false,
    "EnableDeepLinking": false,
    "EnableValidator": true,
    "EnableScalar": true,
    "ScalarTheme": "default",
    "ScalarShowSidebar": true,
    "ScalarHideDownloadButton": true,
    "ScalarHideModels": true
  }
}
```

### Configuration Properties

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
| `DocExpansion` | string | Default expansion state of documentation sections (`List`, `Full`, `None`) |
| `DefaultModelsExpandDepth` | integer | Default depth for expanding models/schemas (-1 to hide) |
| `DisplayRequestDuration` | boolean | Show request duration in UI |
| `EnableFilter` | boolean | Enable API filtering in documentation |
| `EnableDeepLinking` | boolean | Enable deep linking to specific operations |
| `EnableValidator` | boolean | Enable schema validation in UI |
| `ForceHttpsInProduction` | boolean | Force HTTPS URLs in production environments |
| `EnableScalar` | boolean | Use Scalar UI instead of default Swagger UI |
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
    }
  }
}
```

### Documentation Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `TagDescription` | string | Yes | Main description for the endpoint group |
| `MethodDescriptions` | object | No | Specific descriptions for each HTTP method |

## Schema Discovery
Portway automatically generates API documentation by reading your database schema at startup. It connects to the first allowed environment listed for each SQL endpoint to retrieve column metadata. This only included the SQL endpoint type.

:::warning
If you're using Windows Authentication (`Trusted_Connection=True`) in your Environments, ensure your IIS Application Pool identity has the appropriate permissions on all environment databases. With SQL Authentication, each environment uses its own credentials.
:::

## Tag Description Best Practices

It's wise to describe the tag description, with Markdown support.

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

For more information, please see the detailed list of supported alerts at [Scalar Guide](https://guides.scalar.com/scalar/scalar-api-references/markdown#alerts).

## Tag Organization

### Grouping Strategy

Organize endpoints by business function:

- **Product Management** - Product catalog, inventory, pricing
- **Customer Management** - Accounts, contacts, relationships
- **Order Processing** - Sales orders, fulfillment, shipping
- **Financial Operations** - Invoicing, payments, reporting
- **System Integration** - Webhooks, data sync, external APIs

### Naming Conventions

Use consistent naming patterns:

```json
// Good examples
"**Product Catalog**"
"**Service Request Management**"
"**Financial Reporting**"
"**Integration Webhooks**"

// Avoid
"Products"
"Manage Service Requests"
"Finance"
"Webhooks Endpoint"
```

## Private Endpoint Handling

Private endpoints are automatically excluded from OpenAPI documentation:

```json
{
  "IsPrivate": true
  // Documentation section is ignored for private endpoints
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

## Best Practices

### 1. Clear Tag Names

Use business-focused, descriptive tag names:

```json
"TagDescription": "**Customer Account Management**\n\nComplete customer lifecycle management including account creation, updates, and relationship tracking."
```

### 2. Method-Specific Value

Provide descriptions that go beyond generic CRUD operations:

```json
"MethodDescriptions": {
  "GET": "Search products with advanced filtering by category, price range, and availability",
  "POST": "Add new products with automatic SKU generation and inventory setup"
}
```

### 3. Business Context

Explain the business purpose and value:

```json
"TagDescription": "**Outstanding Items**\n\nRetrieve outstanding debtor information and payment tracking for accounts receivable management and cash flow analysis."
```

### 4. Consistent Formatting

Use consistent patterns across all endpoints:

```json
"TagDescription": "**[Business Area]**\n\n[Primary purpose]. [Additional context or capabilities]."
```

## Troubleshooting

This section may assist you in troubleshooting:

### Documentation Not Appearing

1. Verify JSON syntax in entity.json
2. Check that `Documentation` section is properly formatted
3. Ensure endpoint is not marked as `IsPrivate: true`
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
- [API Overview](/reference/api/overview) - API endpoint patterns and usage
- [Environment Settings](/reference/configuration/environment-settings) - Environment configuration
