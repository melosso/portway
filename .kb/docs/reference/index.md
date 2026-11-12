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
| Files | `/api/{env}/files/{namespace}/{name}` | Uploads, downloads and deletes files. Returns the file list of the endpoint |

## Authentication

Bearer token on every request:

```http
Authorization: Bearer your_token_here
```

Unauthenticated paths: `/health`, `/health/live` and the Prometheus scrape path. `/health/details` requires a token ([Health checks](/reference/health-checks)). Token scopes: [Authentication](/reference/api-auth).

## Response codes

| Code | Description |
|---|---|
| 200 | The request succeeded |
| 201 | The request created a record, file or webhook delivery |
| 206 | The response contains the byte range requested in the `Range` header |
| 304 | The `If-None-Match` header matches the current `ETag`. The response has no body |
| 400 | The request, query or tenant header is invalid |
| 401 | The token is missing or invalid |
| 403 | The token does not grant access to the endpoint, environment or tenant |
| 404 | The endpoint or record does not exist |
| 406 | The `Accept` header does not include the content type of the static endpoint |
| 409 | The uploaded file already exists. `overwrite=true` replaces it |
| 413 | The request body exceeds the size limit of the endpoint |
| 415 | The endpoint does not accept the request content type or file extension |
| 416 | The `Range` header requests bytes beyond the end of the file |
| 422 | The request body fails validation. `details` contains each failed field |
| 429 | The client exceeded the rate limit. `Retry-After` contains the wait in seconds |
| 500 | The request failed on the server. `traceId` identifies the log entry |
| 503 | The endpoint is disabled (`Enabled: false`) |

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
| Webhook | `200` `201` | `400` `401` `403` `404` `500` `503` |
| Files | `200` `201` `206` | `400` `401` `403` `404` `409` `413` `415` `416` `500` `503` |

Any endpoint can return `429`. A `503` includes `Retry-After`. A webhook returns `200` for a duplicate delivery ([Deduplication](/guide/endpoints-webhook#deduplication)).

| Endpoint type | Success body |
|---|---|
| SQL read | Rows |
| SQL stored procedure | Procedure result |
| Static | Configured content |
| File download | File bytes |
| Proxy, Composite | Upstream or final step response |

## OData query parameters

SQL and Static endpoints accept `$select`, `$filter`, `$orderby`, `$top`, `$skip` and `$count`. SQL endpoints also accept `$expand`.

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
