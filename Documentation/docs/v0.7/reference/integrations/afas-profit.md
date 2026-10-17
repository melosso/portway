---
title: AFAS Profit Integration
description: "Proxy endpoints for AFAS Profit GetConnectors and UpdateConnectors with the AfasToken stored in the environment"
---

# AFAS Profit Integration

AFAS Profit exposes [GetConnectors](https://help.afas.nl/help/NL/SE/App_Cnr_Rest_GET.htm){target="_blank" rel="noopener"} for reads and [UpdateConnectors](https://help.afas.nl/help/NL/SE/App_Cnr_Rest_Update.htm){target="_blank" rel="noopener"} for writes, authenticated with an app connector token in the `Authorization` header. Portway adds that header from the environment; clients use Portway tokens.

## Environment

The app connector token is an XML fragment, base64-encoded into the header value:

```json [environments/prod/settings.json]
{
  "ServerName": "YOUR-SERVER",
  "Headers": {
    "Authorization": "AfasToken PHRva2VuPjx2ZXJzaW9uPjE8L3ZlcnNpb24+..."
  }
}
```

The environment `Authorization` header replaces the client's. A second environment with its own token connects the same endpoints to an AFAS test member.

## Endpoints

```json [endpoints/Proxy/Afas/Articles/entity.json]
{
  "Url": "https://12345.rest.afas.online/ProfitServices/connectors/Profit_Article",
  "Methods": ["GET"],
  "AllowedEnvironments": ["prod"]
}
```

```json [endpoints/Proxy/Afas/SalesOrders/entity.json]
{
  "Url": "https://12345.rest.afas.online/ProfitServices/connectors/FbSales",
  "Methods": ["POST", "PUT", "DELETE"],
  "AllowedEnvironments": ["prod"]
}
```

The AFAS member number replaces `12345`.

## Requests

AFAS query parameters are passed through:

```http
GET /api/prod/Afas/Articles?skip=0&take=100&filterfieldids=ItemCode&filtervalues=A0001
Authorization: Bearer YOUR_PORTWAY_TOKEN
```

```http
POST /api/prod/Afas/SalesOrders
Authorization: Bearer YOUR_PORTWAY_TOKEN
Content-Type: application/json

{
  "FbSales": {
    "Element": {
      "Fields": { "OrDa": "2026-07-23", "DbId": "10001" }
    }
  }
}
```

## Limitations

- Filtering and paging use AFAS parameters (`skip`, `take`, `filterfieldids`, `filtervalues`, `operatortypes`), not OData.
- Fields are defined by the connector in AFAS.
- The app connector should grant only the connectors the integration uses.

## Troubleshooting

| Symptom | Check |
|---|---|
| `401` from AFAS | `AfasToken` value, token validity in the app connector, base64 encoding |
| `401` from Portway | Portway token and its scope |
| Connector not found | Connector id in `Url`; member number |
| Filters ignored | AFAS parameters instead of `$filter` |
