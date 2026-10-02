---
title: Filter Operations
description: "OData $filter operators, functions and literal formats supported by SQL and static endpoints"
---

# Filter Operations

The `$filter` query option selects rows on SQL and filterable static endpoints. Filters are translated to parameterized SQL.

```
$filter=expression
```

## Comparison operators

| Operator | Meaning | Example |
|---|---|---|
| `eq` | Equal | `$filter=Status eq 'Active'` |
| `ne` | Not equal | `$filter=Status ne 'Closed'` |
| `gt` | Greater than | `$filter=Price gt 100` |
| `ge` | Greater than or equal | `$filter=Price ge 100` |
| `lt` | Less than | `$filter=Price lt 100` |
| `le` | Less than or equal | `$filter=Price le 100` |

```http
GET /api/500/Products?$filter=ItemCode eq 'PROD001'
GET /api/500/Products?$filter=Price gt 50.00
GET /api/500/Orders?$filter=OrderDate gt 2024-01-01
GET /api/500/Customers?$filter=Name gt 'M'
```

## Logical operators

| Operator | Meaning | Example |
|---|---|---|
| `and` | Both conditions | `$filter=Price gt 100 and Status eq 'Active'` |
| `or` | Either condition | `$filter=Status eq 'New' or Status eq 'Pending'` |
| `not` | Negation | `$filter=not contains(Description,'test')` |

The `and` operator binds stronger than `or`; parentheses override the order:

```http
# (Price gt 100 and Category eq 'A') or Category eq 'B'
GET /api/500/Products?$filter=Price gt 100 and Category eq 'A' or Category eq 'B'

# Price gt 100 and (Category eq 'A' or Category eq 'B')
GET /api/500/Products?$filter=Price gt 100 and (Category eq 'A' or Category eq 'B')
```

## String functions

| Function | Meaning | Example |
|---|---|---|
| `contains(field,value)` | Contains | `$filter=contains(Description,'widget')` |
| `startswith(field,value)` | Starts with | `$filter=startswith(Name,'A')` |
| `endswith(field,value)` | Ends with | `$filter=endswith(Email,'.com')` |

```http
GET /api/500/Products?$filter=contains(Description,'premium') and not contains(Description,'refurbished')
GET /api/500/Products?$filter=contains(Name,'widget') or contains(Description,'widget')
```

Case sensitivity follows the database collation. There is no wildcard or regular expression syntax. `startswith` can use an index; `contains` and `endswith` scan.

## Literals

| Type | Format | Example |
|---|---|---|
| String | Single quotes; `''` escapes a quote | `Name eq 'It''s here'` |
| Number | Unquoted, decimal or scientific | `Price eq 99.99`, `Balance gt -100.50`, `Value lt 1.5e6` |
| Date | ISO 8601 date | `OrderDate eq 2024-01-15` |
| Date and time | ISO 8601 with offset | `CreatedAt gt 2024-01-15T14:30:00Z` |
| Boolean | `true`, `false` | `IsActive eq true` |
| Null | `null` | `AssignedTo eq null`, `CompletedDate ne null` |

## Column names

Filters use the public names from `AllowedColumns` (aliases included). Tenant restrictions are applied outside the filter and cannot be widened by it ([Tenant headers](/guide/security#tenant-headers)).

## Errors

| Response | Cause |
|---|---|
| `400` "Invalid OData query. Check $filter, $select, $orderby and $expand syntax." | Malformed expression, unknown function or type mismatch |
| `400` "Selected columns not allowed: …" | `$select` names a column outside `AllowedColumns` |

## Unsupported

| Feature | Alternative |
|---|---|
| Arithmetic (`add`, `mul`, …) | Precomputed columns or a view |
| Regular expressions | `contains`, `startswith`, `endswith` |

## Related topics

- [OData Syntax](/reference/odata)
- [Sorting & Pagination](/reference/sorting-pagination)
- [SQL Endpoints](/guide/endpoints-sql)
- [API Overview](/reference/)
