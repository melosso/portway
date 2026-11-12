---
title: Health and Logs
description: "Health checks, traffic logging, and connection pool visibility for a running Portway instance"
---

# Health and Logs

Monitoring sources: health check endpoints, optional per-request traffic logging to file or SQLite, and SQL connection pool statistics in the application log.

## Health checks

| Path | Authentication | Cache | Content |
|---|---|---|---|
| `/health` | None | 15 s | Overall status |
| `/health/live` | None | 5 s | `Alive`, without downstream checks; for liveness probes and load balancers |
| `/health/details` | Token | 60 s | Each component check with status and duration |

Response format, component checks and probe configuration: [Health Checks](/reference/health-checks).

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

All fields, including SQLite storage and retention: [Audit and traffic logging](/reference/audit#configuration).

:::warning
With `IncludeRequestBodies` and `IncludeResponseBodies`, bodies are logged unfiltered. `Authorization`, cookies and the headers and query parameters of an environment's `Authentication.Methods` are redacted.
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

SQLite storage:

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

```powershell [Windows]
# Check disk space
Get-PSDrive -PSProvider FileSystem

# Review recent errors in application log
Select-String -Path ".\log\*.log" -Pattern "\[ERR\]" | Select-Object -Last 50
```

```bash [Linux]
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
