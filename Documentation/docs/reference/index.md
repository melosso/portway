---
title: API Reference
description: "Routes, endpoint types, authentication, response codes, and query parameters for Portway API requests"
---

# API Reference

URL pattern:

```
/api/{environment}/{endpoint}
```

The environment segment selects a folder under `environments/`; the endpoint segment is the endpoint name, or `{namespace}/{endpoint}` for namespaced endpoints.

## Endpoint types

The namespace segment is optional.

| Type | URL Pattern | Description |
|------|-------------|-------------|
| SQL | `/api/{env}/{namespace}/{endpoint}` | Tables, views, stored procedures and table-valued functions with OData queries |
| Proxy | `/api/{env}/{namespace}/{endpoint}` | Forwards requests to internal HTTP services |
| Static | `/api/{env}/{namespace}/{endpoint}` | Serves a content file |
| Composite | `/api/{env}/{namespace}/{endpoint}` | Calls several proxy endpoints in sequence |
| Webhook | `/api/{env}/{namespace}/{name}/{id}` | Stores POST payloads in a SQL table |
| Files | `/api/{env}/files/{namespace}/{name}` | File upload, download, delete and listing |

## Authentication

Bearer token on every request:

```http
Authorization: Bearer your_token_here
```

Unauthenticated paths: `/health`, `/health/live` and the Prometheus scrape path. `/health/details` requires a token ([Health checks](/reference/health-checks)). Token scopes: [Authentication](/reference/api-auth).

## Response codes

| Code | Meaning |
|------|---------|
| 200 | OK |
| 201 | Created |
| 400 | Invalid request, query or tenant header |
| 401 | Missing or invalid token |
| 403 | Token scope, environment or tenant excludes the request |
| 404 | Endpoint or record not found |
| 429 | Rate limit exceeded |
| 500 | Internal Server Error |

## Error format

Errors share one envelope on every endpoint type:

```json
{
  "success": false,
  "error": "A human-readable message"
}
```

Validation failures (`422`) add a `details` array:

```json
{
  "success": false,
  "error": "Validation failed",
  "details": [
    { "field": "Price", "message": "is required" }
  ]
}
```

The OpenAPI document defines them as `ErrorResponse` and `ValidationErrorResponse`.

A `500` adds `traceId`, which identifies the matching server log entry:

```json
{
  "success": false,
  "error": "Error processing. Please check the logs for more details.",
  "traceId": "00-530e2d01e7e446f7c1a9936cd2858df4-77e01a3151145562-02"
}
```

## Status codes by endpoint type

| Endpoint type | Success | Error codes |
|---------------|---------|-------------|
| SQL (read) | `200` | `400` `401` `403` `404` `500` `503` |
| SQL (write) | `200` `201` | `400` `401` `403` `404` `422` `500` `503` |
| SQL (query) | `200` | `400` `401` `403` `404` `415` `500` `503` |
| Proxy | pass-through | `400` `401` `403` `404` `500` `503` |
| Static | `200` | `400` `401` `403` `404` `406` `500` `503` |
| Composite | `200` | `400` `401` `403` `404` `422` `500` `503` |
| Webhook | `201` | `400` `401` `403` `404` `500` `503` |
| Files | `200` `201` `206` | `400` `401` `403` `404` `409` `413` `415` `416` `500` `503` |

Any endpoint can return `429`. A `503` with `Retry-After` means the endpoint is disabled (`Enabled: false`).

| Endpoint type | Success body |
|---|---|
| SQL read | Rows |
| SQL stored procedure | Procedure result |
| Static | Configured content |
| File download | File bytes |
| Proxy, Composite | Upstream or final step response |

## OData query parameters

SQL and Static endpoints accept `$select`, `$filter`, `$orderby`, `$top`, `$skip` and `$count`.

```http
GET /api/prod/Products?$select=Name,Price&$filter=Price gt 100&$orderby=Name desc&$top=50
```

Options: [OData syntax](/reference/odata). Operators: [Filter operations](/reference/filters).

## Rate limiting

Requests are limited per IP address and per token. Responses include `X-RateLimit-*` headers; `429` responses include `Retry-After`. Configuration: [Rate limiting](/guide/rate-limiting). Headers: [HTTP headers](/reference/headers).

## Next steps

- [Authentication](/reference/api-auth)
- [OData Syntax](/reference/odata)
- [Entity Configuration](/reference/entity-config)
- [HTTP Headers](/reference/headers)
