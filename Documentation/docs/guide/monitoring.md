---
title: Monitoring
description: "Health checks, traffic logging, and connection pool visibility for a running Portway instance"
---

# Monitoring

Knowing your gateway is healthy shouldn't require guesswork. Portway gives you a few complementary ways to keep an eye on things: health check endpoints for your uptime probes, optional per-request traffic logging to file or SQLite, and SQL connection pool statistics written to the application log on a schedule. Let's walk through each of them, starting with the endpoints your monitoring tools will call most often.

## Health checks

The health endpoints are designed to be cheap to call, so you can point your probes at them without worrying about load. Each one caches its result briefly, which keeps aggressive polling from touching your backends on every request.

### Basic health check

```http
GET /health
```

This is the endpoint most uptime monitors will want. It returns a cached status report that stays valid for 15 seconds:

```json
{
  "status": "Healthy",
  "timestamp": "2025-05-03T10:30:00Z",
  "cache_expires_in": "15 seconds"
}
```

### Liveness probe

```http
GET /health/live
```

This one simply answers `Alive` with a 5-second cache. It is the natural choice for Kubernetes liveness probes or load balancer checks, since it only confirms the process is responsive without touching any downstream services.

### Detailed health check

```http
GET /health/details
Authorization: Bearer <token>
```

When something looks off, this is where you get the full picture. It reports every component check individually with its timing, so you can see at a glance whether it is disk space, a proxy target, or a database connection that is dragging the status down. Because it exposes internal details, it asks for a valid token:

```json
{
  "status": "Healthy",
  "timestamp": "2025-05-03T10:30:00Z",
  "cache_expires_in": "60 seconds",
  "version": "1.0.0",
  "checks": [
    {
      "name": "Diskspace",
      "status": "Healthy",
      "description": "Disk space: 65% remaining",
      "duration": "2.45ms",
      "tags": ["storage", "system"]
    },
    {
      "name": "ProxyEndpoints",
      "status": "Healthy",
      "description": "All proxy services are responding",
      "duration": "145.32ms",
      "tags": ["proxies", "external", "readiness"]
    }
  ],
  "totalDuration": "147.77ms"
}
```

## Request traffic logging

Sometimes you want more than a health status: you want to know exactly which requests came through, how long they took, and who sent them. Traffic logging records that per-request metadata to file or SQLite. It is disabled by default, since not every deployment needs it, and you can enable it in `appsettings.json` whenever the question comes up:

```json
{
  "RequestTrafficLogging": {
    "Enabled": true,
    "StorageType": "file",
    "LogDirectory": "log/traffic",
    "MaxFileSizeMB": 50,
    "MaxFileCount": 5,
    "FilePrefix": "proxy_traffic_",
    "BatchSize": 100,
    "FlushIntervalMs": 1000,
    "IncludeRequestBodies": false,
    "IncludeResponseBodies": false,
    "MaxBodyCaptureSizeBytes": 4096,
    "CaptureHeaders": true
  }
}
```

### Configuration options

| Field | Description | Default |
|---|---|---|
| `Enabled` | Enable traffic logging | `false` |
| `StorageType` | `file` or `sqlite` | `file` |
| `LogDirectory` | Output directory for file storage | `log/traffic` |
| `MaxFileSizeMB` | Maximum size per log file before rotation | `50` |
| `MaxFileCount` | Number of rotated log files to retain | `5` |
| `BatchSize` | Records to buffer before writing | `100` |
| `FlushIntervalMs` | Maximum milliseconds before a partial batch is flushed | `1000` |
| `IncludeRequestBodies` | Log request bodies | `false` |
| `IncludeResponseBodies` | Log response bodies | `false` |
| `MaxBodyCaptureSizeBytes` | Maximum body size to capture | `4096` |
| `CaptureHeaders` | Include request headers in log entries | `true` |

:::warning
`IncludeRequestBodies` and `IncludeResponseBodies` can capture sensitive data. Authorization headers are automatically redacted, but request and response bodies are not filtered.
:::

### Log entry format

```json
{
  "Timestamp": "2025-05-03T10:30:00Z",
  "Method": "GET",
  "Path": "/api/prod/Products",
  "QueryString": "?$top=10",
  "Environment": "prod",
  "EndpointName": "Products",
  "StatusCode": 200,
  "DurationMs": 125,
  "Username": "api-user",
  "ClientIp": "192.168.1.100",
  "TraceId": "a1b2c3d4",
  "RequestHeaders": {
    "Accept": "application/json",
    "Authorization": "[REDACTED]"
  }
}
```

