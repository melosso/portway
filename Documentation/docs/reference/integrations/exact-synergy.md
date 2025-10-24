# Exact Synergy Enterprise Integration

Portway provides selective integration with Exact Synergy Enterprise through proxy endpoints, enabling controlled access to specific Synergy data and services. While Synergy Enterprise has a native REST API, Portway is particularly useful when you need to expose only specific database sections or when Synergy is deployed behind a firewall within your internal network.

::: warning NTLM Authentication Required
If you're deploying Portway in IIS and need to connect to on-premise Exact Synergy Enterprise installations, you'll need to configure NTLM authentication. The Application Pool Identity must be bound to an internal (domain) user with the necessary permissions to connect to Synergy services. This is required for on-premise Synergy instances that use Windows/NTLM authentication.
:::

## Overview

The Exact Synergy Enterprise integration uses Portway's proxy endpoints to forward requests to the internal Synergy REST API. This approach is beneficial when:

- **Selective Exposure**: You want to expose only specific Synergy endpoints rather than the entire API surface
- **Network Security**: Synergy is behind a firewall and not directly accessible from external networks
- **Unified Gateway**: You want to integrate Synergy with other internal systems through a single API gateway
- **Access Control**: You need granular control over which Synergy endpoints are accessible

## Configuration Requirements

### Environment Headers

Synergy Enterprise uses standard HTTP authentication and doesn't require special environment headers like Globe+. Authentication is typically handled through **Windows Authentication**, since all environments are domain-integrated. You should have this set-up as mentioned in the installation instructions of Exact Synergy Enterprise. 

### Environment Settings

Each environment must be properly configured in the settings:

