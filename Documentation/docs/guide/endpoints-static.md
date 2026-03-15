# Static Endpoints

> Serve pre-defined JSON, XML, or CSV files with optional OData filtering.

Static endpoints return the contents of a file stored alongside the endpoint configuration. They support the same OData query parameters as SQL endpoints when `EnableFiltering` is enabled, making them suitable for mock data, reference datasets, and read-only configuration responses.

## Configuration

Create a folder under `endpoints/Static/{EndpointName}/` containing `entity.json` and the content file:

```
endpoints/Static/ProductionMachine/
├── entity.json
└── summary.xml
```

**`entity.json`:**

```json
{
  "ContentType": "application/xml",
  "ContentFile": "summary.xml",
  "EnableFiltering": true,
  "IsPrivate": false,
  "AllowedEnvironments": ["prod", "dev"],
  "Documentation": {
    "TagDescription": "Production machine data",
    "MethodDescriptions": {
      "GET": "Retrieve machine details"
    }
  }
}
```

### Configuration properties

| Property | Required | Type | Description |
|---|---|---|---|
| `ContentFile` | Yes | string | Filename relative to the endpoint directory |
| `ContentType` | No | string | MIME type. Auto-detected from file extension if omitted |
| `EnableFiltering` | No | boolean | Enable OData filtering. Defaults to `false` |
| `IsPrivate` | No | boolean | Exclude from OpenAPI documentation. Defaults to `false` |
| `AllowedEnvironments` | Yes | array | Environments where this endpoint responds |
| `Documentation` | No | object | OpenAPI metadata |

## Supported content types

| Format | MIME type | OData filtering |
|---|---|---|
| JSON | `application/json` | Supported |
| XML | `application/xml` | Supported |
| CSV | `text/csv` | Not supported |
| Plain text | `text/plain` | Not supported |
| Images | `image/*` | Not supported |

## OData filtering

When `EnableFiltering: true`, static endpoints accept the same OData parameters as SQL endpoints:

```http
GET /api/prod/ProductionMachine?$filter=status eq 'running'&$top=5&$orderby=name
GET /api/prod/ProductionMachine?$select=id,name,status
```

When filtering is applied, the response includes:

- `X-Filtering-Status: Applied`
- `X-Total-Count`, total items before filtering
- `X-Returned-Count`, items returned after filtering

## Next steps

- [SQL Endpoints](./endpoints-sql)
- [Environments](./environments)
