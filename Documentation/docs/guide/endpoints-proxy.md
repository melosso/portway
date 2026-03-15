# Proxy Endpoints

> Forward requests to internal HTTP/HTTPS services through a consistent, authenticated gateway URL.

Proxy endpoints route incoming requests to an internal service and return its response. Portway adds token authentication, environment headers, and URL rewriting — the internal service receives the request transparently without needing to know about the gateway.

:::info
If your backend requires NTLM authentication (e.g. Exact Globe+ or Exact Synergy), bind the IIS Application Pool identity to a domain user with the necessary permissions to reach those services.
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
| `IsPrivate` | No | boolean | Exclude this endpoint from OpenAPI documentation. Defaults to `false` |
| `AllowedEnvironments` | No | array | Environments where this endpoint responds |

Only configure the HTTP methods your internal service actually exposes. Omit methods that the target does not support.

## Request forwarding

Portway forwards the original request to the target URL, preserving:
- HTTP method
- Query parameters
- Request headers (except `Host`)
- Request body and content type

The `Authorization` header is forwarded unchanged, enabling pass-through authentication to internal services that validate Bearer tokens.

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

## Private endpoints

Set `IsPrivate: true` to exclude an endpoint from the OpenAPI documentation at `/docs`. The endpoint still functions normally — it is simply not listed.

```json
{
  "Url": "http://admin-service/internal-api",
  "Methods": ["POST"],
  "IsPrivate": true
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
  "IsPrivate": true,
  "AllowedEnvironments": ["prod"]
}
```

## Troubleshooting

**"Connection refused"** — Verify the target service is running and reachable from the Portway host. Check port numbers and firewall rules.

**"Method not allowed"** — Verify the HTTP method is listed in `Methods`.

**URL rewriting issues** — If clients receive internal hostnames in responses, check whether the internal service generates absolute URLs in its response body.

**Slow responses** — Enable request traffic logging to measure where latency is occurring:

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

- [Composite Endpoints](./endpoints-composite) — orchestrate multiple proxy steps
- [Environments](./environments) — configure per-environment headers and auth
- [Security](./security)
