# Exact Globe+ Plugins

Portway provides integration with the API-layer of Exact Globe+ (previously known as Globe Next) through proxy endpoints, enabling external applications to interact with Globe+ data and services. This integration relies on environment-specific headers to route requests to the correct database instance.

::: warning NTLM Authentication Required
If you're deploying Portway in IIS and need to connect to Exact Globe+, you'll need to configure NTLM authentication. The Application Pool Identity must be bound to an internal (domain) user with the necessary permissions to connect to Globe+ services. This is required because Globe+ typically uses Windows/NTLM authentication for API access.
:::

## Overview

The Exact Globe+ integration uses Portway's proxy endpoints to forward requests to the internal Globe+ REST services. Each request must include proper environment configuration to ensure data is accessed from the correct database and server.

## Configuration Requirements

### Environment Headers

All requests to Globe+ endpoints require two critical headers that are automatically added based on the environment:

| Header | Description | Example |
|--------|-------------|---------|
| `DatabaseName` | The Globe+ database identifier | `600`, `700` |
| `ServerName` | The server hosting Globe+ | `YOUR-SERVER` |

These headers are configured in the environment settings and automatically injected into proxy requests.

### Environment Settings

Each environment must be properly configured in the settings:

```json
// environments/600/settings.json
{
  "ServerName": "YOUR-SERVER",
  "ConnectionString": "Server=YOUR-SERVER;Database=600;Trusted_Connection=True;",
  "Headers": {
    "DatabaseName": "600",
    "ServerName": "YOUR-SERVER",
    "Origin": "Portway"
  }
}
```

## Available Globe+ Endpoints

### Proxy Endpoints

You can configure the availability of the Globe+ endpoints, by configuring a proxy endpoint.

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
      "Itemcode": "BEK0001",
      "Quantity": 2,
      "Price": 0
    },
    {
      "Itemcode": "BEK0002",
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
2. The service account running Portway must have Globe+ access
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
- Rewritten: `https://api.company.com/api/600/Account(guid'123')`

This ensures that related links in responses continue to work through the proxy.

## Transaction Management

### TransactionKey Handling

Composite endpoints manage TransactionKey automatically:

1. A unique GUID is generated for the transaction
2. The key is applied to all related records
3. Globe+ processes the records as a single unit

### Atomic Operations

Composite endpoints ensure atomicity:
- All steps must succeed for the operation to complete
- Failures in any step roll back the entire transaction
- Detailed error information is provided for troubleshooting

## Best Practices

### 1. Environment Management

- Keep environment configurations synchronized
- Test in non-production environments first
- Use consistent naming conventions

### 2. Error Handling

```javascript
try {
  const response = await fetch('/api/600/composite/SalesOrder', {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(orderData)
  });

  if (!response.ok) {
    const error = await response.json();
    console.error('Globe+ error:', error.details || error.message);
  }
} catch (error) {
  console.error('Network error:', error);
}
```

### 3. Batch Operations

For bulk operations, consider:
- Using composite endpoints for related records
- Implementing retry logic for transient failures
- Monitoring performance impact on Globe+

### 4. Data Validation

- Validate data before sending to Globe+
- Check required fields based on Globe+ configuration
- Handle Globe+ specific field formats (dates, decimals)

## Troubleshooting

### Common Issues

1. **Authentication Failures**
   - Verify service account permissions (e.g. Basic workstation setup, permissions of the user in the desired company)
   - Check Windows authentication configuration (e.g. NTLM in the Application Pool in the IIS-instance)
   - Ensure proper environment headers (see reference)

2. **Transaction Errors**
   - Review Globe+ application logs (which is rather minimal)
   - Check for locked records (e.g. whilest updating Transactions)
   - Verify TransactionKey consistency (re-use the same UUID)

3. **Missing Data**
   - Confirm environment configuration
   - Validate field mappings
   - Check Globe+ entity relationships

## Security Considerations

### Access Control

- Use token scopes to limit endpoint access
- Implement environment-specific permissions
- Monitor access patterns for anomalies

### Data Protection

- Sensitive Globe+ data is not logged by default
- Configure body capture carefully in production
- Use HTTPS for all external communications

### Audit Trail

Globe+ operations are tracked through:
- Request traffic logging
- Globe+ native audit tables
- Windows event logs for authentication

## Integration Examples

### Creating a Sales Order with Error Handling

```javascript
async function createSalesOrder(orderData) {
  const response = await fetch('/api/600/composite/SalesOrder', {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(orderData)
  });

  if (!response.ok) {
    const error = await response.json();
    
    // Handle specific Globe+ errors
    if (error.error?.code === 'ValidationError') {
      console.error('Validation failed:', error.error.message);
      // Show user-friendly error
    } else {
      console.error('Unexpected error:', error);
      // Generic error handling
    }
    
    throw error;
  }

  const result = await response.json();
  console.log('Order created:', result.StepResults.CreateOrderHeader);
  return result;
}
```

### Retrieving Account Information

```javascript
async function getAccount(accountCode) {
  const response = await fetch(
    `/api/600/Account?$filter=CustomerCode eq '${accountCode}'`,
    {
      headers: {
        'Authorization': `Bearer ${token}`
      }
    }
  );

  if (!response.ok) {
    throw new Error(`Failed to fetch account: ${response.statusText}`);
  }

  const data = await response.json();
  return data.Value[0]; // Return first matching account
}
```

### Financial Entry Processing

```javascript
async function processFinancialEntry(entryData) {
  // Validate balanced entry
  const totalAmount = entryData.Lines.reduce(
    (sum, line) => sum + line.Amount, 
    0
  );
  
  if (Math.abs(totalAmount) > 0.01) {
    throw new Error('Entry is not balanced');
  }

  const response = await fetch('/api/600/composite/FinancialEntry', {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(entryData)
  });

  if (!response.ok) {
    const error = await response.json();
    console.error('Financial entry failed:', error);
    throw error;
  }

  return await response.json();
}
```

## Advanced Topics

### Custom Entity Extensions

If your Globe+ installation has custom entities:

1. Create new proxy endpoint configurations
2. Map the entity URL and allowed methods
3. Test thoroughly in development environment

### Performance Optimization

For high-volume integrations:

1. Use batch operations where possible
2. Implement caching for read-heavy operations
3. Monitor Globe+ server resources
4. Consider implementing rate limiting

### Handling Globe+ Maintenance

During Globe+ maintenance windows, which is often configured to be offline once-a-day (e.g. between non-working hours):

1. Implement graceful degradation
2. Queue non-critical operations
3. Provide user notifications
4. Monitor health check endpoints