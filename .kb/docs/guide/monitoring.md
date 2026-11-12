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

Response format, component checks and probe configuration: [Health Checks](/reference/health-checks). Request success rate and latency per endpoint: console [Health](/guide/webui#health) page.

## Request traffic logging

Traffic logging records per-request metadata (path, status, duration, user, client IP) to file or SQLite. Disabled by default. Every field is editable on the console [Trace](/guide/webui#trace) page or in `appsettings.json`:

```json
{
  "RequestTrafficLogging": {
    "Enabled": true,
    "Provider": "File",
    "LogDirectory": "log/traffic"
  }
}
```

Fields, entry format, SQLite storage and example queries: [Audit and traffic logging](/reference/audit).

:::warning
`CaptureRequestBodies` and `CaptureResponseBodies` write bodies unfiltered. Webhook request bodies are never captured. Redacted headers: [Header sanitization](/reference/audit#header-sanitization).
:::

## Log levels

The `Serilog` section of `appsettings.json` sets log levels. `Debug` enables detailed traces. Levels and overrides: [Logging](/reference/logging#levels).

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
| Health check degraded | The `GET /health/details` response contains the failing check; common causes are [disk space](/guide/troubleshooting#disk-space) and proxy targets. |
| High response times | Log traffic to SQLite and query `DurationMs` per endpoint. |
