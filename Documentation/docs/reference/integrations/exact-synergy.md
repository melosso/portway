---
title: Exact Synergy Enterprise Integration
description: "While Synergy Enterprise ships a native REST API, you may not want to expose all of it"
---

# Exact Synergy Enterprise Integration

While Synergy Enterprise ships a native REST API, you may not want to expose all of it. Portway shines when you need only specific database sections available, or when Synergy sits behind a firewall on your internal network. Proxy endpoints give you that selective, controlled access.

::: Note
On-premise Synergy uses Windows/NTLM authentication. When you deploy in IIS, setting the Application Pool Identity to a domain user with Synergy permissions gives Portway the access it needs.
:::

## Overview

Portway proxies requests to the internal Synergy REST API. This is useful when Synergy is behind a firewall, or when you want to expose only a subset of its API surface through a controlled gateway.

## Configuration Requirements

### Environment Headers

Synergy Enterprise uses standard HTTP authentication. Unlike Globe+, it needs no special environment headers. Authentication typically runs through **Windows Authentication**, since Synergy environments are domain-integrated. The installation instructions of Exact Synergy Enterprise cover that setup.

### Environment Settings

Each environment needs to be configured in its settings:

```json [environments/Synergy/settings.json]
{
  "ServerName": "YOUR-SERVER",
  "ConnectionString": "Server=YOUR-SERVER;Database=Synergy;Trusted_Connection=True;",
  "Headers": {
    "Origin": "Portway"
  }
}
```

## Available Synergy Endpoints

### Proxy Endpoints

You can selectively configure which Synergy endpoints to expose through proxy endpoints:

#### Accounts (Selective Exposure)

```json
{
  "Url": "http://YOUR-SERVER/Synergy/services/Exact.Entity.REST.svc/Account",
  "Methods": ["GET"],
  "IsPrivate": false,
  "AllowedEnvironments": ["Synergy"]
}
```

### Composite Endpoints

These endpoints handle complex operations that require multiple related Synergy API calls:

#### Project Creation with Resources

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
    {
      "Code": "DEV001",
      "Description": "Development Phase",
      "Project": "PRJ-2025-001"
    },
    {
      "Code": "TEST001",
      "Description": "Testing Phase", 
      "Project": "PRJ-2025-001"
    }
  ]
}
```

This composite endpoint:
1. Creates a project in Synergy using the Project entity
2. Creates associated project WBS elements
3. Links WBS elements to the project with proper hierarchy

#### Binary Data Upload

```http
POST /api/Synergy/composite/BinaryUpload
Content-Type: application/json

{
  "Binary": {
    "Data": "UERGLTEuNCBmaWxlIGNvbnRlbnQ=",
    "Encoded": true,
    "DataString": "Sample PDF document"
  },
  "Document": {
    "Subject": "Project Documentation",
    "Type": 1,
    "Category": "Technical"
  }
}
```

This composite endpoint:
1. Creates binary data entry in Synergy
2. Creates associated document record
3. Returns the MessageID for future reference

## Error Handling

Synergy specific error responses are preserved and forwarded:

```json
// Synergy validation error
{
  "error": {
    "code": "ValidationError",
    "message": "Account {UUID} does not exist",
    "details": {
      "entity": "Accounts",
      "field": "ID",
      "value": "c91ca921-86e7-47d1-b52a-d3e41ab295a6"
    }
  }
}
```

## URL Rewriting

Portway automatically rewrites Synergy URLs in responses to maintain proxy routing:

- Original: `http://YOUR-SERVER/Synergy/services/Exact.Entity.REST.svc/Account(guid'12345')`
- Rewritten: `https://api.company.com/api/Synergy/Account(guid'12345')`

This ensures that related links in responses continue to work through the proxy.

## Troubleshooting

Most Synergy issues fall into one of these categories:

| Symptom | Check |
|---------|-------|
| Authentication failures (401/403) | Domain user permissions in Synergy; NTLM enabled on IIS Application Pool |
| Connection refused | Synergy web service running; firewall rules from Portway host to Synergy server |
| URL links in responses broken | URL rewriting is automatic. Verify `BaseProtocol` in `appsettings.json` matches your public hostname |
| Missing data | Proxy endpoint `Url` points to correct Synergy REST service path |

:::warning
Use separate Portway environments (and separate Synergy accounts) for TEST and PROD. A single shared account across environments removes the audit trail and risks cross-environment data writes.
:::
