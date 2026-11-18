---
title: Teable Integration
description: "Proxy endpoints for the Teable record API with the access token stored in the environment"
---

# Teable Integration

Teable's REST API uses a personal access token (prefix `teable_`) in the `Authorization` header, the same header as Portway tokens. The environment supplies the Teable token, and clients authenticate with an `X-API-Key` header instead.

## Environment

```json [environments/teable/settings.json]
{
  "ServerName": "YOUR-SERVER",
  "Headers": {
    "Authorization": "Bearer teable_YOUR_TOKEN"
  },
  "Authentication": {
    "Enabled": true,
    "OverrideGlobalToken": true,
    "Methods": [
      {
        "Type": "ApiKey",
        "Name": "X-API-Key",
        "Value": "YOUR_CLIENT_API_KEY",
        "In": "Header"
      }
    ]
  }
}
```

The `OverrideGlobalToken` setting replaces bearer token authentication for the whole environment. Teable requires a dedicated environment. The environment `Authorization` header replaces a client header of the same name. Methods: [Environment Authentication](/reference/environment-auth).

## Endpoints

```json [endpoints/Proxy/Teable/Orders/entity.json]
{
  "Url": "http://teable:3000/api/table/YOUR_TABLE_ID/record",
  "Methods": ["GET", "POST", "PATCH", "DELETE"],
  "AllowedEnvironments": ["teable"]
}
```

## Requests

Teable query parameters are passed through:

```http
GET /api/teable/Teable/Orders?take=25&skip=0
X-API-Key: YOUR_CLIENT_API_KEY
```

```http
POST /api/teable/Teable/Orders
X-API-Key: YOUR_CLIENT_API_KEY
Content-Type: application/json

{ "records": [ { "fields": { "Customer": "60093", "Status": "Open" } } ] }
```

## Limitations

- Filtering, sorting and field selection use Teable parameters, not OData.
- Proxy endpoints return every field of the table. A [SQL endpoint](/guide/endpoints-sql) on Teable's PostgreSQL database provides curated, OData-queryable reads; writes there bypass Teable field logic and permissions.

## Troubleshooting

| Symptom | Check |
|---|---|
| `401` from Teable | `Authorization` value in the environment; token validity |
| `401` from Portway | `X-API-Key` value; `Authentication.Enabled` |
| Bearer tokens rejected in the environment | `OverrideGlobalToken: true` applies to the whole environment |
| Filters ignored | Teable parameters instead of `$filter` |
