# Microsoft Dynamics NAV/Business Central Integration

Portway provides integration with Microsoft Dynamics NAV/Business Central on-premise installations through proxy endpoints, enabling external applications to interact with NAV/BC data and services. This integration relies on environment-specific headers to route requests to the correct database instance.

:::warning
When deploying in IIS, the Application Pool Identity must be a domain user with NAV/BC OData permissions. On-premise NAV/BC uses Windows/NTLM authentication.
:::

## Overview

The Microsoft Dynamics NAV/Business Central integration uses Portway's proxy endpoints to forward requests to the internal NAV/BC OData web services. Each request must include proper environment configuration to ensure data is accessed from the correct company database and server instance.

## Configuration Requirements

### Environment Headers

All requests to NAV/BC endpoints require critical headers that are automatically added based on the environment:

| Header | Description | Example |
|--------|-------------|---------|
| `Company` | The NAV/BC company identifier | `CRONUS%20International%20Ltd.` |
| `ServerInstance` | The NAV/BC server instance | `DynamicsNAV130` |
| `ServerName` | The server hosting NAV/BC | `NAV-SERVER` |

These headers are configured in the environment settings and automatically injected into proxy requests.

### Environment Settings

Each environment must be properly configured in the settings:

```json
// environments/PROD/settings.json
{
  "ServerName": "NAV-SERVER",
  "ServerInstance": "DynamicsNAV130",
  "ConnectionString": "Server=NAV-SERVER;Database=Demo Database NAV (13-0);Trusted_Connection=True;Connection Timeout=5;TrustServerCertificate=true;",
  "Headers": {
    "Company": "CRONUS%20International%20Ltd.",
    "ServerInstance": "DynamicsNAV130", 
    "ServerName": "NAV-SERVER",
    "Origin": "Portway"
  }
}
```

## Available NAV/Business Central Endpoints

### Proxy Endpoints

You can configure the availability of NAV/BC endpoints by configuring proxy endpoints:

#### Customers

```json
{
  "Url": "http://nav-server:7048/DynamicsNAV130/ODataV4/Company('CRONUS%20International%20Ltd.')/Customer",
  "Methods": ["GET", "POST", "PATCH", "DELETE"]
}
```

#### Items

```json
{
  "Url": "http://nav-server:7048/DynamicsNAV130/ODataV4/Company('CRONUS%20International%20Ltd.')/Item",
  "Methods": ["GET", "POST", "PATCH", "DELETE"]
}
```

#### Sales Orders

```json
{
  "Url": "http://nav-server:7048/DynamicsNAV130/ODataV4/Company('CRONUS%20International%20Ltd.')/SalesHeader",
  "Methods": ["GET", "POST", "PATCH", "DELETE"]
}
```

#### Sales Order Lines

```json
{
  "Url": "http://nav-server:7048/DynamicsNAV130/ODataV4/Company('CRONUS%20International%20Ltd.')/SalesLine", 
  "Methods": ["GET", "POST", "PATCH", "DELETE"]
}
```

### Composite Endpoints

Composite endpoints handle complex operations that require multiple related transactions. These endpoints can create sales orders with lines, general journal entries, or other multi-step NAV/BC operations in a single request.

## Authentication with NAV/Business Central

The proxy endpoints handle NAV/BC authentication transparently:

1. Requests are forwarded with Windows authentication or NAV/BC service authentication
2. The service account running Portway must have NAV/BC database access
3. Individual API tokens control access to specific OData services

## Error Handling

NAV/BC specific error responses are preserved and forwarded:

```json
// NAV/BC validation error
{
  "error": {
    "code": "ValidationError",
    "message": "Customer 10000 does not exist in company CRONUS International Ltd.",
    "details": {
      "service": "Customer",
      "field": "No",
      "value": "10000"
    }
  }
}
```

## Notes

- NAV/BC OData field names use underscores (e.g., `Sell_to_Customer_No`) — use these exact names in `$filter` and `$select` expressions.
- The `Company` header value must be URL-encoded (e.g., `CRONUS%20International%20Ltd.`).
- Test against a NAV/BC test company before connecting to production.