---
title: Static Endpoints
description: "Serve pre-defined JSON, XML, or CSV files with optional OData filtering"
---

# Static Endpoints

Static endpoints return the contents of a file stored next to the endpoint configuration.

## Configuration

Each endpoint is a folder `endpoints/Static/{EndpointName}/` with `entity.json` and the content file:

```
endpoints/Static/ProductionMachine/
├── entity.json
└── summary.xml
```

Contents of `entity.json`:

```json
{
  "ContentType": "application/xml",
  "ContentFile": "summary.xml",
  "EnableFiltering": true,
  "Hidden": false,
  "AllowedEnvironments": ["prod", "dev"],
  "Documentation": {
    "TagDescription": "Production machine data",
    "MethodDescriptions": {
      "GET": "Retrieve machine details"
    }
  }
}
```

All properties, types and defaults: [Entity configuration](/reference/entity-config#static).

## Supported content types

| Format | MIME type | OData filtering |
|---|---|---|
| JSON | `application/json` | Supported |
| XML | `application/xml` | Supported |
| CSV | `text/csv` | Not supported |
| Plain text | `text/plain` | Not supported |
| Images | `image/*` | Not supported |

## OData filtering

With `EnableFiltering: true`, static endpoints accept the same OData options as SQL endpoints:

```http
GET /api/prod/ProductionMachine?$filter=status eq 'running'&$top=5&$orderby=name
GET /api/prod/ProductionMachine?$select=id,name,status
```

Filtered responses include these headers:

- `X-Filtering-Status: Applied`
- `X-Total-Count`: item count before filtering
- `X-Returned-Count`: item count after filtering

## Next steps

- [SQL Endpoints](/guide/endpoints-sql)
- [Environments](/guide/environments)
