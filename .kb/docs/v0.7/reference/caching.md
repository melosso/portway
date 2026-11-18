---
title: Caching
description: "Configuration reference for Portway's in-memory and Redis caching"
---

# Caching

Successful GET responses from SQL and proxy endpoints are cached in memory or Redis. Only `2xx` responses with a cacheable content type are stored.

## Providers

| Provider | Use case |
|----------|----------|
| `Memory` | Single instance; cleared on restart |
| `Redis` | Shared across instances; kept across restarts |

## Configuration

```json
{
  "Caching": {
    "Enabled": true,
    "DefaultCacheDurationSeconds": 300,
    "ProviderType": "Memory",
    "MemoryCacheSizeLimitMB": 100,
    "CacheableContentTypes": [
      "application/json",
      "text/json"
    ],
    "EndpointCacheDurations": {
      "Products": 600,
      "Customers": 300
    }
  }
}
```

### Property reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | boolean | `true` | Enable response caching |
| `DefaultCacheDurationSeconds` | integer | `300` | Default TTL for cached responses |
| `ProviderType` | string | `"Memory"` | Cache backend: `"Memory"` or `"Redis"` |
| `MemoryCacheSizeLimitMB` | integer | `100` | Memory budget in MB; entries are evicted once cached payloads exceed it |
| `CacheableContentTypes` | array | `["application/json", ...]` | Only cache responses with these content types |
| `EndpointCacheDurations` | object | `{}` | Per-endpoint TTL overrides keyed by endpoint name |

### Redis configuration

```json
{
  "Caching": {
    "ProviderType": "Redis",
    "Redis": {
      "ConnectionString": "localhost:6379",
      "InstanceName": "Portway:",
      "Database": 0,
      "UseSsl": false,
      "ConnectTimeoutMs": 5000,
      "AbortOnConnectFail": false,
      "FallbackToMemoryCache": true,
      "MaxRetryAttempts": 3,
      "RetryDelayMs": 200
    }
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ConnectionString` | string | `"localhost:6379"` | Redis connection string |
| `InstanceName` | string | `"Portway:"` | Key prefix for all cache entries |
| `Database` | integer | `0` | Redis logical database index |
| `UseSsl` | boolean | `false` | Use TLS for the Redis connection |
| `ConnectTimeoutMs` | integer | `5000` | Connection timeout in milliseconds |
| `AbortOnConnectFail` | boolean | `false` | Throw on connection failure instead of retrying |
| `FallbackToMemoryCache` | boolean | `true` | Fall back to in-process memory cache if Redis is unavailable |
| `MaxRetryAttempts` | integer | `3` | Retry attempts on transient Redis errors |
| `RetryDelayMs` | integer | `200` | Delay between retry attempts in milliseconds |

## Cache behaviour

### Cached responses

GET responses from SQL and proxy endpoints with a `2xx` status and a content type in `CacheableContentTypes`. POST, PUT, DELETE and PATCH responses and error responses are not cached.

### Cache keys

Proxy keys combine the URL and query string, environment, endpoint name, a hash of the `Authorization` header, the selected tenant values and `Accept-Language`. SQL keys combine the environment, endpoint name and the generated SQL with its parameters, which include tenant predicates.

### Cache invalidation

Entries expire after their TTL. A non-GET request to an endpoint invalidates its entries.

## Cache durations

The TTL is `DefaultCacheDurationSeconds`; `EndpointCacheDurations` overrides it per endpoint. A response with `Cache-Control: max-age=N` uses that value.

## High-Availability Redis

### Sentinel

```json
"Redis": {
  "ConnectionString": "sentinel-master-name,sentinel1:26379,sentinel2:26379",
  "InstanceName": "Portway:"
}
```

### Cluster

```json
"Redis": {
  "ConnectionString": "redis1:6379,redis2:6379,redis3:6379",
  "InstanceName": "Portway:"
}
```

With `FallbackToMemoryCache: true`, an unavailable Redis switches caching to memory with a logged warning; Redis is used again after the connection recovers.

## Cache statistics

The detailed health check (`GET /health/details`) includes item count, hit and miss ratio, memory usage and Redis connection status.

## Troubleshooting

### Redis diagnostic commands

```
redis-cli ping
redis-cli info memory
redis-cli --scan --pattern "Portway:*"
```

### Common issues

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| No caching | `Caching.Enabled` is `false`, or not a GET request | Set `Enabled: true` |
| Content type not cached | Not in `CacheableContentTypes` | Add the content type |
| Redis connection failures | Wrong connection string or unreachable server | Verify `ConnectionString`; check firewall |
| High memory usage | Long TTL or a generous budget | Reduce `DefaultCacheDurationSeconds` or `MemoryCacheSizeLimitMB` |

## Related topics

- [Health and Logs](/guide/monitoring)
- [Application Settings](/reference/app-settings)
