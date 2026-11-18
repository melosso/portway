---
title: Rate Limiting
description: "Control request volume per IP address and per authentication token"
---

# Rate Limiting

Rate limiting is enabled by default and applies two limits in sequence: per IP address on all traffic, then per token on requests with a Bearer token. Both use a token bucket with continuous refill: each request takes one token from the client's bucket, and bursts are accepted while the average rate stays within the limit.

Rate limiting runs before token authentication. Invalid tokens consume from their bucket; floods of bad credentials do not reach token verification.

## Configuration

```json
{
  "RateLimiting": {
    "Enabled": true,
    "IpLimit": 100,
    "IpWindow": 60,
    "TokenLimit": 1000,
    "TokenWindow": 60,
    "Store": "Memory"
  }
}
```

| Field | Description | Default |
|---|---|---|
| `Enabled` | Enable or disable rate limiting globally | `true` |
| `IpLimit` | Maximum requests per IP per window | `100` |
| `IpWindow` | Window duration in seconds for IP limits | `60` |
| `TokenLimit` | Maximum requests per token per window | `1000` |
| `TokenWindow` | Window duration in seconds for token limits | `60` |
| `Store` | Bucket storage backend, `Memory` or `Redis` | `Memory` |
| `RedisConnectionString` | Connection string for the Redis store | none |

The `Memory` store keeps bucket state in process memory; behind a load balancer, limits apply per instance.

## Per-token limits

A per-token limit overrides `TokenLimit`, e.g. `5000` per `60` seconds for a bulk integration or `10` per minute for a third party. The limit is set in the token create or edit drawer under **Access Tokens**; an empty field uses the global limit. Changes apply within about 30 seconds without a restart and are recorded in the token's audit log.

Token API fields:

```json
{
  "username": "partner-sync",
  "rate_limit_requests": 5000,
  "rate_limit_window_seconds": 60
}
```

## Distributed deployments with Redis

The Redis store shares bucket state across instances:

```json
{
  "RateLimiting": {
    "Store": "Redis",
    "RedisConnectionString": "localhost:6379"
  }
}
```

An empty `RedisConnectionString` reuses the [caching](/reference/caching) connection string. Buckets are evaluated atomically on the Redis server. When Redis is unreachable, the in-memory store is used with per-instance limits.

## Rate limit response


```http
HTTP/1.1 429 Too Many Requests
Retry-After: 45
X-RateLimit-Limit: 100
X-RateLimit-Remaining: 0
X-RateLimit-Reset: 1616161616
X-RateLimit-Resource: token

{
  "error": "Too many requests",
  "retrytime": "2024-03-07T12:34:56Z",
  "success": false
}
```

`Retry-After` is the wait in seconds; `retrytime` is the same moment as an ISO timestamp. Clients retry after `Retry-After`. Successful responses also include the `X-RateLimit-*` headers. Header reference: [Headers](/reference/headers).

An IP address that exceeds its bucket is blocked for the full window. For a token, the block duration doubles with each consecutive violation after the third, up to one hour.

## Observing the limiter

The console dashboard's Rate Limiting card lists the active store, the limits and blocked IP addresses and tokens. JSON: `/ui/api/ratelimits`.

## Troubleshooting

| Symptom | Resolution |
|---|---|
| Legitimate clients receive `429` | Raise `IpLimit`, or give the affected token its own limit. |
| No rate limiting | Check `"Enabled": true` and restart. |
| Limits reset | The `Memory` store resets on restart; the Redis store keeps state. |

Rate limit events are logged at `Warning`:

```
Token rate limit exceeded for abcd...wxyz - Attempt 2
IP 192.168.1.100 has exceeded rate limit, blocking for 60s
```

Debug logging:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug"
    }
  }
}
```

## Next steps

- [Security](/guide/security)
- [Health and Logs](/guide/monitoring)
