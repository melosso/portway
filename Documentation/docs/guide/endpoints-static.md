# Static Endpoints

Static endpoints serve pre-defined content files (JSON, XML, CSV, etc.) with optional OData filtering capabilities. They're perfect for providing mock data, configuration files, or read-only datasets.

## Configuration

Static endpoints are configured using JSON files in the `endpoints/Static/{EndpointName}/` directory.

### Basic Configuration

**`endpoints/Static/ProductionMachine/entity.json`**

```json
{
  "ContentType": "application/xml",
  "ContentFile": "summary.xml",
  "EnableFiltering": true,
  "IsPrivate": false,
  "AllowedEnvironments": ["prod", "dev"],
  "Documentation": {
    "TagDescription": "Production machine data for testing and simulation",
    "MethodDescriptions": {
      "GET": "Retrieve machine details"
    }
  }
}
```

### Configuration Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `ContentType` | string | No | MIME type (auto-detected if not specified) |
| `ContentFile` | string | Yes | Filename relative to endpoint directory |
| `EnableFiltering` | boolean | No | Enable OData filtering support (default: false) |
| `IsPrivate` | boolean | No | Whether endpoint requires authentication (default: false) |
| `AllowedEnvironments` | array | Yes | List of environments where endpoint is available |
| `Documentation` | object | No | OpenAPI documentation metadata |

## Content Files

Place your content files in the same directory as the `entity.json` file:

```
endpoints/Static/ProductionMachine/
├── entity.json
└── summary.xml
```

### Supported Content Types

- **JSON** (`application/json`) - With full OData filtering
- **XML** (`application/xml`) - With OData filtering support
- **CSV** (`text/csv`) - Raw file serving
- **Text** (`text/plain`) - Raw file serving
- **Images** (`image/*`) - Raw file serving

## OData Filtering

When `EnableFiltering: true` is set, Static endpoints support OData query parameters:

- `$filter` - Filter data based on field values
- `$select` - Choose specific fields to return
- `$orderby` - Sort results
- `$top` - Limit number of results
- `$skip` - Skip a number of results

### Examples

```http
# Get first machine
GET /api/prod/ProductionMachine?$top=1

# Filter by status
GET /api/prod/ProductionMachine?$filter=status eq 'running'

# Select specific fields
GET /api/prod/ProductionMachine?$select=id,name,status

# Combined filtering
GET /api/prod/ProductionMachine?$filter=status eq 'running'&$top=5&$orderby=name
```

## Use Cases

- **Mock APIs** - Provide realistic test data for frontend development
- **Configuration Data** - Serve application settings or reference data
- **Dashboards** - Provide static datasets for reporting and visualization
- **API Simulation** - Prototype APIs before backend implementation

## Authentication

Static endpoints can be public or private:

- **Public** (`IsPrivate: false`) - No authentication required
- **Private** (`IsPrivate: true`) - Requires Bearer token authentication

```json
{
  "IsPrivate": true,
  "AllowedEnvironments": ["prod"]
}
```

## Response Headers

When filtering is applied, additional headers are included:

- `X-Filtering-Status: Applied` - Indicates filtering was processed
- `X-Total-Count` - Total number of items before filtering
- `X-Returned-Count` - Number of items returned after filtering
