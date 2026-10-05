---
title: Troubleshooting
description: "Diagnose and resolve authentication, rate limiting, connectivity, health and performance issues in a Portway deployment"
---

# Troubleshooting

## Status codes

| Status | Message | Cause | Resolution |
|---|---|---|---|
| `400` | "Environment '{env}' is not allowed" | The environment is not routable for the endpoint | Add it to `AllowedEnvironments` in `environments/settings.json` and the endpoint's `entity.json` |
| `400` | "Header {name} is required" | Tenant endpoint, token with several tenant values, header missing | Send the [tenant header](/guide/tenant-headers) |
| `401` | "Invalid or expired token" | Missing, malformed, expired or archived token | Check the `Authorization` header and the token under **Access Tokens** |
| `403` | "Access denied to endpoint" / "to environment" | Token scopes or environments exclude the request | Edit the token under **Access Tokens** |
| `403` | Tenant access refused | Token has no value for the tenant header, or the header names another value | Add the value to the token's tenants |
| `404` | "Endpoint '{name}' not found" | No loaded endpoint configuration for the path | Check the file path and the startup log for load errors |
| `429` | "Too many requests" | IP or token rate limit exceeded | Wait for `Retry-After`, or adjust the limits |
| `500` | "A data error occurred" | Database unreachable or query failure | Check the connection string and database access; the log entry has the reference id |
| none | Blank page | TLS certificate problem | Bind a certificate in IIS or check TLS termination in front of the container |

## Authentication

A `401` means the token could not be verified. The header format:

```http
Authorization: Bearer YOUR_TOKEN
```

Check under **Access Tokens** that the token exists and is neither expired nor archived. Failures from one integration indicate an outdated token in that deployment. Failures across many clients indicate a gateway change.

A `403` means the token is valid but its scopes, environments or tenants exclude the request. Compare the token's scopes and environments under **Access Tokens** with the endpoint's `AllowedEnvironments` and the [scope patterns](/guide/tokens#scoping-tokens).

## Rate limiting

Rate limit events in the log:

::: code-group

```powershell [Windows]
Select-String -Path ".\log\*.log" -Pattern "Rate limit" | Select-Object -Last 20
```

```bash [Linux]
grep -h "Rate limit" ./log/*.log | tail -n 20
```

:::

A single client or IP address hitting the limit needs backoff in its retry logic or its own token limit; general growth needs higher limits. A restart resets all counters of the `Memory` store:

::: code-group

```bash [Linux]
sudo systemctl restart portway
```

```bash [Docker]
docker compose restart portway
```

```powershell [Windows]
Restart-WebAppPool -Name "PortwayAppPool"
```

:::

## Database connections

SQL endpoints return `500` when the database is unreachable.

Connectivity test from the gateway host:

::: code-group

```powershell [Windows]
$conn = New-Object System.Data.SqlClient.SqlConnection
$conn.ConnectionString = "Server=YOUR_SERVER;Database=500;Trusted_Connection=True;"
try {
    $conn.Open()
    Write-Host "Connection successful"
} catch {
    Write-Host "Connection failed: $_"
} finally {
    $conn.Close()
}
```

```bash [Linux]
sqlcmd -S YOUR_SERVER -d 500 -Q "SELECT 1" && echo "Connection successful"
```

:::

Intermittent failures under load indicate an undersized pool (`MaxPoolSize`). Queries that stop at exactly `CommandTimeout` need a higher timeout or a faster query. Settings: [`SqlConnectionPooling`](/reference/app-settings#sqlconnectionpooling).

## Proxy endpoints

Unreachable upstreams return timeouts, "Error processing endpoint" or `503`. Direct test from the gateway host:

::: code-group

```powershell [Windows]
Invoke-WebRequest -Uri "http://localhost:8020/services/Exact.Entity.REST.EG/Account" -UseDefaultCredentials
```

```bash [Linux]
curl -I http://localhost:8020/services/Exact.Entity.REST.EG/Account
```

:::

When the direct request succeeds, compare the endpoint's `Url` and the environment's `settings.json` headers with what the upstream expects. Blocked hosts: [Network access policy](/guide/environments#network-access-policy).

## Health checks

The detailed health check (`GET /health/details`, with a token) names the failing check. The startup log lists unhealthy proxy endpoints in one `Health check status: Unhealthy proxy endpoints detected` warning; the reason per endpoint is logged at `Debug`.

### Disk space

Low disk space reports `Unhealthy` and eventually stops log writes:

::: code-group

```powershell [Windows]
Get-PSDrive -PSProvider FileSystem
Get-ChildItem ".\log" -Recurse -File |
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } |
    Remove-Item -Force
```

```bash [Linux]
df -h
find ./log -type f -mtime +30 -delete
```

:::

Traffic log rotation:

```json
{
  "RequestTrafficLogging": {
    "MaxFileSizeMB": 50,
    "MaxFileCount": 5
  }
}
```

## Performance

Durations above `1000ms` indicate a database, network or host problem. With SQLite traffic logging:

```sql
SELECT Path, QueryString, DurationMs, StatusCode
FROM TrafficLogs
WHERE DurationMs > 1000
ORDER BY DurationMs DESC
LIMIT 20;
```

## Logs

| Log | Location | Contents |
|---|---|---|
| Application | `./log/portwayapi-*.log` | Startup, errors and events |
| Traffic (file) | `./log/traffic/proxy_traffic_*.json` | Per-request metadata |
| Traffic (SQLite) | `./log/traffic_logs.db` | Queryable per-request metadata |
| Authentication | `./auth.db` | Tokens, accounts and audits |

Recent errors and a live tail:

::: code-group

```powershell [Windows]
Select-String -Path ".\log\*.log" -Pattern "\[ERR\]|\[FTL\]" | Select-Object -Last 50
Get-Content ".\log\portwayapi-$(Get-Date -Format 'yyyyMMdd').log" -Wait -Tail 50
```

```bash [Linux]
grep -hE "\[ERR\]|\[FTL\]" ./log/*.log | tail -n 50
tail -n 50 -f "./log/portwayapi-$(date +%Y%m%d).log"
```

:::

Log message patterns:

```text
[WRN] Rate limit enforced for {Identifier}, retry after {Seconds}s (at {Time})
[WRN] Tokens detected in the tokens directory; relocate them to a secure location
[DBG] SQL Query Request: {Url}
```

## Application not starting

Host state and startup output:

::: code-group

```bash [Linux]
systemctl status portway
journalctl -u portway -n 100
```

```bash [Docker]
docker compose ps
docker compose logs --tail=100 portway
```

```powershell [Windows]
Get-EventLog -LogName Application -Source "IIS*" -Newest 20
Get-WebAppPoolState -Name "PortwayAppPool"
```

:::

Startup errors in the application log:

::: code-group

```bash [Linux]
grep -hE "\[ERR\]|\[FTL\]" ./log/portwayapi-*.log | head -50
```

```powershell [Windows]
Select-String -Path ".\log\portwayapi-*.log" -Pattern "\[ERR\]|\[FTL\]" | Select-Object -First 50
```

:::

## Related topics

- [Health and Logs](/guide/monitoring)
- [Security](/guide/security)
- [Deployment](/guide/deployment)
- [SQL Endpoints](/guide/endpoints-sql)
