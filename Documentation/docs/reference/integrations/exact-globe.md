---
title: Exact Globe+ Integration
description: "Proxy and composite endpoints for the Exact Globe+ REST services"
---

# Exact Globe+ Integration

Exact Globe+ (formerly Globe Next) REST services are exposed through proxy endpoints. Environment headers select the Globe+ database and server.

:::info
Globe+ uses Windows (NTLM) authentication. Under IIS, the Application Pool identity is a domain account with Globe+ access.
:::

## Environment

```json [environments/500/settings.json]
{
  "ServerName": "YOUR-SERVER",
  "ConnectionString": "Server=YOUR-SERVER;Database=500;Trusted_Connection=True;",
  "Headers": {
    "DatabaseName": "500",
    "ServerName": "YOUR-SERVER",
    "Origin": "Portway"
  }
}
```

| Header | Value |
|---|---|
| `DatabaseName` | Globe+ administration, e.g. `500` |
| `ServerName` | Globe+ server |

## Proxy endpoints

```json [endpoints/Proxy/Account/entity.json]
{
  "Url": "http://localhost:8020/services/Exact.Entity.REST.EG/Account",
  "Methods": ["GET", "POST", "PUT", "DELETE"],
  "SupportsOData": true,
  "AllowedEnvironments": ["500", "700"]
}
```

Globe+ URLs in responses are rewritten to the Portway URL, e.g. `http://localhost:8020/services/Exact.Entity.REST.EG/Account(guid'123')` to `https://api.company.com/api/500/Account(guid'123')`. Behind a TLS-terminating proxy, the scheme is taken from `X-Forwarded-Proto` only when the proxy is listed in `ForwardedHeaders` ([Application Settings](/reference/app-settings#forwardedheaders)).

## Composite endpoints

The sample composites create lines and a header that share a generated `TransactionKey`, which Globe+ processes as one entry.

```http
POST /api/{env}/composite/SalesOrder
Content-Type: application/json

{
  "Header": { "OrderDebtor": "60093", "YourReference": "Connect async" },
  "Lines": [
    { "Itemcode": "ITEM-001", "Quantity": 2, "Price": 0 },
    { "Itemcode": "ITEM-002", "Quantity": 4, "Price": 0 }
  ]
}
```

```http
POST /api/{env}/composite/FinancialEntry
Content-Type: application/json

{
  "Header": { "Journal": "90", "Description": "Invoice payment" },
  "Lines": [
    { "GLAccount": "1000", "Amount": 1000, "Description": "Payment received" },
    { "GLAccount": "1300", "Amount": -1000, "Description": "AR clearing" }
  ]
}
```

Steps run in order; a failed step stops the composite, and earlier steps are not undone. The response names the failed step. Configuration: [Composite Endpoints](/guide/endpoints-composite).

## Troubleshooting

| Symptom | Check |
|---|---|
| `401` or `403` from Globe+ | Globe+ rights of the service account; NTLM on the Application Pool |
| Transaction errors | Globe+ logs; locked records; one `TransactionKey` across all lines |
| Wrong or missing data | `DatabaseName` and `ServerName` headers in `settings.json` |
| `http` links behind HTTPS | Proxy listed in `ForwardedHeaders:KnownProxies` or `KnownNetworks` |