### SQLite storage

File storage is fine for occasional inspection, but if you find yourself wanting to ask questions of your traffic data (which endpoints are slow, where errors cluster), SQLite is the more comfortable choice. It turns your traffic log into a queryable database:

```json
{
  "RequestTrafficLogging": {
    "StorageType": "sqlite",
    "SqlitePath": "log/traffic_logs.db"
  }
}
```

A few queries to get you started; each answers a question you will sooner or later want answered:

```sql
-- Top endpoints by request count (last hour)
SELECT EndpointName, COUNT(*) AS RequestCount
FROM TrafficLogs
WHERE Timestamp > datetime('now', '-1 hour')
GROUP BY EndpointName
ORDER BY RequestCount DESC
LIMIT 10;

-- Average response time by endpoint
SELECT EndpointName, AVG(DurationMs) AS AvgDuration
FROM TrafficLogs
WHERE Timestamp > datetime('now', '-1 hour')
GROUP BY EndpointName
ORDER BY AvgDuration DESC;

-- Error rate by environment (last 24 hours)
SELECT
    Environment,
    COUNT(*) AS TotalRequests,
    SUM(CASE WHEN StatusCode >= 400 THEN 1 ELSE 0 END) AS Errors,
    CAST(SUM(CASE WHEN StatusCode >= 400 THEN 1 ELSE 0 END) AS FLOAT) / COUNT(*) * 100 AS ErrorRate
FROM TrafficLogs
WHERE Timestamp > datetime('now', '-24 hours')
GROUP BY Environment;

-- Slowest requests
SELECT Path, QueryString, DurationMs, StatusCode
FROM TrafficLogs
WHERE DurationMs > 1000 AND Timestamp > datetime('now', '-1 hour')
ORDER BY DurationMs DESC
LIMIT 20;
```

## Log levels

The application log can be tuned per component, which is handy when you want detail from Portway itself without drowning in framework chatter. Verbosity lives in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  }
}
```

Rotation is handled for you: application logs roll over daily (`portwayapi-20250503.log`) while traffic logs roll by file size (`proxy_traffic_20250503_143000.json`).

## SQL connection pool metrics

If your gateway leans heavily on SQL endpoints, the connection pool is worth a periodic glance. Portway writes pool statistics to the application log every 10 minutes at Information level, so a slow pool exhaustion shows up in the log history before it becomes an outage:

```
SQL Connection Pool Status: Active connections: 12, Available: 88
```

Pool sizing can be adjusted in `appsettings.json` if the defaults do not fit your workload:

```json
{
  "SqlConnectionPooling": {
    "Enabled": true,
    "MinPoolSize": 5,
    "MaxPoolSize": 100,
    "ConnectionTimeout": 15,
    "CommandTimeout": 30
  }
}
```

## Prometheus integration

Portway can serve its request metrics on a native Prometheus scrape endpoint. Set the telemetry provider and Prometheus pulls metrics straight from the gateway:

```json
{
  "Telemetry": {
    "Provider": "Prometheus"
  }
}
```

```yaml
scrape_configs:
  - job_name: "portway"
    scrape_interval: 15s
    static_configs:
      - targets: ["portway.yourdomain.com"]
```

The endpoint defaults to `/metrics` and exposes request duration histograms (tagged per endpoint), cache hit rates, and the standard ASP.NET Core server metrics. The [Telemetry](/guide/opentelemetry) guide covers the full metric list, path configuration, and the OTLP push alternative.

## Troubleshooting

A few situations come up often enough to mention here.

**Missing traffic logs**: usually one of three things. Check that `Enabled` is `true`, that the process can write to the log directory, and that the queue has not been exhausted (`QueueCapacity` in the configuration).

**Health check degraded**: `GET /health/details` tells you which check failed and how long it took. Disk space and proxy endpoint connectivity are the most common culprits, so those are good places to look first.

**High response times**: enabling traffic logging with `StorageType: sqlite` and querying `DurationMs` will quickly show you which endpoints are slow, and whether the slowness is broad or concentrated.

::: code-group

```powershell [PowerShell]
# Check disk space
Get-PSDrive -PSProvider FileSystem

# Review recent errors in application log
Select-String -Path ".\log\*.log" -Pattern "\[ERR\]" | Select-Object -Last 50
```

```bash [Bash]
# Check disk space
df -h

# Review recent errors in application log
grep -h "\[ERR\]" ./log/*.log | tail -n 50
```

:::

## Next steps

- [Rate Limiting](/guide/rate-limiting)
- [Security](/guide/security)
- [Deployment](/guide/deployment)
