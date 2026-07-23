---
title: NocoDB Integration
description: "Put Portway in front of NocoDB's REST API to add token management, rate limiting, and caching to your no-code database"
---

# NocoDB Integration

NocoDB turns any Postgres or MySQL database into a spreadsheet-style workspace with a REST API on top. Portway can act as a gateway in front of that API. Your external applications talk to Portway with their own scoped tokens. The NocoDB API token stays on the server. You also gain rate limiting, response caching, and traffic logging in one place.

## Overview

The integration uses Portway's proxy endpoints to forward requests to NocoDB's v2 REST API. NocoDB authenticates callers with an `xc-token` header. Portway injects that header from the environment configuration on every forwarded request. Clients never see the NocoDB token; they authenticate against Portway as usual.

## Configuration

### Environment Headers

NocoDB expects its API token in the `xc-token` header. Generate one in NocoDB under **Account Settings → API Tokens**. Then place it in the environment settings, where it gets attached to every proxied request:

```json [environments/prod/settings.json]
{
  "ServerName": "YOUR-SERVER",
  "Headers": {
    "xc-token": "YOUR_NOCODB_API_TOKEN"
  }
}
```

The token lives in the environment, not the endpoint. That means the same endpoint definitions can serve different NocoDB instances per environment, each with its own token.

### Proxy Endpoint

Each exposed NocoDB table gets its own endpoint file. The URL targets the table's records collection in the v2 API:

```json [endpoints/Proxy/Nocodb/Orders/entity.json]
{
  "Url": "http://nocodb:8080/api/v2/tables/YOUR_TABLE_ID/records",
  "Methods": ["GET", "POST", "PATCH", "DELETE"],
  "AllowedEnvironments": ["prod", "dev"]
}
```

The table ID is visible in the NocoDB URL when you open the table. Trim `Methods` to what the integration needs; a read-only endpoint only needs `["GET"]`.

## Usage

Clients call the endpoint with their regular Portway bearer token. Query parameters pass through untouched, so NocoDB's own query dialect applies:

```http
GET /api/prod/Nocodb/Orders?limit=25&where=(Status,eq,Open)&sort=-CreatedAt
Authorization: Bearer YOUR_PORTWAY_TOKEN
```

Creating a record is a plain POST with the field values:

```http
POST /api/prod/Nocodb/Orders
Authorization: Bearer YOUR_PORTWAY_TOKEN
Content-Type: application/json

{
  "Customer": "60093",
  "Status": "Open",
  "Amount": 1250.00
}
```

Updates and deletes follow NocoDB's v2 convention of carrying the record `Id` in the request body:

```http
PATCH /api/prod/Nocodb/Orders
Authorization: Bearer YOUR_PORTWAY_TOKEN
Content-Type: application/json

{
  "Id": 42,
  "Status": "Shipped"
}
```

## What the gateway adds

Routing NocoDB through Portway is not just indirection. Each layer does useful work on the way through:

- **Token scoping**: Portway tokens control who reaches which endpoints and environments; the NocoDB token stays server-side
- **Rate limiting**: per-IP and per-token limits protect NocoDB from runaway clients
- **Caching**: JSON responses are cacheable, which softens repeated dashboard-style reads
- **Traffic logging and metrics**: every request shows up in the [traffic log](/reference/audit) and the `portway.endpoint` metrics dimension

## Things to keep in mind

A few properties of the proxy pattern are worth knowing before you build on it:

- Filtering, sorting, and pagination use NocoDB's query syntax (`where`, `sort`, `limit`, `offset`), not OData. OData translation applies to SQL endpoints only.
- Proxy endpoints expose the table's full field surface; there is no column allowlisting. For a curated column set, use a [SQL endpoint](/guide/endpoints-sql) against the underlying database.
- Writes through the underlying database bypass NocoDB's formulas, webhooks, and permissions. Keep that route to read-heavy reporting.

## Troubleshooting

When something misbehaves, these are the usual suspects:

| Symptom | Check |
|---------|-------|
| `401` from NocoDB behind a `200`-healthy gateway | `xc-token` value in the environment headers; token not expired or revoked in NocoDB |
| Empty result where data exists | Table ID in the endpoint URL; the token's workspace access in NocoDB |
| Filters ignored | Query syntax is NocoDB's own (`where=(Field,eq,Value)`), not OData `$filter` |
| Stale data | Response caching. Tune or disable per endpoint via [caching settings](/reference/app-settings#caching) |
