---
title: Odoo Integration
description: "Proxy endpoint for the Odoo JSON-RPC API"
---

# Odoo Integration

Odoo's external API is JSON-RPC, with the database, user and API key in the request body. Portway does not modify request bodies. Clients send their own Odoo credentials. Portway controls which clients reach Odoo.

## Endpoint

```json [endpoints/Proxy/Odoo/Rpc/entity.json]
{
  "Url": "http://odoo.internal:8069/jsonrpc",
  "Methods": ["POST"],
  "AllowedEnvironments": ["prod"]
}
```

No environment headers are required. Each integration uses its own Odoo user and API key (user preferences, **Account Security**); Odoo access rights limit what it can read and write.

## Requests

The `authenticate` call returns the user id:

```http
POST /api/prod/Odoo/Rpc
Authorization: Bearer YOUR_PORTWAY_TOKEN
Content-Type: application/json

{
  "jsonrpc": "2.0",
  "method": "call",
  "params": {
    "service": "common",
    "method": "authenticate",
    "args": ["mydb", "integration@company.com", "ODOO_API_KEY", {}]
  }
}
```

Data calls use `execute_kw` with that id:

```http
POST /api/prod/Odoo/Rpc
Authorization: Bearer YOUR_PORTWAY_TOKEN
Content-Type: application/json

{
  "jsonrpc": "2.0",
  "method": "call",
  "params": {
    "service": "object",
    "method": "execute_kw",
    "args": [
      "mydb", 2, "ODOO_API_KEY",
      "res.partner", "search_read",
      [[["is_company", "=", true]]],
      { "fields": ["name", "email"], "limit": 25 }
    ]
  }
}
```

## Limitations

- Request bodies contain Odoo API keys. `IncludeRequestBodies` in [traffic logging](/reference/audit) stays off for this endpoint.
- All calls use one endpoint; model access is limited in Odoo, not per Portway endpoint.

## Troubleshooting

| Symptom | Check |
|---|---|
| `401` from Portway | Token scope for the endpoint and environment |
| `odoo.exceptions.AccessDenied` | Database, login and API key in the body |
| `result: false` on `authenticate` | Database name |
| Access errors on a model | Access rights and record rules of the Odoo user |
