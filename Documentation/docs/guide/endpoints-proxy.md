---
title: Proxy Endpoints
description: "Forward requests to internal HTTP/HTTPS services through a consistent, authenticated gateway URL"
---

# Proxy Endpoints

Proxy endpoints put Portway in front of an internal service: requests route through the gateway and responses come back to your caller unchanged. Along the way Portway adds token authentication, environment headers, and URL rewriting, while the internal service receives the request transparently without ever knowing about the gateway.

:::details Note on pass-through authentication
If your backend requires NTLM authentication (Exact Globe+ or Exact Synergy, for example), binding the IIS Application Pool identity to a domain user with the necessary permissions gives Portway the access it needs.
:::

## Configuration

Create `endpoints/Proxy/{EndpointName}/entity.json`:

```json
{
  "Url": "http://internal-service:8080/api/resource",
  "Methods": ["GET", "POST", "PUT", "DELETE"],
  "AllowedEnvironments": ["dev", "test", "prod"]
}
```

### Configuration properties

| Property | Required | Type | Description |
|---|---|---|---|
| `Url` | Yes | string | Target URL to forward requests to |
| `Methods` | Yes | array | HTTP methods to allow: `GET`, `POST`, `PUT`, `DELETE`, `PATCH` |
| `Hidden` | No | boolean | Exclude from OpenAPI documentation. Defaults to `false` |
| `AllowedEnvironments` | No | array | Environments where this endpoint responds |

Only configure the HTTP methods your internal service actually exposes. Omit methods that the target does not support.

## Request forwarding

Portway forwards the original request to the target URL, preserving:
- HTTP method
- Query parameters
- Request headers (except `Host`)
- Request body and content type

The `Authorization` header is forwarded unchanged, enabling pass-through authentication to internal services that validate Bearer tokens.

The query string is forwarded verbatim, including the provided OData options in your (HTTP) request. In particular `$expand` is never parsed, validated or rejected on a proxy endpoint: it reaches the upstream exactly as written, so a service that implements `$expand` natively keeps working. That's a long way of saying: Portway only handles `$expand` itself on [SQL Table and View endpoints](/reference/expand).

Because forwarding is all Portway does here, the documentation cannot infer which query parameters a proxied service accepts. Set `SupportsOData` when the service behind the endpoint understands them:

```json
{
  "Url": "http://localhost:8020/services/Exact.Entity.REST.EG/Account",
  "Methods": ["GET"],
  "SupportsOData": true
}
```

With the flag set, the endpoint's GET operation lists `$select`, `$top` and `$filter` so callers can discover them. Without it, the operation says instead that any query parameters are passed through untouched, which keeps the reference transparent about services that never implemented OData.

Environment headers defined in `environments/{env}/settings.json` are appended to every forwarded request:

```http
# Added by Portway from environment settings
ServerName: PROD-APP-SERVER
DatabaseName: production
Origin: Portway
```

## URL rewriting

Portway rewrites internal URLs in responses so callers always see gateway-relative paths:

**Internal service response:**
```json
{
  "_links": {
    "self": "http://internal-service:8080/api/users/123",
    "orders": "http://internal-service:8080/api/users/123/orders"
  }
}
```

**Rewritten response returned to caller:**
```json
{
  "_links": {
    "self": "/api/prod/UserService/123",
    "orders": "/api/prod/UserService/123/orders"
  }
}
```

This ensures internal hostnames and ports are never exposed to API consumers.

## Caching

GET responses are cached for 5 minutes by default. The cache key includes the URL, query parameters, and Authorization header. POST, PUT, DELETE, and PATCH requests bypass the cache and invalidate any cached GET response for that endpoint.

## Hidden endpoints

Setting `Hidden: true` keeps an endpoint out of the OpenAPI documentation at `/docs`. It carries on serving requests exactly as before, it is simply not listed for browsers of your reference.

```json
{
  "Url": "http://admin-service/internal-api",
  "Methods": ["POST"],
  "Hidden": true
}
```

## Examples

**Internal API:**
```json
{
  "Url": "http://internal-api-gateway:8080/services",
  "Methods": ["GET", "POST"],
  "AllowedEnvironments": ["prod", "staging"]
}
```

**Legacy SOAP service (write-only, unlisted):**
```json
{
  "Url": "http://legacy-service/soap/endpoint",
  "Methods": ["POST"],
  "Hidden": true,
  "AllowedEnvironments": ["prod"]
}
```

## Troubleshooting

**"Connection refused"**: Verify the target service is running and reachable from the Portway host. Check port numbers and firewall rules.

**"Method not allowed"**: Verify the HTTP method is listed in `Methods`.

**URL rewriting issues**: If clients receive internal hostnames in responses, check whether the internal service generates absolute URLs in its response body.

**Slow responses**: Enable request traffic logging to measure where latency is occurring:

```json
{
  "RequestTrafficLogging": {
    "Enabled": true,
    "IncludeRequestBodies": true,
    "IncludeResponseBodies": true
  }
}
```

## Next steps

- [Composite Endpoints](/guide/endpoints-composite): orchestrate multiple proxy steps
- [Environments](/guide/environments): configure per-environment headers and auth
- [Security](/guide/security)
