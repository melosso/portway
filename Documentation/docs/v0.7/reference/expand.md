---
title: Expanding Related Data
description: "To-one relationships in entity.json, joined into SQL Table and View responses with OData $expand"
---

# Expanding Related Data

A relationship declared in `entity.json` makes the related row available through `$expand`. The relationship is executed as a SQL `JOIN` on every supported provider, with the endpoint's authentication, environment rules and column allowlists.

## When it applies

Portway processes `$expand` on SQL Table and View endpoints only:

| Endpoint type | `$expand` | Behaviour |
|---|:---:|---|
| SQL Table | ✅ | Portway emits the JOIN |
| SQL View | ✅ | Same as Table |
| SQL TVF | ❌ | Returns `400`; a table-valued function call cannot include a JOIN |
| Proxy / Composite | ➡️ | Forwarded unchanged to the upstream |
| File / Static | n/a | Not SQL |

## Declaring a relationship

Each `Relationships` entry names a navigation and a target SQL endpoint; the target's schema, table and `AllowedColumns` apply:

```json
{
  "DatabaseObjectName": "Items",
  "DatabaseSchema": "dbo",
  "DatabaseObjectType": "Table",
  "PrimaryKey": "ItemCode",
  "AllowedColumns": [
    "ItemCode;ProductNumber",
    "Description;Description",
    "Assortment;AssortmentID"
  ],
  "AllowedMethods": ["GET"],
  "Relationships": [
    {
      "Name": "Category",
      "Target": "Assortments",
      "LocalColumn": "Assortment",
      "TargetColumn": "AssortmentID",
      "Multiplicity": "ToOne"
    }
  ]
}
```

| Field | Meaning |
|---|---|
| `Name` | Navigation name in `$expand` and the response |
| `Target` | Target SQL endpoint, optionally namespaced (e.g. `Product/Assortments`) |
| `LocalColumn` | Foreign key column on this endpoint |
| `TargetColumn` | Matching column on the target, usually its primary key |
| `Multiplicity` | `ToOne` (default); to-many is not supported |

Fields are validated as identifiers at load. An unresolved `Target` is logged as a configuration error and the expand returns `400`.

## Making a request

```http
GET /api/prod/Products?$expand=Category&$filter=AssortmentID eq 10
```

The `Category` navigation joins `Assortments` on `Assortment = AssortmentID`; the related row is nested under `Category`:

```json
{
  "ProductNumber": "A-100",
  "Description": "Widget",
  "AssortmentID": 10,
  "Category": {
    "AssortmentID": 10,
    "Name": "Tools"
  }
}
```

Only the target's `AllowedColumns` are returned. Filters, selection, ordering and paging apply to the base entity, including filters on the foreign key.

## Join behavior

::: danger The target column must be unique
`TargetColumn` is not checked for uniqueness. A repeated value returns one copy of the base record per match. Use the primary key, or check another column:

```sql [Verify your configuration:]
SELECT TargetColumn, COUNT(*)
FROM YourTargetTable
GROUP BY TargetColumn
HAVING COUNT(*) > 1;
```

Any returned row is a duplicate key value.
:::

To-one navigations use an `INNER JOIN`; base rows without a match are omitted. The `$count` value counts base rows for the `$filter` without the join.

## Limits

| Request | Result |
|---|---|
| To-many relationship | Rejected at load |
| Table-valued function endpoint | `400` |
| Nested options, e.g. `$expand=Category($select=Name)` | `400` |
| Endpoint without `AllowedColumns` | `400` |
| Unknown navigation | `400`, naming the navigation |
| Target endpoint with `Tenancy` | `400` |

## Related topics

- [OData Syntax](/reference/odata)
- [HTTP Methods](/reference/http-methods)
- [Entity Configuration](/reference/entity-config)
- [SQL Endpoints](/guide/endpoints-sql)
