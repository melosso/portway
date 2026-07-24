---
title: Expanding Related Data
description: "Declare a relationship once in entity.json and let readers pull related rows in a single request with OData $expand"
---

# Expanding Related Data

`$expand` lets a reader pull related rows alongside the main record in one request. You declare the relationship once in `entity.json`, and every client gets the joined data under the same authentication, the same environment gates and the same column allowlist as the rest of the endpoint. There is no per-endpoint code and no ORM: Portway turns the relationship into a SQL `JOIN` for you, on any supported dialect.

This is the one join operations teams keep hand-rolling. Portway exposes it directly, and it says no, out loud, to the shapes it cannot serve safely yet.

## When it applies

`$expand` is handled by Portway only on **SQL Table and View** endpoints. Other endpoint types either cannot join or own the semantics themselves:

| Endpoint type | `$expand` | Behaviour |
|---|:---:|---|
| SQL Table | ✅ | Portway emits the JOIN |
| SQL View | ✅ | Same path as Table; a view is just a queryable object |
| SQL TVF | ❌ | Returns `400`; a table-valued function cannot carry the JOIN |
| Proxy / Composite | ➡️ | The query string passes through untouched; the upstream owns `$expand` |
| File / Static | n/a | Not SQL |

For a Proxy endpoint, `?$expand=Lines` reaches the upstream service exactly as written. Portway never parses, validates or strips it, so an upstream that implements `$expand` natively keeps working.

## Declaring a relationship

Add a `Relationships` array to the SQL endpoint's `entity.json`. Each entry names a navigation and points at another registered SQL endpoint by name (target-by-name), so the target's schema, table and `AllowedColumns` are reused rather than repeated:

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
| `Name` | The navigation name used in `$expand` and as the nested response key |
| `Target` | The registered SQL endpoint the navigation points at (may be namespaced, for example `Product/Assortments`) |
| `LocalColumn` | The foreign key column on this endpoint (the side that holds the key) |
| `TargetColumn` | The matching column on the target, usually its primary key |
| `Multiplicity` | `ToOne` (the default). To-many is not supported yet |

Every field is validated as a plain identifier when the endpoint loads. If `Target` does not resolve to a registered SQL endpoint, the endpoint summary logs a configuration error and the expand is refused at request time.

## Making a request

Name the navigation in `$expand`:

```http
GET /api/prod/Products?$expand=Category&$filter=AssortmentID eq 10
```

Portway joins `Assortments` to `Items` on `Assortment = AssortmentID` and returns each product with its category nested under the navigation name:

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

Only the target endpoint's `AllowedColumns` are joinable. A column that the target does not expose is never selectable through the expand, so an allowlist stays an allowlist across the join.

`$filter`, `$select`, `$orderby` and paging keep working on the base entity while you expand, including filters on the foreign key column itself.

## What it refuses, and why

Portway states its limits rather than returning a wrong result:

* **To-one only.** A navigation from the foreign key holder to a single related row. To-many is rejected when the endpoint loads.
* **Table and View only.** `$expand` on a table-valued function returns `400`; the function call has no place to carry a JOIN.
* **No nested options.** `$expand=Category($select=Name)` returns `400`. Expand the navigation whole for now.
* **An allowlist is required.** The endpoint must declare `AllowedColumns` so its own columns survive the join. Without one, `$expand` returns `400`.
* **Unknown navigations are rejected.** `$expand=Something` that is not a configured relationship returns `400` naming the navigation.

## Related Topics

- [OData Syntax](/reference/odata): the query options shared by GET and QUERY
- [HTTP Methods](/reference/http-methods): where `$expand` fits among the read verbs
- [Entity Configuration](/reference/entity-config): the rest of `entity.json`
- [SQL Endpoints Guide](/guide/endpoints-sql): endpoint types and write strategies
