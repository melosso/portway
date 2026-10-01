---
title: OData Syntax
description: "OData query options for SQL and static endpoints: $select, $filter, $orderby, $top, $skip, $count and $expand"
---

# OData Syntax

SQL endpoints, and static endpoints with `EnableFiltering`, accept OData query options in the URL (GET) or in the request body (QUERY). Queries are translated to parameterized SQL.

| Option | Purpose | Example |
|---|---|---|
| `$select` | Columns to return | `$select=Name,Price` |
| `$filter` | Row condition | `$filter=Price gt 100` |
| `$orderby` | Sort order | `$orderby=Name desc` |
| `$top` | Maximum rows | `$top=10` |
| `$skip` | Rows to skip | `$skip=20` |
| `$count` | Adds `totalCount` | `$count=true` |
| `$expand` | Related to-one entity | `$expand=Category` |

```http
GET /api/prod/Products?$select=ItemCode,Description&$filter=Price gt 50&$orderby=Price desc&$top=10
```

## $select

```
$select=field1,field2
```

Only columns in `AllowedColumns` (public names) can be selected; other names return `400`. Without `$select`, all allowed columns are returned.

## $filter

```
$filter=field operator value
```

Operators, functions and literals: [Filter operations](/reference/filters).

## $orderby

```
$orderby=field [asc|desc][,field [asc|desc]]
```

```http
GET /api/prod/Products?$orderby=Category asc,Price desc,Name asc
```

Ascending is the default.

## $top and $skip

```http
GET /api/prod/Products?$top=10&$skip=10
```

Paging requires a stable `$orderby`. Responses with more rows include `nextLink`. Details: [Sorting & Pagination](/reference/sorting-pagination).

## $count

`$count=true` runs an additional COUNT query with the same `$filter` and adds `totalCount`:

```http
GET /api/prod/Products?$filter=Price gt 100&$top=10&$count=true
```

```json
{
  "success": true,
  "count": 10,
  "totalCount": 342,
  "value": [ "..." ]
}
```

The `count` property is the number of rows in the response; `totalCount` ignores `$top`, `$skip`, `$select` and `$orderby` and is omitted without `$count=true`.

## $expand

```http
GET /api/prod/Products?$expand=Category
```

The relationship is declared in `entity.json` and executed as a SQL `JOIN` on Table and View endpoints. Details: [Expanding Related Data](/reference/expand).

## Response format

```json
{
  "success": true,
  "count": 2,
  "value": [
    { "ItemCode": "PROD001", "Description": "Widget A", "Price": 99.99 },
    { "ItemCode": "PROD002", "Description": "Widget B", "Price": 149.99 }
  ],
  "nextLink": "/api/prod/Products?$top=2&$skip=2"
}
```

| Property | Description |
|---|---|
| `success` | `true` for successful responses |
| `count` | Rows in this response |
| `value` | Rows |
| `nextLink` | Next page URL, or `null` |
| `totalCount` | Total matching rows, with `$count=true` |

A request by id (`/api/prod/Products/PROD001`) returns the row without the envelope.

## URL encoding

| Character | Encoded |
|---|---|
| Space | `%20` |
| `'` | `%27` |
| `&` (in values) | `%26` |
| `+` | `%2B` |

## Errors

Malformed options return `400` with "Invalid OData query. Check $filter, $select, $orderby and $expand syntax."; columns outside `AllowedColumns` in `$select` return `400` with "Selected columns not allowed: …".

## Related topics

- [Filter Operations](/reference/filters)
- [Sorting & Pagination](/reference/sorting-pagination)
- [HTTP Methods](/reference/http-methods)
- [SQL Endpoints](/guide/endpoints-sql)
