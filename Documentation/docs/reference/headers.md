---
title: HTTP Headers
description: "Headers carry a surprising amount of Portway's conversation with your clients: authentication, content negotiation, request tracking, and service configuration all travel there"
---

# HTTP Headers

Headers carry a surprising amount of Portway's conversation with your clients: authentication, content negotiation, request tracking, and service configuration all travel there. This page collects every header Portway reads or writes, so you have one place to look things up.

## Required headers

### Authentication header

Every API request needs to include authentication:

```http
Authorization: Bearer your_token_here
```

This applies to every endpoint except `/health/live`, so it is worth confirming your client attaches the token on each call. Requests that arrive without it receive a `401 Unauthorized` response.

### Content type headers

For requests with a body (POST, PUT, PATCH):

```http
Content-Type: application/json
Accept: application/json
```

## Request headers

### Standard headers

| Header | Required | Description | Example |
|--------|----------|-------------|---------|
| `Authorization` | Yes | Bearer token for authentication | `Bearer abc123...` |
| `Content-Type` | For POST/PUT | Media type of request body | `application/json` |
| `Accept` | No | Preferred response format | `application/json` |
| `User-Agent` | No | Client application identifier | `MyApp/1.0` |
| `Accept-Encoding` | No | Supported compression | `gzip, deflate` |

### Custom headers

| Header | Purpose | Example |
|--------|---------|---------|
| `X-Client-ID` | Identify client application | `inventory-service` |
| `X-Request-ID` | Request correlation ID | `req-12345` |
| `X-Debug-Mode` | Enable debug information | `true` |
| `X-Forward-Host` | Original host in proxy | `api.internal` |

### Environment headers

Internal headers can be automatically added, based on the environment configuration. This means that `settings.json` in the `/environments/{env}` folder can append custom headers. For example:

| Header | Description | Set By |
|--------|-------------|--------|
| `ServerName` | Target server name | Environment config |
| `DatabaseName` | Target database | Environment config |
| `Origin` | Request origin | Environment config |

Example from environment settings:
```json
{
  ...
  "Headers": {
    "DatabaseName": "prod",
    "ServerName": "YOUR-APP-SERVER",
    "Origin": "Portway"
  }
}
```

## Response headers

### Standard response headers

| Header | Description | Example |
|--------|-------------|---------|
| `Content-Type` | Response format | `application/json` |
| `Content-Length` | Response size | `1234` |
| `Date` | Response timestamp | `Wed, 21 Oct 2023 07:28:00 GMT` |
| `Cache-Control` | Caching directives | `private, max-age=300` |

### Security headers

Portway automatically adds security headers to all responses:

| Header | Value | Purpose |
|--------|-------|---------|
| `X-Content-Type-Options` | `nosniff` | Prevent MIME sniffing |
| `X-Frame-Options` | `DENY` | Prevent clickjacking |
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains` | Enforce HTTPS |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Control referrer info |
| `Permissions-Policy` | `geolocation=(), camera=(), microphone=()` | Restrict features |

### Rate limiting headers

| Header | Description | Example |
|--------|-------------|---------|
| `X-RateLimit-Limit` | Request limit per window | `100` |
| `X-RateLimit-Remaining` | Remaining requests | `95` |
| `X-RateLimit-Reset` | Unix timestamp when the bucket is fully replenished | `1616161616` |
| `X-RateLimit-Resource` | Which limit produced the values, `ip` or `token` | `token` |
| `X-RateLimit-Used` | Requests consumed in the current window | `5` |
| `Retry-After` | Seconds until retry allowed, only on `429` responses | `60` |

## Content negotiation

### Request headers

```http
Accept: application/json
Accept-Language: en-US
Accept-Encoding: gzip, deflate
```

### Response headers

```http
Content-Type: application/json; charset=utf-8
Content-Language: en-US
Content-Encoding: gzip
```

## Compression

Portway supports response compression:

| Algorithm | Header Value | Priority |
|-----------|--------------|----------|
| Brotli | `br` | Highest |
| Gzip | `gzip` | Medium |
| Deflate | `deflate` | Lowest |

Request with compression preference:
```http
Accept-Encoding: br, gzip, deflate
```

## CORS headers

For cross-origin requests:

| Header | Description | Value |
|--------|-------------|-------|
| `Access-Control-Allow-Origin` | Allowed origins | `*` or specific origin |
| `Access-Control-Allow-Methods` | Allowed HTTP methods | `GET, POST, PUT, DELETE, OPTIONS` |
| `Access-Control-Allow-Headers` | Allowed request headers | `Authorization, Content-Type` |
| `Access-Control-Max-Age` | Preflight cache duration | `86400` |

## Proxy headers

When Portway acts as a reverse proxy:

| Header | Purpose | Example |
|--------|---------|---------|
| `X-Forwarded-For` | Client IP address | `192.168.1.1` |
| `X-Forwarded-Proto` | Original protocol | `https` |
| `X-Forwarded-Host` | Original host | `api.company.com` |
| `X-Real-IP` | Actual client IP | `192.168.1.1` |

## Caching headers

### ETag revalidation

Every successful `GET` response under `/api` carries a strong `ETag`, computed from the response body:

```http
HTTP/1.1 200 OK
ETag: "33a64df551425fcc55e4d42a148795d9f25f89d4..."
```

When your client sends that value back in `If-None-Match` and the data hasn't changed, Portway answers with `304 Not Modified` and an empty body, saving the transfer entirely:

```http
GET /api/prod/Products?$top=10
If-None-Match: "33a64df551425fcc55e4d42a148795d9f25f89d4..."

