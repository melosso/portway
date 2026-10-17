---
title: Endpoint Versions
description: "Release a breaking change as a new endpoint version, move clients to it and retire the old version"
---

# Endpoint Versions

This guide releases a breaking change to an endpoint as `v2`, keeps `v1` running for existing clients and retires `v1` on a fixed date. The example is the SQL endpoint `Inventory/Products`. The same steps apply to Proxy, Composite, Static, Webhook and Files endpoints.

| Version | Change |
|---|---|
| `v1` | Returns `ProductId` and `ProductName` |
| `v2` | Renames `ProductName` to `Name` and adds `Price` |

## 1. Start from the current endpoint

`endpoints/SQL/Inventory/Products/entity.json` is the current endpoint. Without a version folder, this file is `v1`:

```json
{
  "DatabaseObjectName": "Products",
  "DatabaseSchema": "dbo",
  "PrimaryKey": "ProductId",
  "AllowedColumns": ["ProductId", "ProductName"]
}
```

Existing clients call `/api/prod/Inventory/Products`. That URL keeps serving `v1` through every step of this guide.

## 2. Create v2

Create the folder `v2/` inside the endpoint folder and add `v2/entity.json`:

```
endpoints/SQL/Inventory/Products/
├── entity.json
└── v2/
    └── entity.json
```

```json
{
  "DatabaseObjectName": "Products",
  "DatabaseSchema": "dbo",
  "PrimaryKey": "ProductId",
  "AllowedColumns": ["ProductId", "ProductName;Name", "Price"]
}
```

`v2/entity.json` is a complete endpoint. Settings from `v1` are not inherited: copy methods, environments and `Mcp` settings that `v2` needs. The gateway loads the new folder without a restart.

In the console, **New version** on the endpoint page creates `v2/` with a copy of `entity.json` and its sibling files.

## 3. Test v2

```sh
curl -H "Authorization: Bearer $TOKEN" "https://api.example.com/api/prod/v2/Inventory/Products?\$top=1"
```

A token scoped to `Inventory/Products` returns `403` here. That scope grants `v1`. Use a token with `Inventory/Products@v2` or `Inventory/Products*`.

`/docs` displays a version switch for the endpoint. `/docs/openapi/v2/openapi.json` contains the `v2` operations for client generation.

## 4. Move clients to v2

Give each client a token that grants `v2`:

| Client | Token scope |
|---|---|
| Migrated to `v2` | `Inventory/Products@v2` |
| Calls both during the move | `Inventory/Products*` |
| Not migrated | `Inventory/Products` (unchanged) |

Edit the scope under **Tokens** in the console. The change applies to the next request. Scope syntax: [Endpoint scopes](/guide/tokens#endpoint-scopes).

## 5. Announce the end of v1

Add the lifecycle keys to the `v1` file, `endpoints/SQL/Inventory/Products/entity.json`:

```json
{
  "DatabaseObjectName": "Products",
  "DatabaseSchema": "dbo",
  "PrimaryKey": "ProductId",
  "AllowedColumns": ["ProductId", "ProductName"],
  "Deprecated": true,
  "DeprecatedSince": "2026-10-01T00:00:00Z",
  "Sunset": "2027-04-01T00:00:00Z"
}
```

Every `v1` response now includes the retirement date and the successor:

```http
HTTP/1.1 200 OK
Deprecation: @1790812800
Sunset: Thu, 01 Apr 2027 00:00:00 GMT
Link: </api/prod/v2/Inventory/Products>; rel="successor-version"
```

The OpenAPI document marks the `v1` operations deprecated. Check the remaining `v1` traffic on the console **Health** page with the **Version** filter.

## 6. Retire v1

After the sunset date, set `"Enabled": false` in the `v1` file. `v1` requests return `503`, and the `v1` MCP tool is removed. `v2` is unchanged.

The next breaking change follows the same steps with a `v3/` folder.

## Related topics

- [Versions reference](/reference/namespaces#versions): every rule for version folders, routes, scopes and headers
- [Access tokens](/guide/tokens)
- [Instances](/guide/instances): separate Portway instances per path, e.g. `/v1` and `/v2`
