---
title: Sorting & Pagination
description: "Sorting with $orderby and paging with $top, $skip and nextLink"
---

# Sorting & Pagination

## Sorting with $orderby

```
$orderby=field [asc|desc][,field [asc|desc]]
```

```http
GET /api/prod/Products?$orderby=Name
GET /api/prod/Products?$orderby=Price desc
GET /api/prod/Orders?$orderby=CustomerCode,OrderDate desc,Priority asc
```

Ascending is the default; fields are applied in the listed order, before paging. Only columns in `AllowedColumns` can be sorted on. Null placement follows the database.

## Paging with $top and $skip

| Option | Default | Behavior |
|---|---|---|
| `$top` | `10` | Maximum rows; limited to the endpoint's `MaxPageSize` when set |
| `$skip` | `0` | Rows skipped before the first returned row |

```http
GET /api/prod/Orders?$top=25&$skip=25&$orderby=OrderDate desc
```

Paging requires an `$orderby` that is stable; add a unique column as the last sort key:

```http
GET /api/prod/Products?$orderby=Price desc,ItemCode&$top=20&$skip=0
```

## nextLink

When more rows exist, the response includes `nextLink` with the next page URL; the last page has `"nextLink": null`. Response format: [OData](/reference/odata#response-format).

The total number of matching rows is returned with `$count=true` ([OData](/reference/odata#count)).

## Deep pages

Large `$skip` values make the database read and discard the skipped rows. Keyset paging filters on the last returned key instead:

```http
# Offset
GET /api/prod/Products?$orderby=ItemCode&$top=20&$skip=10000

# Keyset
GET /api/prod/Products?$filter=ItemCode gt 'PROD10000'&$orderby=ItemCode&$top=20
```

## Errors

Malformed `$orderby` and unknown columns return `400` with "Invalid OData query. Check $filter, $select, $orderby and $expand syntax.".

## Related topics

- [OData Syntax](/reference/odata)
- [Filter Operations](/reference/filters)
- [SQL Endpoints](/guide/endpoints-sql)
