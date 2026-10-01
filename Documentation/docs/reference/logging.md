---
title: Logging
description: "Serilog configuration for log levels, file rotation and log output"
---

# Logging

Portway logs through Serilog, configured in the `Serilog` section of `appsettings.json`. Request traffic logging is separate: [Auditing](/reference/audit).

## Default configuration

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.EntityFrameworkCore.Database.Command": "Warning",
        "System": "Warning",
        "Microsoft.AspNetCore": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "log/portwayapi-.log",
          "rollingInterval": "Day",
          "fileSizeLimitBytes": 10485760,
          "rollOnFileSizeLimit": true,
          "retainedFileCountLimit": 10,
          "buffered": true,
          "flushToDiskInterval": "00:00:30"
        }
      }
    ]
  }
}
```

## Outputs

| Output | Behavior |
|---|---|
| Console | `[time level] message`, with exception details |
| File | `log/portwayapi-YYYYMMDD.log`; daily rotation and at 10MB; 10 files retained; buffered, flushed every 30 seconds |

## Levels

| Level | Content |
|---|---|
| `Debug` | Queries, endpoint trees, pool and provider details, per-endpoint health failures |
| `Information` | Startup summary and operational events |
| `Warning` | Handled problems: rate limit blocks, configuration fallbacks, unhealthy upstreams |
| `Error` | Failures and exceptions |
| `Fatal` | Startup failures |

The `Default` level applies to Portway's events, which have no source context; `Override` entries apply to framework namespaces.

## Examples

```
[DBG] Incoming request: POST /api/500/Orders
[DBG] Outgoing response: 200 for /api/500/Orders - Took 125ms
[INF] Rate limiting enabled. Store: InMemoryRateLimiterStore, IP: 100/60s, Token: 1000/60s
[WRN] IP 192.168.1.100 has exceeded rate limit, blocking for 60s
```

Secrets, tokens and environment authentication values are not written to logs; rate limit keys are hashed.

## Log queries

::: code-group

```powershell [PowerShell]
# Errors in today's log
Get-Content "log/portwayapi-$(Get-Date -Format 'yyyyMMdd').log" | Select-String "ERR"

# Log files by date
Get-ChildItem "log" -Filter "*.log" | Sort-Object LastWriteTime -Descending | Select-Object Name, Length
```

```bash [Bash]
# Errors in today's log
grep "ERR" "log/portwayapi-$(date +%Y%m%d).log"

# Log files by size
ls -lhtS log/*.log
```

:::

## Related topics

- [Monitoring](/guide/monitoring)
- [Auditing](/reference/audit)
- [Application Settings](/reference/app-settings)
