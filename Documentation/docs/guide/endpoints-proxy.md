---
title: Proxy Endpoints
description: "Forward requests to internal HTTP/HTTPS services through a consistent, authenticated gateway URL"
---

# Proxy Endpoints

Proxy endpoints forward requests to an internal service and return its responses. Portway adds token authentication, environment headers and URL rewriting.

:::details Note on pass-through authentication
For backends that require NTLM authentication (e.g. Exact Globe+, Exact Synergy), run the IIS Application Pool under a domain user with the required permissions.
:::

## Configuration

Each endpoint is defined in `endpoints/Proxy/{EndpointName}/entity.json`:

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
| `Url` | Yes | string | Target URL |
| `Methods` | Yes | array | Allowed HTTP methods: `GET`, `POST`, `PUT`, `DELETE`, `PATCH` |
| `Hidden` | No | boolean | Excludes the endpoint from the OpenAPI document (default `false`) |
| `AllowedEnvironments` | No | array | Environments the endpoint serves |
| `Tenancy` | No | object | Tenant header to upstream header; see [Tenant headers](/guide/security#tenant-headers) |

All properties: [Entity configuration](/reference/entity-config#endpoint-proxy).

## Request forwarding

Forwarded unchanged:

- HTTP method
- Query string, including OData options
- Request body and content type
- Request headers, except hop-by-hop headers, `Host`, `Content-Length` and `X-Forwarded-*`

The `X-Forwarded-For` header is set from the verified client connection. The client's `Authorization` header is forwarded unless the environment sets its own.

Environment headers from `environments/{env}/settings.json` and the endpoint's `HttpMethodAppendHeaders` are added to every forwarded request and replace client headers of the same name:

```http
# Added by Portway from environment settings
ServerName: PROD-APP-SERVER
DatabaseName: production
Origin: Portway
```

### OData on proxy endpoints

OData options, `$expand` included, are forwarded as written and never parsed. Portway processes `$expand` only on [SQL Table and View endpoints](/reference/expand).

The `SupportsOData` flag documents `$select`, `$top` and `$filter` on the endpoint's GET operation. Without it, the operation documents that query parameters are forwarded unchanged.

```json
{
  "Url": "http://localhost:8020/services/Exact.Entity.REST.EG/Account",
  "Methods": ["GET"],
  "SupportsOData": true
}
```

## URL rewriting

Internal URLs in responses are rewritten to gateway paths.

Internal service response:

```json
{
  "_links": {
    "self": "http://internal-service:8080/api/users/123",
    "orders": "http://internal-service:8080/api/users/123/orders"
  }
}
```

Response returned to the caller:

```json
{
  "_links": {
    "self": "/api/prod/UserService/123",
    "orders": "/api/prod/UserService/123/orders"
  }
}
```

## Caching

GET responses are cached for 5 minutes by default. The cache key includes the URL, query string, `Authorization` header and selected tenant values. POST, PUT, DELETE and PATCH bypass the cache and invalidate cached GET responses for the endpoint.

## Hidden endpoints

With `Hidden: true`, the endpoint is excluded from the OpenAPI document at `/docs`; the endpoint continues to serve requests.

```json
{
  "Url": "http://admin-service/internal-api",
  "Methods": ["POST"],
  "Hidden": true
}
```

## Examples

Internal API:

```json
{
  "Url": "http://internal-api-gateway:8080/services",
  "Methods": ["GET", "POST"],
  "AllowedEnvironments": ["prod", "staging"]
}
```

Legacy SOAP service, write-only and unlisted:

```json
{
  "Url": "http://legacy-service/soap/endpoint",
  "Methods": ["POST"],
  "Hidden": true,
  "AllowedEnvironments": ["prod"]
}
```

## Troubleshooting

| Symptom | Resolution |
|---|---|
| "Connection refused" | Check that the target service is reachable from the Portway host (port, firewall). |
| "Method not allowed" | Add the method to `Methods`. |
| Internal hostnames in responses | The internal service returns absolute URLs outside the rewritten link format. |
| Slow responses | Enable request traffic logging (below) and compare durations. |

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

- [Composite Endpoints](/guide/endpoints-composite)
- [Environments](/guide/environments)
- [Security](/guide/security)
