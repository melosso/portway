---
title: SAP Business One Integration
description: "Route SAP Business One Service Layer traffic through Portway, with session login passing through the gateway"
---

# SAP Business One Integration

SAP Business One exposes its data through the [Service Layer](https://help.sap.com/doc/fc2f5477516c404c8bf9ad1315a17238/10.0/en-US/Working_with_SAP_Business_One_Service_Layer.pdf){target="_blank" rel="noopener"}, an OData-style REST API. Authentication is session-based: clients log in, receive a session cookie, and the session expires after roughly 30 minutes. 

Portway cannot perform that login on the client's behalf, so this integration uses a passthrough pattern. Clients log in through the gateway and hold their own session. Portway adds access control, rate limiting, and a traffic log in front of a Service Layer that stays internal.

## Overview

A proxy endpoint forwards Service Layer requests, including the login call. Cookies pass through the gateway in both directions, so the session works exactly as it would against the Service Layer directly. Portway decides who reaches the Service Layer at all; SAP B1 user accounts decide what each session may do.

::: Note
If you want credentials kept server-side, the AFAS and NocoDB integrations show the header-injection pattern. The Service Layer's session login rules that pattern out.
:::

## Configuration

### Proxy Endpoint

One endpoint covers the Service Layer root:

```json [endpoints/Proxy/SapB1/ServiceLayer/entity.json]
{
  "Url": "https://sap-server:50000/b1s/v1",
  "Methods": ["GET", "POST", "PATCH", "DELETE"],
  "AllowedEnvironments": ["prod"]
}
```

Paths after the endpoint name are appended to the target URL, so one definition serves `Login`, `Items`, `Orders`, and the rest of the Service Layer surface.

### Service Accounts

Give each integration its own SAP B1 user with the minimum authorizations it needs. Session activity in SAP then stays attributable per integration, and Portway's traffic log shows the same split on the gateway side.

## Usage

Clients start with a login through the gateway:

```http
POST /api/prod/SapB1/ServiceLayer/Login
Authorization: Bearer YOUR_PORTWAY_TOKEN
Content-Type: application/json

{
  "CompanyDB": "SBODEMOUS",
  "UserName": "integration01",
  "Password": "SAP_B1_PASSWORD"
}
```

The response sets the `B1SESSION` cookie. Subsequent calls carry it, alongside the Portway token:

```http
GET /api/prod/SapB1/ServiceLayer/Items?$filter=ItemsGroupCode eq 100&$top=20
Authorization: Bearer YOUR_PORTWAY_TOKEN
Cookie: B1SESSION=...
```

Sessions expire after about 30 minutes. Clients re-login through the same endpoint when they receive a `401` from the Service Layer.

## What the gateway adds

Even with sessions handled client-side, the gateway does real work:

- **Network isolation**: the Service Layer stays internal; only Portway is exposed
- **Access control**: Portway tokens decide who may reach the Service Layer at all
- **Rate limiting**: per-IP and per-token limits protect SAP from misbehaving clients
- **Traffic logging and metrics**: every call lands in the [traffic log](/reference/audit) and the `portway.endpoint` metrics dimension

## Things to keep in mind

A few properties of the passthrough pattern deserve attention:

- SAP credentials pass through the gateway in the login body. Body capture in [traffic logging](/reference/audit) is off by default; leave it off for this endpoint.
- The Service Layer speaks its own OData dialect natively, so `$filter`, `$select`, and `$top` work as documented by SAP. Portway passes them through without translation.
- The whole Service Layer surface sits behind one endpoint. Narrowing what an integration can do happens through SAP B1 authorizations, not endpoint definitions.

## Troubleshooting

Session handling causes most of the friction here, so start with it:

| Symptom | Check |
|---------|-------|
| `401` from the Service Layer mid-run | Session expired; re-login and retry |
| Login succeeds, next call `401` | `B1SESSION` cookie forwarded by the client on every request |
| `401` from Portway | Bearer token valid and scoped to the endpoint and environment |
| Connection refused | Service Layer running on the SAP server; port 50000 reachable from the Portway host |
