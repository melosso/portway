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

`allowedHosts` must contain the target host of `environments/network-access-policy.json` ([Network access policy](/guide/environments#network-access-policy)). Backends with NTLM authentication (e.g. Exact Globe+, Exact Synergy) require a domain service account ([Deploying on Windows Server](/guide/deployment-windows#3-configure-iis)).

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

### Endpoint headers

`Headers` in `entity.json` adds headers to every forwarded request. Values replace environment headers and client headers of the same name, and accept a [secret reference](/reference/secrets#secret-store). `Authorization` is allowed. Hop-by-hop, `Host`, `X-Forwarded-*` and `Content-*` names are a load error.

```json
{
  "Url": "https://erp.internal/api/orders?company=${secret:ERP-COMPANY}",
  "Methods": ["GET", "POST"],
  "Headers": { "Authorization": "Bearer ${secret:ERP-TOKEN}" }
}
```

A secret reference in `Url` or `FallbackUrls` must be a query value. A reference in any other position is a load error. A client query parameter with the same name as a configured one is dropped.

### Redirects

A request with a secret reference, or with an environment or endpoint header whose name contains `key`, `token`, `auth`, `secret`, `password` or a similar term, follows redirects to the same host and port, and from `http` to `https` on the same host, for at most 5 hops. Every hop is checked against the network access policy. Any other redirect is returned to the client. `303`, and `301` or `302` after a `POST`, continue as `GET` without a body; `307` and `308` resend the method and body. Other requests follow redirects.

### OData on proxy endpoints

OData options, `$expand` included, are forwarded as written and never parsed. `$expand` processing applies to [SQL Table and View endpoints](/reference/expand).

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

GET responses are cached for `Caching:DefaultCacheDurationSeconds` (default `300`). The cache key includes the URL, query string, `Authorization` header and selected tenant values. POST, PUT, PATCH, MERGE and DELETE bypass the cache and invalidate the endpoint's cached reads. A composite write step invalidates the cached reads of the step's proxy endpoint ([Caching](/reference/caching#cache-invalidation)).

## Troubleshooting

| Symptom | Resolution |
|---|---|
| "Connection refused" | Check that the target service is reachable from the Portway host (port, firewall). |
| "Method not allowed" | Add the method to `Methods`. |
| Internal hostnames in responses | The internal service returns absolute URLs outside the rewritten link format. |
| Slow responses | Enable [traffic logging](/guide/monitoring#request-traffic-logging) and compare durations. |
| Host not allowed | Add the host to `allowedHosts` in `network-access-policy.json`. |
