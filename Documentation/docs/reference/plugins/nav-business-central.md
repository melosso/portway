# Microsoft Dynamics NAV/Business Central Plugins

Portway provides integration with Microsoft Dynamics NAV/Business Central on-premise installations through proxy endpoints, enabling external applications to interact with NAV/BC data and services. This integration relies on environment-specific headers to route requests to the correct database instance.

::: warning NTLM Authentication Required
If you're deploying Portway in IIS and need to connect to on-premise Microsoft Dynamics NAV/Business Central installations, you'll need to configure NTLM authentication. The Application Pool Identity must be bound to an internal (domain) user with the necessary permissions to connect to NAV/BC OData services. This is required because NAV/BC typically uses Windows/NTLM authentication for API access.
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

## Best Practices

### 1. Environment Management

- Keep NAV/BC company configurations synchronized
- Test in NAV/BC test companies first
- Use consistent NAV/BC field naming conventions (underscores)

### 2. Error Handling

```javascript
try {
  const response = await fetch('/api/PROD/Customers', {
    method: 'GET',
    headers: {
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json'
    }
  });

  if (!response.ok) {
    const error = await response.json();
    console.error('NAV/BC error:', error.details || error.message);
  }
} catch (error) {
  console.error('Network error:', error);
}
```

### 3. NAV/BC Field Mapping

- Use NAV/BC OData field names with underscores (e.g., `Sell_to_Customer_No`)
- Validate data according to NAV/BC field types and lengths
- Handle NAV/BC-specific date and decimal formats

## Troubleshooting

### Common Issues

1. **Authentication Failures**
   - Verify NAV/BC user permissions and licenses
   - Check Windows authentication configuration
   - Ensure proper company access rights

2. **OData Service Errors**
   - Review NAV/BC event logs
   - Check for locked records in NAV/BC
   - Verify OData service publication and availability

3. **Missing Data**
   - Confirm NAV/BC company configuration
   - Validate NAV/BC field mappings
   - Check NAV/BC table permissions

## Security Considerations

### Access Control

- Use Portway token scopes to limit OData service access
- Implement environment-specific permissions
- Monitor access patterns for unusual activity

### Data Protection

- Sensitive NAV/BC data is not logged by default
- Configure body capture carefully in production
- Use HTTPS for all external communications

### Audit Trail

NAV/BC operations are tracked through:
- Portway request traffic logging
- NAV/BC native change log
- Windows event logs for authentication