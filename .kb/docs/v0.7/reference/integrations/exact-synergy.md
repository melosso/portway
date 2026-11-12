---
title: Exact Synergy Enterprise Integration
description: "Proxy endpoints that expose selected parts of the Exact Synergy Enterprise REST API"
---

# Exact Synergy Enterprise Integration

Proxy endpoints expose selected Synergy Enterprise REST entities, including from a Synergy server on an internal network. Entities without an endpoint stay unreachable.

:::info
On-premise Synergy uses Windows (NTLM) authentication. Under IIS, the Application Pool identity is a domain account with Synergy access. Synergy needs no environment headers.
:::

## Environment

```json [environments/Synergy/settings.json]
{
  "ServerName": "YOUR-SERVER",
  "ConnectionString": "Server=YOUR-SERVER;Database=Synergy;Trusted_Connection=True;",
  "Headers": {
    "Origin": "Portway"
  }
}
```

## Proxy endpoints

```json [endpoints/Proxy/Account/entity.json]
{
  "Url": "http://YOUR-SERVER/Synergy/services/Exact.Entity.REST.svc/Account",
  "Methods": ["GET"],
  "SupportsOData": true,
  "AllowedEnvironments": ["Synergy"]
}
```

Synergy URLs in responses are rewritten to the Portway URL, e.g. `http://YOUR-SERVER/Synergy/services/Exact.Entity.REST.svc/Account(guid'12345')` to `https://api.company.com/api/Synergy/Account(guid'12345')`.

## Composite endpoints

A composite creates related entities in one request, e.g. a project with its WBS elements. It requires a composite definition with one step per entity ([Composite Endpoints](/guide/endpoints-composite)):

```http
POST /api/Synergy/composite/ProjectSetup
Content-Type: application/json

{
  "Project": {
    "Code": "PRJ-2025-001",
    "Description": "Website Development Project",
    "StartDate": "2025-08-18T00:00:00",
    "Type": 2
  },
  "ProjectWBS": [
    { "Code": "DEV001", "Description": "Development Phase", "Project": "PRJ-2025-001" },
    { "Code": "TEST001", "Description": "Testing Phase", "Project": "PRJ-2025-001" }
  ]
}
```

:::warning
Separate Portway environments and separate Synergy accounts for test and production keep writes and audit trails apart.
:::

## Troubleshooting

| Symptom | Check |
|---|---|
| `401` or `403` from Synergy | Synergy rights of the domain account; NTLM on the Application Pool |
| Connection refused | Synergy web service; firewall between Portway and Synergy |
| `http` links behind HTTPS | Proxy listed in `ForwardedHeaders:KnownProxies` or `KnownNetworks` |
| Missing data | Synergy REST path in `Url` |
