---
title: SAP Business One Integration
description: "Proxy endpoint for the SAP Business One Service Layer with client-side session login"
---

# SAP Business One Integration

The SAP Business One [Service Layer](https://help.sap.com/doc/fc2f5477516c404c8bf9ad1315a17238/10.0/en-US/Working_with_SAP_Business_One_Service_Layer.pdf){target="_blank" rel="noopener"} is an OData REST API with session login. Clients log in through Portway and send the `B1SESSION` cookie on later requests; Portway controls which clients reach the Service Layer.

## Endpoint

```json [endpoints/Proxy/SapB1/ServiceLayer/entity.json]
{
  "Url": "https://sap-server:50000/b1s/v1",
  "Methods": ["GET", "POST", "PATCH", "DELETE"],
  "SupportsOData": true,
  "AllowedEnvironments": ["prod"]
}
```

Paths after the endpoint name are appended to `Url`. One endpoint serves `Login`, `Items`, `Orders` and the other Service Layer resources. Each integration uses its own SAP B1 user with minimal authorizations.

## Requests

```http
POST /api/prod/SapB1/ServiceLayer/Login
Authorization: Bearer YOUR_PORTWAY_TOKEN
Content-Type: application/json

{ "CompanyDB": "SBODEMOUS", "UserName": "integration01", "Password": "SAP_B1_PASSWORD" }
```

```http
GET /api/prod/SapB1/ServiceLayer/Items?$filter=ItemsGroupCode eq 100&$top=20
Authorization: Bearer YOUR_PORTWAY_TOKEN
Cookie: B1SESSION=...
```

Sessions expire after about 30 minutes; a `401` from the Service Layer requires a new login.

## Limitations

- The login body contains the SAP password. `IncludeRequestBodies` in [traffic logging](/reference/audit) stays off for this endpoint.
- OData parameters are passed to the Service Layer without translation.
- All resources share one endpoint; access is limited by SAP B1 authorizations.

## Troubleshooting

| Symptom | Check |
|---|---|
| `401` from the Service Layer | Session expired; log in again |
| `401` directly after login | `B1SESSION` cookie on every request |
| `401` from Portway | Token scope for the endpoint and environment |
| Connection refused | Service Layer on the SAP server; port 50000 from the Portway host |
