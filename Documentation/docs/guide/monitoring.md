---
title: Health and Logs
description: "Health checks, traffic logging, and connection pool visibility for a running Portway instance"
---

# Health and Logs

Monitoring sources: health check endpoints, optional per-request traffic logging to file or SQLite, and SQL connection pool statistics in the application log.

## Health checks

Health endpoints cache their result, so frequent polling does not reach the backends on every request.

### Basic health check

```http
GET /health
```

Overall status, cached for 15 seconds:

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

Returns `Alive`, cached for 5 seconds, without checking downstream services. Intended for liveness probes and load balancers.

### Detailed health check

```http
GET /health/details
Authorization: Bearer <token>
```

Each component check with its status and duration, cached for 60 seconds. Requires a token:

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

Traffic logging records per-request metadata (path, status, duration, user, client IP) to file or SQLite. Disabled by default; enabled in `appsettings.json`:

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

All fields, including SQLite storage and retention: [Audit and traffic logging](/reference/audit#configuration).

:::warning
With `IncludeRequestBodies` and `IncludeResponseBodies`, bodies are logged unfiltered. Authorization headers are always redacted.
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

SQLite storage makes the traffic log queryable:

```json
{
  "RequestTrafficLogging": {
    "StorageType": "sqlite",
    "SqlitePath": "log/traffic_logs.db"
  }
}
```

Example queries:

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

The `Serilog` section of `appsettings.json` sets log levels. The `Default` level applies to Portway's events; `Override` entries apply to framework namespaces:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft.AspNetCore": "Warning",
        "Microsoft.EntityFrameworkCore": "Warning"
      }
    }
  }
}
```

Overrides match a log event's source context. Portway's events have no source context and follow `Default`; `Debug` enables detailed traces.

Application logs rotate daily (`portwayapi-20250503.log`); traffic logs rotate by file size (`proxy_traffic_20250503_143000.json`).

## SQL connection pool metrics

Pool statistics are logged every 10 minutes at `Information`:

```
SQL Connection Pool Status: Active connections: 12, Available: 88
```

Pool sizing: `SqlConnectionPooling` in [Application Settings](/reference/app-settings#sqlconnectionpooling).

Metrics for Prometheus and OTLP: [Telemetry](/guide/telemetry).

## Troubleshooting

| Symptom | Resolution |
|---|---|
| No traffic logs | Check `Enabled: true`, write access to the log directory and `QueueCapacity`. |
| Health check degraded | `GET /health/details` names the failing check; common causes are disk space and proxy targets. |
| High response times | Log traffic to SQLite and query `DurationMs` per endpoint. |

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
