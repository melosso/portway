---
title: Tenant Headers
description: "Restrict tokens to specific customers with request headers mapped to columns, parameters, upstream headers and folders"
---

# Tenant Headers

Tenant headers restrict a token to the rows, upstream records and files of specific customers. `Tenancy` on the endpoint maps each header to its target; `AllowedTenants` on the token lists the permitted values per header. The request header selects one permitted value and cannot add a value.

```json [endpoints/SQL/Sales/Orders/entity.json]
{
  "DatabaseObjectName": "Orders",
  "AllowedColumns": ["Id", "Total"],
  "Tenancy": { "X-Company-Id": "CompanyId" }
}
```

```json [token AllowedTenants]
{ "X-Company-Id": ["ACME", "GLOBEX"] }
```

Header names are configurable (e.g. `X-Company-Id`, `X-Client-Id`, `Administratie`). Reserved names are rejected: `Authorization`, `Cookie`, `Host`, `Origin`, `Content-*`, `X-Forwarded-*` and hop-by-hop headers. Values match `[A-Za-z0-9][A-Za-z0-9_.-]{0,63}`; `*` in `AllowedTenants` accepts any value of that form.

| Request | Result |
|---|---|
| Endpoint has no `Tenancy` | Request unchanged |
| No bearer token (environment authentication) | `403` |
| Token holds no value for the header | `403` |
| Header absent, token holds one value | That value |
| Header absent, token holds several values or `*` | `400` |
| Header holds a value the token holds | That value |
| Header holds another value | `403` |
| Header repeated or malformed | `400` |

The `Tenancy` value depends on the endpoint type:

| Endpoint type | `Tenancy` value | Behavior |
|---|---|---|
| SQL table or view | Column | Reads and `$count` include `Column = value`, combined with `AND` outside the client `$filter`. Inserts set the column; updates and deletes require a match; the column cannot be changed. Rows of other tenants return `404`. |
| SQL table-valued function | Function parameter | Receives the tenant value; client values are ignored. |
| SQL stored procedure | Procedure parameter | `@{value}` receives the tenant value after the payload parameters. The procedure enforces it. |
| Proxy | Upstream header | Set to the tenant value. Client copies of the inbound and upstream headers are removed. |
| File | Ignored | `BaseDirectory` contains a `{Header}` placeholder per tenant header. Uploads, downloads, deletes and listings are restricted to the resolved directory. |

Tenancy is not supported on static, webhook or composite endpoints, on composite step targets or on `$expand` targets. Endpoints with invalid `Tenancy` are not loaded and cannot be saved in the console. The OpenAPI document lists tenant headers as optional header parameters; MCP tools accept them in the `tenants` argument.

## Related topics

- [Access Tokens](/guide/tokens)
- [Security](/guide/security)
- [Entity Configuration](/reference/entity-config)
