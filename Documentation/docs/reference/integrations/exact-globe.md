---
title: Exact Globe+ Integration
description: "Exact Globe+ (previously known as Globe Next) exposes an API layer that Portway can put a friendly gateway in front of"
---

# Exact Globe+ Integration

Exact Globe+ (previously known as Globe Next) exposes an API layer that Portway can put a friendly gateway in front of. Through proxy endpoints, your external applications talk to Globe+ data and services, while environment-specific headers route each request to the correct database instance.

::: Note
Globe+ uses Windows/NTLM authentication. When you deploy in IIS, setting the Application Pool Identity to a domain user with Globe+ permissions gives Portway the access it needs
::: 

## Overview

The Exact Globe+ integration uses Portway's proxy endpoints to forward requests to the internal Globe+ REST services. Each request carries its environment configuration. That is how data ends up coming from the correct database and server.

## Configuration Requirements

### Environment Headers

All requests to Globe+ endpoints require two critical headers that are automatically added based on the environment:

| Header | Description | Example |
|--------|-------------|---------|
| `DatabaseName` | The Globe+ database identifier | `500`, `700` |
| `ServerName` | The server hosting Globe+ | `YOUR-SERVER` |

These headers are configured in the environment settings and automatically injected into proxy requests.

### Environment Settings

Each environment needs to be configured in its settings:

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

## Available Globe+ Endpoints

### Proxy Endpoints

Each Globe+ service you want to expose gets a proxy endpoint definition. The endpoint URL points at the internal Globe+ REST service, and the environment headers above route it to the right database:

```json [endpoints/Proxy/Account/entity.json]
{
  "Url": "http://localhost:8020/services/Exact.Entity.REST.EG/Account",
  "Methods": ["GET", "POST", "PUT", "DELETE"],
  "AllowedEnvironments": ["500", "700"]
}
```

### Composite Endpoints

These endpoints handle complex operations that require multiple related transactions:

#### Sales Order Creation

```http
POST /api/{env}/composite/SalesOrder
Content-Type: application/json

{
  "Header": {
    "OrderDebtor": "60093",
    "YourReference": "Connect async"
  },
  "Lines": [
    {
      "Itemcode": "ITEM-001",
      "Quantity": 2,
      "Price": 0
    },
    {
      "Itemcode": "ITEM-002",
      "Quantity": 4,
      "Price": 0
    }
  ]
}
```

This composite endpoint:
1. Creates sales order lines with a shared TransactionKey
2. Creates the sales order header using the same key
3. Returns the complete order information

#### Financial Entry Creation

```http
POST /api/{env}/composite/FinancialEntry
Content-Type: application/json

{
  "Header": {
    "Journal": "90",
    "Description": "Invoice payment"
  },
  "Lines": [
    {
      "GLAccount": "1000",
      "Amount": 1000,
      "Description": "Payment received"
    },
    {
      "GLAccount": "1300", 
      "Amount": -1000,
      "Description": "AR clearing"
    }
  ]
}
```

This composite endpoint:
1. Creates financial lines with a shared TransactionKey
2. Creates the financial header to complete the transaction
3. Ensures balanced entries

## Authentication with Globe+

The proxy endpoints handle Globe+ authentication transparently:

1. Requests are forwarded with Windows authentication
2. The service account running Portway needs Globe+ access
3. Individual API tokens control access to specific endpoints

## Error Handling

Globe+ specific error responses are preserved and forwarded:

```json
// Globe+ validation error
{
  "error": {
    "code": "ValidationError",
    "message": "Customer 60093 not found",
    "details": {
      "field": "OrderDebtor",
      "value": "60093"
    }
  }
}
```

## URL Rewriting

Portway automatically rewrites Globe+ URLs in responses to maintain proxy routing:

- Original: `http://localhost:8020/services/Exact.Entity.REST.EG/Account(guid'123')`
- Rewritten: `https://api.company.com/api/500/Account(guid'123')`

This ensures that related links in responses continue to work through the proxy.

## Transaction Management

### TransactionKey Handling

Composite endpoints manage TransactionKey automatically:

1. A unique GUID is generated for the transaction
2. The key is applied to all related records
3. Globe+ processes the records as a single unit

### Atomic Operations

Composite endpoints ensure atomicity:
- The operation completes only when every step succeeds
- Failures in any step roll back the entire transaction
- Detailed error information is provided for troubleshooting

## Troubleshooting

Most Globe+ issues fall into one of these categories:

| Symptom | Check |
|---------|-------|
| Authentication failures (401/403) | Service account permissions in Globe+; NTLM enabled on IIS Application Pool; correct domain user bound |
| Transaction errors | Globe+ application logs; locked records; re-use same TransactionKey UUID across all lines in a composite |
| Missing data in responses | Environment headers (`DatabaseName`, `ServerName`) correctly set in `settings.json` |
| URL links in responses broken | URL rewriting is automatic. Verify `BaseProtocol` in `appsettings.json` matches your public hostname |
