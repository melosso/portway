---
title: Microsoft Dynamics NAV/Business Central Integration
description: "Proxy endpoints for on-premise Microsoft Dynamics NAV/Business Central OData web services"
---

# Microsoft Dynamics NAV/Business Central Integration

On-premise NAV/Business Central OData web services are exposed through proxy endpoints. Environment headers select the company and server instance.

:::info
On-premise NAV/BC uses Windows (NTLM) authentication. Under IIS, the Application Pool identity is a domain account with NAV/BC OData permissions.
:::

## Environment

```json [environments/PROD/settings.json]
{
  "ServerName": "NAV-SERVER",
  "ConnectionString": "Server=NAV-SERVER;Database=Demo Database NAV (13-0);Trusted_Connection=True;Connection Timeout=5;TrustServerCertificate=true;",
  "Headers": {
    "Company": "CRONUS%20International%20Ltd.",
    "ServerInstance": "DynamicsNAV130",
    "ServerName": "NAV-SERVER",
    "Origin": "Portway"
  }
}
```

| Header | Value |
|---|---|
| `Company` | Company name, URL-encoded |
| `ServerInstance` | Server instance, e.g. `DynamicsNAV130` |
| `ServerName` | NAV/BC server |

## Proxy endpoints

One endpoint per OData service, e.g. `Customer`, `Item`, `SalesHeader` and `SalesLine`:

```json [endpoints/Proxy/Customer/entity.json]
{
  "Url": "http://nav-server:7048/DynamicsNAV130/ODataV4/Company('CRONUS%20International%20Ltd.')/Customer",
  "Methods": ["GET", "POST", "PATCH", "DELETE"],
  "SupportsOData": true
}
```

Sales orders with lines and journal entries are created in one request with a [composite endpoint](/guide/endpoints-composite).

## Notes

- OData field names contain underscores (e.g. `Sell_to_Customer_No`) and are used as-is in `$filter` and `$select`.
- The `Company` value is URL-encoded.
- A NAV/BC test company is the target for initial testing.
