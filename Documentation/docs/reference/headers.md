---
title: HTTP Headers
description: "Request and response headers Portway reads or writes: authentication, tenants, forwarding, security, rate limiting, caching and compression"
---

# HTTP Headers

## Request headers

| Header | Required | Description |
|---|---|---|
| `Authorization` | Yes, except `/health`, `/health/live` and the Prometheus path | `Bearer <token>` |
| `Content-Type` | With a body | `application/json` for SQL and webhook endpoints; proxy endpoints accept any type |
| `Accept` | No | Response format for content negotiation |
| `Accept-Encoding` | No | `br` or `gzip` response compression |
| `If-None-Match` | No | ETag revalidation |
| Tenant headers | On endpoints with `Tenancy` | Header names from the endpoint's `Tenancy` ([Tenant headers](/guide/tenant-headers)) |

## Proxy forwarding

Client headers are forwarded to proxy upstreams, except:

| Headers | Handling |
|---|---|
| `Host`, `Connection`, `Keep-Alive`, `Proxy-Authenticate`, `Proxy-Authorization`, `TE`, `Trailers`, `Transfer-Encoding`, `Upgrade` | Not forwarded (hop-by-hop) |
| `X-Forwarded-For`, `X-Forwarded-Host`, `X-Forwarded-Proto`, `X-Forwarded-Port`, `X-Real-IP`, `X-Original-For`, `Forwarded` | Not forwarded; `X-Forwarded-For` is set from the verified client address |
| `Content-Length` | Recomputed |
| Environment `Headers` and endpoint `HttpMethodAppendHeaders` | Configured value replaces the client value |
| Tenant headers | Inbound header removed; the upstream header is set to the tenant value |

Environment headers are defined in `environments/{env}/settings.json`:

```json
{
  "Headers": {
    "DatabaseName": "prod",
    "ServerName": "YOUR-APP-SERVER",
    "Origin": "Portway"
  }
}
```

The proxy response's `Server`, `X-Powered-By`, `X-AspNet-Version` and `X-AspNetMvc-Version` headers are removed.

## Security headers

| Header | Value |
|---|---|
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` |
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains` (HTTPS) |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `Permissions-Policy` | `geolocation=(), camera=(), microphone=(), payment=()` |
| `Content-Security-Policy` | `default-src 'self'; object-src 'none'; frame-ancestors 'none'; ...` |

Responses under `/ui` also include `Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Resource-Policy: same-origin`.

## Rate limiting headers

| Header | Description | Example |
|---|---|---|
| `X-RateLimit-Limit` | Requests per window | `100` |
| `X-RateLimit-Remaining` | Remaining requests | `95` |
| `X-RateLimit-Reset` | Unix time of full replenishment | `1616161616` |
| `X-RateLimit-Resource` | Limit that produced the values: `ip` or `token` | `token` |
| `X-RateLimit-Used` | Requests used in the window | `5` |
| `Retry-After` | Seconds until retry; `429` only | `60` |

## Caching headers

Successful `GET` responses under `/api` include a strong `ETag` computed from the body. A matching `If-None-Match` returns `304 Not Modified` without a body:

```http
GET /api/prod/Products?$top=10
If-None-Match: "33a64df551425fcc55e4d42a148795d9f25f89d4..."

HTTP/1.1 304 Not Modified
```

Authenticated responses include `Cache-Control: private, max-age=600` and `Vary: Authorization`. Cached GET responses from endpoints with caching enabled include `Cache-Control` with the endpoint's duration.

## Compression

Responses are compressed with Brotli (`br`) or Gzip (`gzip`) according to `Accept-Encoding`, also over HTTPS.

## CORS

Outside Development, cross-origin requests are allowed only from `WebUi:CorsOrigins`, with any method and header and with credentials. Without configured origins, cross-origin requests are refused. In Development, any origin is allowed.

## Related topics

- [Authentication](/reference/api-auth)
- [Rate Limiting](/guide/rate-limiting)
- [Environment Settings](/reference/environment-settings)
- [Security](/guide/security)
