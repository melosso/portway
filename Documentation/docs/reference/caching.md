---
title: Caching
description: "Configuration reference for Portway's in-memory and Redis caching"
---

# Caching

Successful GET and QUERY responses from SQL, proxy and static endpoints are cached in memory or Redis. Only `2xx` responses with a cacheable content type are stored.

## Providers

| Provider | Use case |
|----------|----------|
| `Memory` | Single instance; cleared on restart. Each process keeps its own entries: a write on one instance does not clear the cache of another instance. Startup logs a notice when `Memory` is active |
| `Redis` | Shared across instances; kept across restarts |

## Configuration

```json
{
  "Caching": {
    "Enabled": true,
    "DefaultCacheDurationSeconds": 300,
    "Provider": "Memory",
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
| `Provider` | string | `"Memory"` | Cache backend: `"Memory"` or `"Redis"` |
| `MemoryCacheSizeLimitMB` | integer | `100` | Memory budget in MB; entries are evicted once cached payloads exceed it |
| `CacheableContentTypes` | array | `["application/json", ...]` | Only cache responses with these content types |
| `EndpointCacheDurations` | object | `{}` | Per-endpoint TTL overrides keyed by endpoint name |

### Redis configuration

```json
{
  "Caching": {
    "Provider": "Redis",
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

GET and QUERY responses from SQL and proxy endpoints with a `2xx` status and a content type in `CacheableContentTypes`. POST, PUT, PATCH, MERGE and DELETE responses and error responses are not cached. Static endpoints cache the content file; the cache key includes the file's last write time, so an edited file is served on the next request.

### Cache keys

Proxy keys combine the URL and query string, environment, endpoint name, a hash of the `Authorization` header, the selected tenant values and `Accept-Language`. SQL keys combine the environment, endpoint name and the generated SQL with its parameters, which include tenant predicates.

### Cache invalidation

Entries expire after their TTL. Writes invalidate cached reads before the TTL expires:

| Write | Invalidated reads |
|-------|-------------------|
| SQL write (table or procedure) | Every SQL endpoint on the same table or view, plus every endpoint that lists the table in `CacheInvalidatedBy` |
| Webhook insert | Every SQL endpoint on the webhook table, plus every endpoint that lists the table in `CacheInvalidatedBy` |
| Proxy write (POST, PUT, PATCH, MERGE, DELETE, SOAP) | The proxy endpoint |
| Composite write step | The proxy endpoint of the step |

A write invalidates reads even when the writing endpoint has `CacheEnabled: false`. Writes made outside Portway (direct database changes, other clients of an upstream) are visible after the TTL.

### Per-endpoint settings

SQL endpoints accept these keys in `Properties` ([Entity configuration](/reference/entity-config)):

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `CacheEnabled` | boolean | `true` | `false` bypasses the response cache for this endpoint |
| `CacheDurationMinutes` | integer | `5` | TTL for this endpoint's cached reads |
| `CacheInvalidatedBy` | string[] | | Tables whose writes invalidate this endpoint's cached reads. Entries are `Table` or `Schema.Table`; `Table` uses the endpoint's `DatabaseSchema` |

A view, table-valued function or procedure endpoint reads or writes tables other than its `DatabaseObjectName`. List those tables in `CacheInvalidatedBy`:

```json
{
  "DatabaseObjectName": "ActiveItems",
  "DatabaseObjectType": "View",
  "Properties": {
    "CacheInvalidatedBy": ["dbo.Items", "dbo.Categories"]
  }
}
```

A procedure endpoint that writes `dbo.Items` lists `dbo.Items`; its writes then invalidate every read that depends on `dbo.Items`. An entry that is not a table name prevents the endpoint from loading.

### Client caching

Authenticated responses include `Cache-Control: private, no-cache`, `Vary: Authorization` and an `ETag`. Clients revalidate each read with `If-None-Match`; an unchanged response returns `304 Not Modified`. A proxy upstream `Cache-Control` value is returned with `public` changed to `private`.

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

The detailed health check (`GET /health/details`) includes the Redis connection status and `PING` round trip (`latency_ms`) in the `redis_cache` check ([Health checks](/reference/health-checks#redis-cache-check)).

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
| No caching | `Caching.Enabled` is `false`, or not a GET or QUERY request | Set `Enabled: true` |
| View or procedure reads are stale after a write | The written table is not in `CacheInvalidatedBy` | Add the table to the endpoint's `CacheInvalidatedBy` |
| Reads are stale on another instance | `Provider` is `Memory` with more than one instance | Set `Provider` to `Redis` |
| Content type not cached | Not in `CacheableContentTypes` | Add the content type |
| Redis connection failures | Wrong connection string or unreachable server | Verify `ConnectionString`; check firewall |
| High memory usage | Long TTL or a generous budget | Reduce `DefaultCacheDurationSeconds` or `MemoryCacheSizeLimitMB` |

## Related topics

- [Health and Logs](/guide/monitoring)
- [Application Settings](/reference/app-settings)
