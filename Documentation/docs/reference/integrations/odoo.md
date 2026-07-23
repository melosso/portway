---
title: Odoo Integration
description: "Expose a firewalled Odoo instance through Portway with access control, rate limiting, and traffic logging on top of the JSON-RPC API"
---

# Odoo Integration

Odoo's external API speaks JSON-RPC and XML-RPC. Authentication works differently than most REST APIs: the database name, user, and API key travel inside the request body, not in a header. That changes what the gateway can do for you. Portway cannot inject body content, so it does not hide the Odoo credentials. What it does add is access control, rate limiting, and a traffic log in front of an Odoo instance that stays off the public internet.

## Overview

The integration uses a proxy endpoint to forward JSON-RPC calls to Odoo. Clients authenticate against Portway with a bearer token, then carry their own Odoo credentials in the request body as usual. Portway decides who reaches Odoo at all; Odoo decides what those credentials may do.

::: Note
If you want credentials kept server-side, the AFAS and NocoDB integrations show the header-injection pattern. Odoo's body-carried credentials rule that pattern out.
:::

## Configuration

### Proxy Endpoint

A single endpoint covers the JSON-RPC entry point:

```json [endpoints/Proxy/Odoo/Rpc/entity.json]
{
  "Url": "http://odoo.internal:8069/jsonrpc",
  "Methods": ["POST"],
  "AllowedEnvironments": ["prod"]
}
```

No environment headers are needed. The environment still controls which clients may call the endpoint through `AllowedEnvironments` and token scopes.

### Odoo API Keys

Give each integration its own Odoo user and API key, generated under the user's account security settings. API keys act as the password in RPC calls. Odoo's own access rights then bound what each integration can read and write.

## Usage

Clients send standard JSON-RPC envelopes through the gateway. Authentication resolves the user ID first:

```http
POST /api/prod/Odoo/Rpc
Authorization: Bearer YOUR_PORTWAY_TOKEN
Content-Type: application/json

{
  "jsonrpc": "2.0",
  "method": "call",
  "params": {
    "service": "common",
    "method": "authenticate",
    "args": ["mydb", "integration@company.com", "ODOO_API_KEY", {}]
  }
}
```

Data calls use `execute_kw` with the resolved user ID:

```http
POST /api/prod/Odoo/Rpc
Authorization: Bearer YOUR_PORTWAY_TOKEN
Content-Type: application/json

{
  "jsonrpc": "2.0",
  "method": "call",
  "params": {
    "service": "object",
    "method": "execute_kw",
    "args": [
      "mydb", 2, "ODOO_API_KEY",
      "res.partner", "search_read",
      [[["is_company", "=", true]]],
      { "fields": ["name", "email"], "limit": 25 }
    ]
  }
}
```

## What the gateway adds

The gateway earns its place even without credential injection:

- **Network isolation**: Odoo stays on the internal network; only Portway is reachable from outside
- **Access control**: Portway tokens and environment scoping decide who may reach the RPC endpoint at all
- **Rate limiting**: per-IP and per-token limits protect Odoo from runaway integrations
- **Traffic logging**: every RPC call lands in the [traffic log](/reference/audit) with timing and caller identity

## Things to keep in mind

A few properties of this setup deserve attention before you build on it:

- Odoo credentials pass through the gateway inside request bodies. Body capture in [traffic logging](/reference/audit) is off by default; leave it off for this endpoint, or the log will contain API keys.
- All calls are `POST` to one endpoint, so per-table endpoint splitting does not apply. Odoo's model-level access rights are the tool for narrowing what an integration can touch.
- Responses are JSON-RPC envelopes. Caching applies poorly here, since identical URLs carry different bodies.

## Troubleshooting

RPC integrations fail in characteristic ways, so check these first:

| Symptom | Check |
|---------|-------|
| `401` from Portway | Bearer token valid and scoped to the endpoint and environment |
| Odoo returns `odoo.exceptions.AccessDenied` | Database name, login, and API key in the request body; key not revoked in Odoo |
| Empty `result` on `authenticate` | Wrong database name; Odoo returns `false` rather than an error |
| Access errors on specific models | The integration user's access rights and record rules in Odoo |
