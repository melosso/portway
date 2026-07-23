---
title: Teable Integration
description: "Route Teable's REST API through Portway with environment-scoped tokens and a clean authentication handoff"
---

# Teable Integration

Teable is a spreadsheet-style database built on Postgres. Its REST API authenticates through personal access tokens. Portway can act as a gateway in front of that API. The Teable token stays on the server, and your integrations authenticate against Portway instead.

One thing to understand up front: Teable expects its token in the `Authorization` header. Portway's own tokens use that same header. This page walks through the configuration that avoids the conflict.

## Overview

The integration uses Portway's proxy endpoints to forward requests to Teable's record API. The Teable personal access token is injected from the environment configuration.

There is one catch. Portway forwards the client's `Authorization` header to upstream services. To keep that header free for the Teable token, clients authenticate with an `X-API-Key` header instead of a bearer token. The environment's custom authentication makes that switch.

## Configuration

### Environment Settings

Two things happen in the environment file. The Teable token goes into the outbound headers. Inbound authentication switches to an API key header, which keeps the `Authorization` header clear:

```json [environments/prod/settings.json]
{
  "ServerName": "YOUR-SERVER",
  "Headers": {
    "Authorization": "Bearer teable_YOUR_TOKEN"
  },
  "Authentication": {
    "Enabled": true,
    "OverrideGlobalToken": true,
    "Methods": [
      {
        "Type": "ApiKey",
        "Name": "X-API-Key",
        "Value": "YOUR_CLIENT_API_KEY",
        "In": "Header"
      }
    ]
  }
}
```

With `OverrideGlobalToken` set to `true`, clients authenticate with the `X-API-Key` header only. They send no `Authorization` header of their own. The environment's Teable token is then the only value that reaches Teable. The [Environment Authentication reference](/reference/environment-auth) covers the available methods in more detail.

::: Note 
Skipping the `X-API-Key` setup breaks the integration. A client's Portway bearer token would be forwarded alongside the environment's Teable token, both in the `Authorization` header. Teable rejects that malformed header. Also note that `OverrideGlobalToken` applies to the whole environment, so give Teable a dedicated environment.
:::

You can generate a personal access token in Teable under your account's token settings. Tokens carry the `teable_` prefix.

### Proxy Endpoint

Each Teable table gets an endpoint file pointing at its record collection:

```json [endpoints/Proxy/Teable/Orders/entity.json]
{
  "Url": "http://teable:3000/api/table/YOUR_TABLE_ID/record",
  "Methods": ["GET", "POST", "PATCH", "DELETE"],
  "AllowedEnvironments": ["prod"]
}
```

The table ID is visible in the Teable URL when you open the table. Trim `Methods` to what the integration needs; a read-only endpoint only needs `["GET"]`.

## Usage

Clients authenticate with the environment's API key. Query parameters pass through the proxy untouched, so Teable's own parameters apply:

```http
GET /api/prod/Teable/Orders?take=25&skip=0
X-API-Key: YOUR_CLIENT_API_KEY
```

Creating a record follows Teable's API shape:

```http
POST /api/prod/Teable/Orders
X-API-Key: YOUR_CLIENT_API_KEY
Content-Type: application/json

{
  "records": [
    {
      "fields": {
        "Customer": "60093",
        "Status": "Open"
      }
    }
  ]
}
```

Filtering, sorting, and field selection follow Teable's API reference. Portway does not translate these parameters.

## What the gateway adds

Beyond solving the authentication handoff, the gateway earns its place in the chain:

- **Credential isolation**: the Teable token never leaves the server; client API keys are scoped per environment
- **Rate limiting**: per-IP limits protect Teable from misbehaving clients
- **Caching**: JSON responses are cacheable for read-heavy workloads
- **Traffic logging and metrics**: requests appear in the [traffic log](/reference/audit) and the `portway.endpoint` metrics dimension

## Things to keep in mind

A few properties of the proxy pattern are worth knowing before you build on it:

- Query syntax is Teable's own, not OData. OData translation applies to [SQL endpoints](/guide/endpoints-sql) only.
- Proxy endpoints expose the table's full field surface; there is no column allowlisting.
- Teable stores its data in Postgres. A [SQL endpoint](/guide/endpoints-sql) against that database gives you curated, OData-queryable read access. Writes through that route bypass Teable's field logic and permissions, so keep it to reporting.

## Troubleshooting

Most problems here trace back to the token handoff, so start there:

| Symptom | Check |
|---------|-------|
| `401` from Teable | Environment `Authorization` header value and `teable_` token validity; confirm clients are not sending their own `Authorization` header |
| `401` from Portway | `X-API-Key` header present and matching the environment's configured value; `Authentication.Enabled` set to `true` |
| Other endpoints in the environment now reject bearer tokens | `OverrideGlobalToken: true` applies to the whole environment. Give Teable a dedicated environment if that is unwanted |
| Filters ignored | Query parameters follow Teable's API reference, not OData `$filter` |
