---
title: HTTP Methods
description: "HTTP methods per endpoint type: GET, QUERY, POST, PUT, PATCH, DELETE and MERGE"
---

# HTTP Methods

Each endpoint lists its accepted methods in `AllowedMethods` (SQL) or `Methods` (proxy) in `entity.json`.

## Overview

| Method | Meaning | SQL | Proxy | Composite | Static | File |
|---|---|:---:|:---:|:---:|:---:|:---:|
| `GET` | Read with OData query parameters | ✅ | ✅ | ❌ | ✅ | ✅ |
| `QUERY` | Read with the query in the request body | ✅ | ✅ | ❌ | ✅ | ❌ |
| `POST` | Create a record, or invoke a composite flow | ✅ | ✅ | ✅ | ❌ | ✅ |
| `PUT` | Full update | ✅ | ✅ | ❌ | ❌ | ❌ |
| `PATCH` | Partial update | ✅ | ✅ | ❌ | ❌ | ❌ |
| `DELETE` | Remove a record or file | ✅ | ✅ | ❌ | ❌ | ✅ |
| `MERGE` | Partial update under its OData name | ✅ | ✅ | ❌ | ❌ | ❌ |

Valid values: `GET`, `POST`, `PUT`, `PATCH`, `DELETE`, `MERGE`, `QUERY`; other values fail endpoint loading. File endpoints use upload (`POST`), download and list (`GET`) and delete (`DELETE`).

## GET

Read with OData query options in the URL:

```http
GET /api/prod/Products?$filter=Price gt 20&$orderby=Name&$top=10
```

Query syntax: [OData](/reference/odata). Related rows on SQL Table and View endpoints: [`$expand`](/reference/expand).

## QUERY

The `QUERY` method (RFC 10008) is a safe, idempotent read with the criteria in the request body, for long queries or criteria that must stay out of access logs:

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

Body fields: `select`, `filter`, `orderby`, `top`, `skip`. The response equals the corresponding GET.

* Only `Content-Type: application/json` is accepted; other types return `415`.
* Responses are cacheable; the cache key includes a hash of the body.
* The response includes `Content-Location` with the equivalent GET URL.
* Composite and webhook endpoints return `405`.

## POST, PUT, PATCH and DELETE

SQL endpoints write through one of two strategies:

| Strategy | Behavior |
|---|---|
| Stored procedure (default) | The procedure receives `@Method` (`INSERT`, `UPDATE`, `PATCH`, `DELETE`) and the payload columns ([SQL Endpoints](/guide/endpoints-sql)) |
| Table write mode (`"WriteMode": "Table"`) | Parameterized statements limited to `AllowedColumns` and keyed on `PrimaryKey` |

A `PUT` sends the full record with the primary key; `PATCH` sends changed columns with the primary key; `DELETE` takes the key from the URL:

```http
DELETE /api/prod/Products?id=abc123
```

Proxy endpoints forward these methods unchanged unless a translation applies.

## MERGE and method translation

The `MERGE` method is the OData name for a partial update and an alias of `PATCH` when listed in `AllowedMethods`. Stored procedures receive `@Method` as `PATCH` for both. The OpenAPI document lists `MERGE` under `additionalOperations`.

Proxy endpoints translate methods for backends that expect other verbs (e.g. classic OData services expecting `MERGE`):

```json
{
  "HttpMethodTranslation": "PUT:MERGE,POST:CREATE"
}
```

Translation targets: `GET`, `POST`, `PUT`, `DELETE`, `PATCH`, `MERGE`, `HEAD`, `OPTIONS`, `QUERY`. Configuration: [Entity configuration](/reference/entity-config).

## Related topics

- [Entity Configuration](/reference/entity-config)
- [OData](/reference/odata)
- [SQL Endpoints](/guide/endpoints-sql)
- [Headers](/reference/headers)
