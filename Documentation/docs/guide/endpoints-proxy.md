---
title: Proxy Endpoints
description: "Forward requests to internal HTTP/HTTPS services through a consistent, authenticated gateway URL"
---

# Proxy Endpoints

Proxy endpoints forward requests to an internal service and return its responses. Portway adds token authentication, environment headers and URL rewriting.

## Configuration

Each endpoint is defined in its own `entity.json`:

```json [endpoints/Proxy/{EndpointName}/entity.json]
{
  "Url": "http://internal-service:8080/api/resource",
  "Methods": ["GET", "POST", "PUT", "DELETE"],
  "AllowedEnvironments": ["dev", "test", "prod"]
}
```

The target host must be listed in `allowedHosts` of `environments/network-access-policy.json` ([Network access policy](/guide/environments#network-access-policy)). Backends with NTLM authentication (e.g. Exact Globe+, Exact Synergy) require a domain service account ([Deploying on Windows Server](/guide/deployment-windows#3-configure-iis)).

Fallback URLs, retries, response transforms, delete patterns and method translation: [Entity configuration](/reference/entity-config#proxy).

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

GET responses are cached for `Caching:DefaultCacheDurationSeconds` (default `300`). The cache key includes the URL, query string, `Authorization` header and selected tenant values. POST, PUT, DELETE and PATCH bypass the cache and invalidate cached GET responses for the endpoint.

## Troubleshooting

| Symptom | Resolution |
|---|---|
| "Connection refused" | Check that the target service is reachable from the Portway host (port, firewall). |
| "Method not allowed" | Add the method to `Methods`. |
| Internal hostnames in responses | The internal service returns absolute URLs outside the rewritten link format. |
| Slow responses | Enable [traffic logging](/guide/monitoring#request-traffic-logging) and compare durations. |
| Host not allowed | Add the host to `allowedHosts` in `network-access-policy.json`. |