HTTP/1.1 304 Not Modified
```

Because the tag is derived from the actual response content, any change in the underlying data produces a new tag and a fresh `200` with the full body. Polling clients benefit the most: a poll loop that sends `If-None-Match` costs almost nothing while nothing changes.

### Response cache control

```http
Cache-Control: private, max-age=300
ETag: "33a64df551425fcc55e4d42a148795d9f25f89d4"
```

### Cache control directives

| Directive | Description | Example |
|-----------|-------------|---------|
| `public` | Cacheable by any cache | `public, max-age=3600` |
| `private` | Cacheable by browser only | `private, max-age=300` |
| `no-cache` | Validate before using cache | `no-cache` |
| `no-store` | Don't cache at all | `no-store` |
| `max-age` | Cache lifetime in seconds | `max-age=3600` |

## Header size limits

| Limit Type | Value | Description |
|------------|-------|-------------|
| Total header size | 32 KB | All headers combined |
| Single header value | 8 KB | Individual header value |
| Header count | 100 | Maximum number of headers |

Keep header values concise and use standard headers where possible. Avoid including sensitive data (passwords, connection strings, PII) in headers. Validate and sanitize custom header values. Disable debug headers in production.

## Security considerations

### Headers to avoid

:::warning
Never include passwords, secrets, PII, connection strings, or internal system paths in headers. Authorization headers are logged as `[REDACTED]` by Portway's traffic logging, but headers sent to upstream services are not automatically masked.
:::

## Common header issues

### Missing headers

```http
# Error: Missing Authorization
GET /api/500/Products
Response: 401 Unauthorized
{
  "error": "Authentication required",
  "clientIp": "0.0.0.0",
  "requestedPath": "/api/500/Products",
  "success": false
}
```

### Invalid header values

```http
# Error: Invalid content type
POST /api/500/Products
Content-Type: text/plain
Response: 415 Unsupported Media Type
{
  "error": "Content-Type must be application/json"
}
```

### Header conflicts

```http
# Error: Conflicting cache directives
GET /api/500/Products
Cache-Control: no-cache, max-age=3600
Response: 400 Bad Request
{
  "error": "Conflicting cache control directives"
}
```

## Testing headers

### Using cURL

```bash
# Basic request with headers
curl -H "Authorization: Bearer token123" \
     -H "Accept: application/json" \
     https://api.company.com/api/500/Products

# POST with content type
curl -X POST \
     -H "Authorization: Bearer token123" \
     -H "Content-Type: application/json" \
     -d '{"name":"Product"}' \
     https://api.company.com/api/500/Products
```

### Using Postman

1. Add headers in the Headers tab
2. Use environment variables for tokens
3. Set up header presets for common requests
4. Use Postman collections for header management

## Related topics

- [Authentication](/reference/api-auth) - Token and authorization headers
- [API Overview](/reference/) - General API reference
- [Environment Settings](/reference/environment-settings) - Environment-specific headers
- [Security Guide](/guide/security) - Security header configuration
