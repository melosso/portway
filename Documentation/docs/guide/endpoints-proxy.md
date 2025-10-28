# Proxy Endpoints

Proxy endpoints in Portway enable you to securely expose internal services and APIs through a unified gateway. These endpoints act as a reverse proxy, forwarding requests to internal services while providing authentication, environment awareness, and URL rewriting capabilities.


::: tip
If you have to rely on NTLM-authentication (e.g. for [Exact Globe+](https://www.exact.com/nl/software/exact-globe) or [Exact Synergy](https://www.exact.com/nl/software/exact-synergy)), then you'll have to bind the Identity of the Application Pool to an internal (domain) user instead. This user has to have the necessary permissions to connect to the internal services.
:::

## Overview

Proxy endpoints allow you to:
- Forward requests to internal HTTP/HTTPS services
- Control which HTTP methods are allowed
- Apply authentication and authorization
- Rewrite URLs for consistent external API structure
- Cache responses for improved performance
- Integrate with legacy systems seamlessly

## Configuration

### Example

Create a JSON file in the `endpoints/Proxy/{EndpointName}/entity.json` directory:

```json
{
  "Url": "http://internal-service:8080/api/resource",
  "Methods": ["GET", "POST", "PUT", "DELETE"],
  "AllowedEnvironments": ["dev", "test", "prod"]
}
```

### Configuration Properties

The `entity.json` file has various properties:

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Url` | string | Yes | The target URL to proxy requests to |
| `Methods` | array | Yes | HTTP methods to allow (GET, POST, PUT, DELETE, PATCH) |
| `IsPrivate` | boolean | No | If true, endpoint won't be exposed in OpenAPI (default: false) |
| `AllowedEnvironments` | array | No | Environments where endpoint is available |

## Using Proxy Endpoints

### Making Requests

Proxy endpoints maintain the same request structure as the target service:

```http
GET /api/prod/UserService?id=123
Authorization: Bearer <token>
```

This request would be forwarded to:
```
http://internal-service:8080/api/resource?id=123
```

### Request Forwarding

All aspects of the original request are preserved:
- HTTP method
- Query parameters
- Request headers (except Host)
- Request body
- Content type

```http
POST /api/prod/OrderService
Content-Type: application/json
Authorization: Bearer <token>

{
  "orderId": "ORD-123",
  "items": [
    {"product": "ABC", "quantity": 2}
  ]
}
```

## URL Rewriting

Portway automatically rewrites URLs in responses to maintain consistent external paths:

### Original Response
```json
{
  "id": "123",
  "name": "Test User",
  "_links": {
    "self": "http://internal-service:8080/api/users/123",
    "orders": "http://internal-service:8080/api/users/123/orders"
  }
}
```

### Rewritten Response
```json
{
  "id": "123",
  "name": "Test User",
  "_links": {
    "self": "/api/prod/UserService/123",
    "orders": "/api/prod/UserService/123/orders"
  }
}
```

:::tip
URL rewriting ensures that clients always use the proxy endpoint, maintaining security and consistency.
:::

## Headers and Authentication

### Environment Headers

Portway automatically adds environment-specific headers to forwarded requests:

```http
# Added by Portway
ServerName: PROD-SERVER
DatabaseName: production
Origin: Portway
```

Configure these in your environment settings:

```json
{
  ...
  "Headers": {
    "DatabaseName": "production",
    "ServerName": "APP-SERVER",
    "CustomHeader": "CustomValue"
  }
}
```

### Authentication Forwarding

The `Authorization` header is forwarded to the internal service, allowing for:
- Pass-through authentication
- Service-to-service authentication
- Token validation at the target service

## Response Caching

Proxy endpoints implement intelligent caching for GET requests:

```json
// Response includes cache headers
Cache-Control: public, max-age=300
```

### Cache Configuration

The caching behavior is automatic and includes:
- 5-minute default cache duration for GET requests
- Cache key based on URL, query parameters, and authorization
- Automatic cache invalidation on non-GET requests

:::warning
Caching is only applied to GET requests. POST, PUT, DELETE, and PATCH requests are never cached.
:::

## Advanced Examples

### Internal API Gateway

```json
{
  "Url": "http://internal-api-gateway:8080/services",
  "Methods": ["GET", "POST"],
  "AllowedEnvironments": ["prod", "staging"]
}
```

### Legacy SOAP Service

```json
{
  "Url": "http://legacy-service/soap/endpoint",
  "Methods": ["POST"],
  "IsPrivate": true,
  "AllowedEnvironments": ["prod"]
}
```

### External API Integration

```json
{
  "Url": "https://api.external-service.com/v1/data",
  "Methods": ["GET"],
  "AllowedEnvironments": ["dev", "test", "prod"]
}
```

## Security Considerations

### Network Access Control

Portway validates target URLs against security policies:

```json
{
  "allowedHosts": [
    "internal-service",
    "api.trusted-domain.com"
  ],
  "blockedIpRanges": [
    "10.0.0.0/8",
    "172.16.0.0/12",
    "192.168.0.0/16"
  ]
}
```

### Private Endpoints

Mark sensitive endpoints as private to hide them from documentation:

```json
{
  "Url": "http://admin-service/sensitive-api",
  "Methods": ["POST"],
  "IsPrivate": true
}
```

:::tip
Use `IsPrivate: true` for administrative endpoints or services that shouldn't be publicly documented.
:::

## Performance Optimization

### Response Streaming

Large responses are streamed to avoid memory issues:
- File downloads
- Large data exports
- Media content

### Connection Pooling

Portway maintains connection pools for efficient request handling:
- Reuses TCP connections
- Reduces latency
- Handles high concurrent load

## Error Handling

### Common Error Responses

```json
// Target service unavailable
{
  "error": "Service unavailable",
  "detail": "Unable to connect to internal service",
  "status": 503
}

// Method not allowed
{
  "error": "Method not allowed",
  "detail": "POST method is not configured for this endpoint",
  "status": 405
}

// Authentication failure
{
  "error": "Unauthorized",
  "detail": "Invalid or missing authentication token",
  "status": 401
}
```

## Troubleshooting

### Common Issues

1. **"Connection refused" errors**
   - Verify the target service is running
   - Check network connectivity
   - Ensure correct port numbers

2. **"Method not allowed" errors**
   - Check the `Methods` array in configuration
   - Verify the HTTP method is spelled correctly

3. **URL rewriting issues**
   - Check for hardcoded URLs in the target service
   - Verify the proxy configuration matches the service structure

4. **Performance problems**
   - Monitor target service response times
   - Check for network latency
   - Review caching configuration

### Debugging

Enable detailed logging for proxy requests:

```json
{
  "RequestTrafficLogging": {
    "Enabled": true,
    "IncludeRequestBodies": true,
    "IncludeResponseBodies": true
  }
}
```

## Best Practices

1. **Use Specific Methods**
   - Only enable required HTTP methods
   - Avoid using `["*"]` for methods

2. **Implement Timeouts**
   - Configure appropriate timeouts for slow services
   - Handle timeout errors gracefully

3. **Monitor Performance**
   - Track response times
   - Set up alerts for service degradation
   - Use caching strategically

4. **Secure Internal Services**
   - Use HTTPS for sensitive data
   - Implement proper authentication
   - Validate SSL certificates

## Proxy vs. SQL Endpoints

| Feature | Proxy Endpoints | SQL Endpoints |
|---------|----------------|---------------|
| Purpose | Forward HTTP requests | Direct database access |
| Protocol | HTTP/HTTPS | SQL over TCP |
| Authentication | Pass-through | Token-based |
| Data Format | Any (JSON, XML, etc.) | JSON only |
| Caching | Automatic for GET | Not implemented |
| Use Case | Legacy services, APIs | Database operations |

## Next Steps

- Explore [Composite Endpoints](/guide/endpoints/composite) for orchestrating multiple services
- Learn about [SQL Endpoints](/guide/endpoints/sql) for database access
- Configure [Environment Settings](/guide/environments) for your proxy endpoints
- Implement [Security Best Practices](/guide/security) for your API gateway