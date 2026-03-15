# Monitoring

> Health checks, traffic logging, and connection pool visibility for a running Portway instance.

Portway exposes health check endpoints for uptime probes and optionally logs per-request traffic to file or SQLite. SQL connection pool statistics are emitted to the application log on a scheduled interval.

## Health checks

### Basic health check

```http
GET /health
```

Returns a cached status report valid for 15 seconds:

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

Returns `Alive` with a 5-second cache. Use this for Kubernetes or load balancer liveness probes.

### Detailed health check

```http
GET /health/details
Authorization: Bearer <token>
```

Requires authentication. Returns per-component status with timing:

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

Traffic logging records per-request metadata to file or SQLite. It is disabled by default.

Configure in `appsettings.json`:

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

For production deployments where you want to query traffic data, use SQLite:

```json
{
  "RequestTrafficLogging": {
    "StorageType": "sqlite",
    "SqlitePath": "log/traffic_logs.db"
  }
}
```

Example queries against the SQLite log:

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

Configure component-level verbosity in `appsettings.json`:

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

Application logs rotate daily: `portwayapi-20250503.log`. Traffic logs rotate by file size: `proxy_traffic_20250503_143000.json`.

## SQL connection pool metrics

Connection pool statistics are logged every 10 minutes at Information level:

```
SQL Connection Pool Status: Active connections: 12, Available: 88
```

Configure pool sizing in `appsettings.json`:

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

Portway's `/health/details` endpoint can be scraped directly:

```yaml
scrape_configs:
  - job_name: "portway"
    metrics_path: "/health/details"
    bearer_token: "your-monitoring-token"
    scrape_interval: 30s
    static_configs:
      - targets: ["portway.yourdomain.com"]
```

## Troubleshooting

**Missing traffic logs**: Verify `Enabled: true`, check write permissions on the log directory, and confirm the queue has not been exhausted (`QueueCapacity` in config).

**Health check degraded**: `GET /health/details` reports which check failed and its duration. Disk space and proxy endpoint connectivity are the most common failure sources.

**High response times**: Enable traffic logging with `StorageType: sqlite` and query `DurationMs` to identify slow endpoints.

```powershell
# Check disk space on Windows
Get-PSDrive -PSProvider FileSystem

# Review recent errors in application log
Select-String -Path ".\log\*.log" -Pattern "\[ERR\]" | Select-Object -Last 50
```

## Next steps

- [Rate Limiting](./rate-limiting)
- [Security](./security)
- [Deployment](./deployment)
