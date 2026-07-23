---
title: HTTP Methods
description: "Every HTTP method Portway endpoints understand, from plain GET to body-based QUERY"
---

# HTTP Methods

Every endpoint declares which methods it accepts through `AllowedMethods` in its `entity.json`. This page is your dictionary for those methods: what each one does, which endpoint types support it, and the details that matter when you wire up a client.

## Overview

| Method | Meaning | SQL | Proxy | Composite | Static | File |
|---|---|:---:|:---:|:---:|:---:|:---:|
| `GET` | Read with OData query parameters | ✅ | ✅ | ❌ | ✅ | ✅ |
| `QUERY` | Read with the query in the request body | ✅ | ✅ | ❌ | ✅ | ❌ |
| `POST` | Create a record, or invoke a composite flow | ✅ | ✅ | ✅ | ❌ | ✅ |
| `PUT` | Full update | ✅ | ✅ | ❌ | ❌ | ❌ |
| `PATCH` | Partial update | ✅ | ✅ | ❌ | ❌ | ❌ |
| `DELETE` | Remove a record or file | ✅ | ✅ | ❌ | ❌ | ✅ |
| `MERGE` | Legacy update verb for older backends | ❌ | ✅ | ❌ | ❌ | ❌ |

The accepted values for `AllowedMethods` are `GET`, `POST`, `PUT`, `PATCH`, `DELETE`, `MERGE` and `QUERY`. Anything else is flagged as a configuration error when the endpoint loads. File endpoints manage uploads and downloads through their own routes, so their table column above reflects upload (`POST`), download (`GET`) and removal (`DELETE`).

## GET

The workhorse. Filters, projection, sorting and paging all travel as OData query parameters in the URL:

```http
GET /api/prod/Products?$filter=Price gt 20&$orderby=Name&$top=10
```

The full query syntax lives in the [OData reference](/reference/odata).

## QUERY

`QUERY` (RFC 10008) is a safe, idempotent read whose criteria travel in the request body instead of the URL. It shines when a search combines many filters at once and the URL would become unwieldy, or when criteria should not appear in access logs:

```http
QUERY /api/prod/Inventory/StockLevels
Content-Type: application/json

{
  "select": "Sku,Warehouse,Quantity",
  "filter": "Quantity lt 10 and Warehouse eq 'AMS'",
  "orderby": "Quantity",
  "top": 25,
  "skip": 0
}
```

The body fields mirror the OData query parameters: `select`, `filter`, `orderby`, `top` and `skip`. The response is identical to the equivalent GET.

A few behaviours worth knowing:

* QUERY only accepts `Content-Type: application/json`; anything else returns `415 Unsupported Media Type`.
* Responses stay cacheable. The request body is folded into the cache key, so two different queries never share a cache entry.
* The response carries a `Content-Location` header pointing at the equivalent GET URL.
* SQL, Static and Proxy endpoints accept QUERY. Composite and Webhook endpoints return `405 Method Not Allowed`.

## POST, PUT, PATCH and DELETE

Write methods on SQL endpoints route through one of two strategies:

* **Stored procedure** (default): the procedure receives the verb as `@Method` (`INSERT`, `UPDATE`, `PATCH` or `DELETE`) along with the payload columns. See the [SQL endpoints guide](/guide/endpoints-sql) for the procedure contract.
* **Table write mode** (`"WriteMode": "Table"`): Portway generates parameterized statements directly, guarded by the `AllowedColumns` allowlist and a declared `PrimaryKey`.

`PUT` expects the full record including the primary key in the body. `PATCH` sends only the columns that change. `DELETE` takes the key as a URL parameter:

```http
DELETE /api/prod/Products?id=abc123
```

On Proxy endpoints these methods forward to the backing service as-is, unless a translation applies (next section).

## MERGE and method translation

Some older backends, notably classic OData services, expect `MERGE` instead of `PATCH` or `PUT`. Proxy endpoints can translate on the way through:

```json
{
  "HttpMethodTranslation": "PUT:MERGE,POST:CREATE"
}
```

Clients keep speaking standard HTTP; the backend receives the verb it understands. The translation targets can be any of `GET`, `POST`, `PUT`, `DELETE`, `PATCH`, `MERGE`, `HEAD`, `OPTIONS` and `QUERY`. Details live in the [entity configuration reference](/reference/entity-config).

## Related Topics

- [Entity Configuration](/reference/entity-config): `AllowedMethods` and the rest of `entity.json`
- [OData Reference](/reference/odata): the query syntax shared by GET and QUERY
- [SQL Endpoints Guide](/guide/endpoints-sql): write strategies in depth
- [Headers Reference](/reference/headers): response headers including cache behaviour