```json
// environments/Synergy/settings.json
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

## Use Cases

### 1. Selective API Exposure

**Scenario**: You want to expose only customer and invoice data from Synergy, not the entire API surface.

```json
// Only expose specific endpoints
{
  "AllowedEndpoints": [
    "Accounts",
    "Binary", 
    "Request"
  ]
}
```

### 2. Firewall Bypass

**Scenario**: Synergy is deployed on-premise behind a corporate firewall, but you need external access.

```json
{
  "Url": "http://internal-synergy/Synergy/services/Exact.Entity.REST.svc/Account",
  "Methods": ["GET"],
  "NetworkAccess": "Internal",
  "ExternalAccess": "Via Portway Proxy"
}
```

## Transaction Management

### Synergy Transaction Handling

Composite endpoints manage Synergy transactions according to API capabilities:

1. Uses Synergy's native transaction support where available
2. Maintains Synergy referential integrity rules
3. Handles Synergy-specific field validation

### Atomic Operations

Composite endpoints ensure atomicity according to Synergy's transaction model:
- Related API calls are grouped logically
- Failures trigger appropriate compensation actions
- Detailed error information includes Synergy API responses

## Best Practices

### 1. Authentication Management

- Use appropriate authentication method for your Synergy setup
- Test authentication in Synergy test environment first
- Monitor authentication failures and credential expiration

### 2. Error Handling

```javascript
try {
  const response = await fetch('/api/Synergy/composite/ProjectSetup', {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(projectData)
  });

  if (!response.ok) {
    const error = await response.json();
    console.error('Synergy error:', error.details || error.message);
  }
} catch (error) {
  console.error('Network error:', error);
}
```

### 3. Performance Considerations

- Synergy Enterprise may have connection limits
- Implement appropriate connection pooling in Portway
- Cache frequently accessed data to reduce load on Synergy server

### 4. Security Best Practices

For selective exposure:
- Use token scopes to limit endpoint access
- Implement field-level filtering where needed
- Monitor access patterns for unusual activity

## Troubleshooting

### Common Issues

1. **Authentication Failures**
   - Verify Synergy user permissions and credentials
   - Check Windows authentication configuration if using domain integration
   - Ensure proper network connectivity to Synergy server

2. **API Connectivity Issues**
   - Verify Synergy web service is running and accessible
   - Check firewall rules for Portway to Synergy communication
   - Test connectivity using Synergy's health check endpoints

3. **Network Connectivity**
   - Verify internal network access to Synergy server
   - Check that Synergy web services are properly configured
   - Test direct API calls to confirm Synergy availability

## Security Considerations

### Access Control

- Use token scopes to limit Synergy API access
- Implement environment-specific permissions
- Regular audit of exposed endpoints

### Data Protection

- Sensitive Synergy data is not logged by default
- Configure body capture policies carefully
- Use HTTPS for all external communications

### Authentication Management

- Secure storage of Synergy credentials
- Proper credential rotation policies
- Monitor authentication failures for security events

## Integration Examples

### Creating a Project with Error Handling

```javascript
async function createSynergyProject(projectData) {
  const response = await fetch('/api/Synergy/composite/ProjectSetup', {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(projectData)
  });

  if (!response.ok) {
    const error = await response.json();
    
    // Handle specific Synergy errors
    if (error.error?.code === 'ValidationError') {
      console.error('Synergy validation failed:', error.error.message);
    } else if (error.error?.code === 'RateLimitExceeded') {
      console.error('Synergy rate limit exceeded, retrying...');
      // Implement retry logic
    } else {
      console.error('Unexpected Synergy error:', error);
    }
    
    throw error;
  }

  const result = await response.json();
  console.log('Synergy project created:', result.StepResults);
  return result;
}
```

### Retrieving Account Information (Selective)

```javascript
async function getSynergyAccount(accountId, fieldsOnly = ['ID', 'Name', 'Code']) {
  const fields = fieldsOnly.join(',');
  const response = await fetch(
    `/api/Synergy/Account(guid'${accountId}')?$select=${fields}`,
    {
      headers: {
        'Authorization': `Bearer ${token}`,
        'Accept': 'application/json'
      }
    }
  );

  if (!response.ok) {
    throw new Error(`Failed to fetch Synergy account: ${response.statusText}`);
  }

  const data = await response.json();
  return data.d; // Return Synergy OData response
}
```

### Retrieving Binary Data

```javascript
async function getSynergyBinary(messageId) {
  const response = await fetch(
    `/api/Synergy/Binary?$filter=MessageID eq guid'${messageId}'`,
    {
      headers: {
        'Authorization': `Bearer ${token}`,
        'Accept': 'application/json'
      }
    }
  );

  if (!response.ok) {
    throw new Error(`Failed to fetch Synergy binary: ${response.statusText}`);
  }

  const data = await response.json();
  return data.d.results[0]; // Return first binary result
}
```

### Batch Operations for Performance

```javascript
async function batchSynergyOperations(operations) {
  // Synergy supports OData $batch operations
  const batchData = {
    requests: operations.map((op, index) => ({
      id: index + 1,
      method: op.method,
      url: op.endpoint,
      body: op.data,
      headers: {
        'Content-Type': 'application/json',
        'Accept': 'application/json'
      }
    }))
  };

  const response = await fetch('/api/Synergy/$batch', {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'multipart/mixed; boundary=batch_boundary'
    },
    body: JSON.stringify(batchData)
  });

  if (!response.ok) {
    const error = await response.json();
    console.error('Synergy batch operation failed:', error);
    throw error;
  }

  return await response.json();
}
```

## Advanced Topics

### Custom Entity Exposures

For custom entities in Synergy:

1. Map custom entity endpoints in proxy configuration
2. Handle custom field validation rules
3. Test thoroughly in Synergy development environment

### Performance Optimization

For high-volume integrations:

1. Use connection pooling for Synergy database connections
2. Implement intelligent caching strategies
3. Monitor Synergy server performance and resource usage
4. Consider using webhooks or polling for real-time updates

### Multi-Environment Support

For organizations with multiple Synergy environments:

```json
{
  "environments": {
    "PROD": {
      "ServerName": "synergy-prod",
      "Url": "http://synergy-prod/Synergy/services/Exact.Entity.REST.svc"
    },
    "TEST": {
      "ServerName": "synergy-test", 
      "Url": "http://synergy-test/Synergy/services/Exact.Entity.REST.svc"
    }
  }
}
```

> [!IMPORTANT]
> If you choose to implement a single Portway instance, you will bypass best practices regarding environment separation. In this context, a system account is simply an account; however, it is still recommended to maintain separate accounts per environment (e.g., TEST, PROD) to ensure proper security isolation and prevent cross-environment issues.