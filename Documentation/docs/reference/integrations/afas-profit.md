---
title: AFAS Profit Integration
description: "Front AFAS Profit's REST connectors with Portway, keeping the AfasToken on the server and your integrations on scoped gateway tokens"
---

# AFAS Profit Integration

AFAS Profit exposes its data through REST connectors: [GetConnectors](https://help.afas.nl/help/NL/SE/App_Cnr_Rest_GET.htm){target="_blank" rel="noopener"} for reading, [UpdateConnectors](https://help.afas.nl/help/NL/SE/App_Cnr_Rest_Update.htm){target="_blank" rel="noopener"} for writing. Authentication runs on a long-lived app token in a static header. ExpoThat makes AFAS a natural fit for Portway's proxy pattern. The token stays on the server, and your integrations authenticate against Portway with their own scoped tokens.

## Overview

The integration uses Portway's proxy endpoints to forward requests to the AFAS REST services. AFAS expects an `Authorization: AfasToken <base64>` header on every call. Portway injects that header from the environment configuration. Clients never see the AFAS token; they authenticate against Portway as usual.

## Configuration

### Environment Headers

Create an app connector in AFAS under the app connector settings and copy its token. The token is an XML fragment that gets base64-encoded into the header value:

```json [environments/prod/settings.json]
{
  "ServerName": "YOUR-SERVER",
  "Headers": {
    "Authorization": "AfasToken PHRva2VuPjx2ZXJzaW9uPjE8L3ZlcnNpb24+..."
  }
}
```

The token lives in the environment, not the endpoint. A separate environment can point the same endpoint definitions at your AFAS test member with its own token.

::: Note About authorization
AFAS puts its token in the `Authorization` header, the same header Portway's bearer tokens use. Portway forwards the client's `Authorization` header upstream, so the two collide. Use the same solution as the [Teable integration](/reference/integrations/teable): give the environment a custom `X-API-Key` authentication method with `OverrideGlobalToken` set to `true`. Clients then send no `Authorization` header of their own.
::: 

### Proxy Endpoints

Each connector gets its own endpoint file. A GetConnector is read-only, so it only needs `GET`:

```json [endpoints/Proxy/Afas/Articles/entity.json]
{
  "Url": "https://12345.rest.afas.online/ProfitServices/connectors/Profit_Article",
  "Methods": ["GET"],
  "AllowedEnvironments": ["prod"]
}
```

UpdateConnectors take writes:

```json [endpoints/Proxy/Afas/SalesOrders/entity.json]
{
  "Url": "https://12345.rest.afas.online/ProfitServices/connectors/FbSales",
  "Methods": ["POST", "PUT", "DELETE"],
  "AllowedEnvironments": ["prod"]
}
```

Replace `12345` with your AFAS member number.

## Usage

Clients authenticate with the environment's API key. AFAS query parameters pass through the proxy untouched:

```http
GET /api/prod/Afas/Articles?skip=0&take=100&filterfieldids=ItemCode&filtervalues=A0001
X-API-Key: YOUR_CLIENT_API_KEY
```

Writes follow the UpdateConnector's JSON schema:

```http
POST /api/prod/Afas/SalesOrders
X-API-Key: YOUR_CLIENT_API_KEY
Content-Type: application/json

{
  "FbSales": {
    "Element": {
      "Fields": {
        "OrDa": "2026-07-23",
        "DbId": "10001"
      }
    }
  }
}
```

## What the gateway adds

Routing AFAS through Portway does useful work at every layer:

- **Credential isolation**: the AFAS app token never leaves the server; client keys are scoped per environment
- **Rate limiting**: per-IP limits protect your AFAS member from runaway clients
- **Caching**: GetConnector responses are cacheable, which softens repeated reads on slow connectors
- **Traffic logging and metrics**: requests appear in the [traffic log](/reference/audit) and the `portway.endpoint` metrics dimension

## Things to keep in mind

A few properties of the pattern are worth knowing before you build on it:

- Filtering and paging use AFAS parameters (`skip`, `take`, `filterfieldids`, `filtervalues`, `operatortypes`), not OData.
- The endpoint exposes whatever fields the connector definition in AFAS exposes. Field curation happens in AFAS, not in Portway.
- App connector tokens are powerful. Scope the connector in AFAS to the minimum set of Get and UpdateConnectors the integration needs.

## Troubleshooting

Most problems here trace back to the token or the connector definition, so start there:

| Symptom | Check |
|---------|-------|
| `401` from AFAS | `AfasToken` header value; token still valid in the AFAS app connector; base64 encoding intact |
| `401` from Portway | `X-API-Key` header present and matching the environment's configured value |
| Connector not found | Connector ID in the endpoint `Url` matches the AFAS definition exactly; member number correct |
| Filters ignored | Parameters follow AFAS conventions (`filterfieldids`/`filtervalues`), not OData `$filter` |
