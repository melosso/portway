---
title: Static Endpoints
description: "Serve pre-defined JSON, XML, or CSV files with optional OData filtering"
---

# Static Endpoints

Sometimes the data you want to serve doesn't live in a database at all. Static endpoints return the contents of a file stored alongside the endpoint configuration, and when `EnableFiltering` is on they answer the same OData query parameters as SQL endpoints. That makes them a natural fit for mock data, reference datasets, and read-only configuration responses.

## Configuration

Setting one up takes a folder under `endpoints/Static/{EndpointName}/` containing `entity.json` and the content file:

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

### Configuration properties


Every property this endpoint type accepts, with its type and default, is listed in [Entity configuration](/reference/entity-config#endpoint-static).

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

- [SQL Endpoints](/guide/endpoints-sql)
- [Environments](/guide/environments)
