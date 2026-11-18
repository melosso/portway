---
title: NocoDB Integration
description: "Proxy endpoints for the NocoDB v2 REST API with the xc-token stored in the environment"
---

# NocoDB Integration

NocoDB tables are exposed through proxy endpoints to the v2 REST API. Portway adds the NocoDB `xc-token` header from the environment; clients use Portway tokens.

## Environment

The API token is created in NocoDB under **Account Settings → API Tokens**:

```json [environments/prod/settings.json]
{
  "ServerName": "YOUR-SERVER",
  "Headers": {
    "xc-token": "YOUR_NOCODB_API_TOKEN"
  }
}
```

Each environment can point the same endpoints at a different NocoDB instance with its own token.

## Endpoints

One endpoint per table; the table id is part of the NocoDB table URL:

```json [endpoints/Proxy/Nocodb/Orders/entity.json]
{
  "Url": "http://nocodb:8080/api/v2/tables/YOUR_TABLE_ID/records",
  "Methods": ["GET", "POST", "PATCH", "DELETE"],
  "AllowedEnvironments": ["prod", "dev"]
}
```

## Requests

NocoDB query parameters are passed through:

```http
GET /api/prod/Nocodb/Orders?limit=25&where=(Status,eq,Open)&sort=-CreatedAt
Authorization: Bearer YOUR_PORTWAY_TOKEN
```

```http
POST /api/prod/Nocodb/Orders
Authorization: Bearer YOUR_PORTWAY_TOKEN
Content-Type: application/json

{ "Customer": "60093", "Status": "Open", "Amount": 1250.00 }
```

Updates and deletes send the record `Id` in the body:

```http
PATCH /api/prod/Nocodb/Orders
Authorization: Bearer YOUR_PORTWAY_TOKEN
Content-Type: application/json

{ "Id": 42, "Status": "Shipped" }
```

## Limitations

- Filtering, sorting and paging use NocoDB parameters (`where`, `sort`, `limit`, `offset`), not OData.
- Proxy endpoints return every field of the table. A curated column set requires a [SQL endpoint](/guide/endpoints-sql) on the underlying database.
- Writes to the underlying database bypass NocoDB formulas, webhooks and permissions.

## Troubleshooting

| Symptom | Check |
|---|---|
| `401` from NocoDB | `xc-token` value; token not revoked in NocoDB |
| Empty result | Table id in `Url`; workspace access of the token |
| Filters ignored | NocoDB syntax (`where=(Field,eq,Value)`) instead of `$filter` |
| Stale data | [Caching](/reference/caching) |
