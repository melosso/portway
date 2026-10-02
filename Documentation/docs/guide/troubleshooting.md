---
title: Troubleshooting
description: "Diagnose and resolve authentication, rate limiting, connectivity, health and performance issues in a Portway deployment"
---

# Troubleshooting

## Status codes

| Status | Message | Cause | Resolution |
|---|---|---|---|
| `400` | "Environment '{env}' is not allowed" | The environment is not routable for the endpoint | Add it to `AllowedEnvironments` in `environments/settings.json` and the endpoint's `entity.json` |
| `400` | "Header {name} is required" | Tenant endpoint, token with several tenant values, header missing | Send the [tenant header](/guide/security#tenant-headers) |
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

Check under **Access Tokens** that the token exists and is neither expired nor archived. Failures from one integration point to an outdated token in that deployment; failures across many clients point to a gateway change.

A `403` means the token is valid but its scopes, environments or tenants exclude the request. The token file (present until the token is archived) lists its scopes and environments:

::: code-group

```powershell [PowerShell]
Get-Content ".\tokens\username.txt" | ConvertFrom-Json | Format-List
```

```bash [Bash]
cat ./tokens/username.txt | jq .
```

:::

Compare them with the endpoint's `AllowedEnvironments` and the token scope patterns in [Access Tokens](/guide/tokens#scoping-tokens).

## Rate limiting

Current limits:

```json
{
  "RateLimiting": {
    "Enabled": true,
    "IpLimit": 100,
    "IpWindow": 60,
    "TokenLimit": 1000,
    "TokenWindow": 60
  }
}
```

Rate limit events in the log:

::: code-group

```powershell [PowerShell]
Select-String -Path ".\log\*.log" -Pattern "Rate limit" | Select-Object -Last 20
```

```bash [Bash]
grep -h "Rate limit" ./log/*.log | tail -n 20
```

:::

A single client or IP address hitting the limit needs backoff in its retry logic or its own token limit; general growth needs higher limits. A restart resets all counters of the `Memory` store:

::: code-group

```bash [Docker]
docker compose restart portway
```

```powershell [IIS]
Restart-WebAppPool -Name "PortwayAppPool"
```

:::

## Database connections

SQL endpoints return `500` when the database is unreachable. Connection string example:

```json
{
  "ConnectionString": "Server=YOUR_SERVER;Database=500;Trusted_Connection=True;Connection Timeout=15;TrustServerCertificate=true;"
}
```

Connectivity test from the gateway host:

::: code-group

```powershell [PowerShell]
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

```bash [Bash]
sqlcmd -S YOUR_SERVER -d 500 -Q "SELECT 1" && echo "Connection successful"
```

:::

Intermittent failures under load indicate an undersized pool (`MaxPoolSize`). Queries that stop at exactly `CommandTimeout` need a higher timeout or a faster query. Settings: [`SqlConnectionPooling`](/reference/app-settings#sqlconnectionpooling).

## Proxy endpoints

Unreachable upstreams return timeouts, "Error processing endpoint" or `503`. Direct test from the gateway host:

::: code-group

```powershell [PowerShell]
Invoke-WebRequest -Uri "http://localhost:8020/services/Exact.Entity.REST.EG/Account" -UseDefaultCredentials
```

```bash [Bash]
curl -I http://localhost:8020/services/Exact.Entity.REST.EG/Account
```

:::

When the direct request succeeds, compare the endpoint's `Url` and the environment's `settings.json` headers with what the upstream expects. Blocked hosts: [Network access policy](/guide/environments#network-access-policy).

## Health checks

The detailed health check (`GET /health/details`, with a token) names the failing check. The startup log lists unhealthy proxy endpoints in one `Health check status: Unhealthy proxy endpoints detected` warning; the reason per endpoint is logged at `Debug`.

### Disk space

Low disk space reports `Unhealthy` and eventually stops log writes:

::: code-group

```powershell [PowerShell]
Get-PSDrive -PSProvider FileSystem
Get-ChildItem ".\log" -Recurse -File |
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } |
    Remove-Item -Force
```

```bash [Bash]
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

Durations above `1000ms` point to the database, the network or the host. With SQLite traffic logging:

```sql
SELECT Path, QueryString, DurationMs, StatusCode
FROM TrafficLogs
WHERE DurationMs > 1000
ORDER BY DurationMs DESC
LIMIT 20;
```

Error rate per endpoint:

```sql
SELECT EndpointName,
       COUNT(CASE WHEN StatusCode >= 400 THEN 1 END) AS Errors,
       COUNT(*) AS TotalRequests,
       ROUND(CAST(COUNT(CASE WHEN StatusCode >= 400 THEN 1 END) AS FLOAT) / COUNT(*) * 100, 2) AS ErrorRate
FROM TrafficLogs
WHERE Timestamp > datetime('now', '-24 hours')
GROUP BY EndpointName
HAVING Errors > 0
ORDER BY ErrorRate DESC;
```

## Logs

| Log | Location | Contents |
|---|---|---|
| Application | `./log/portwayapi-*.log` | Startup, errors and events |
| Traffic (file) | `./log/traffic/proxy_traffic_*.json` | Per-request metadata |
| Traffic (SQLite) | `./log/traffic_logs.db` | Queryable per-request metadata |
| Authentication | `./auth.db` | Tokens, accounts and audits |

Recent errors, most frequent errors and a live tail:

::: code-group

```powershell [PowerShell]
Get-ChildItem ".\log\*.log" |
    Where-Object { $_.LastWriteTime -gt (Get-Date).AddHours(-1) } |
    Select-String -Pattern "ERROR|EXCEPTION"

Get-Content ".\log\portwayapi-$(Get-Date -Format 'yyyyMMdd').log" |
    Select-String -Pattern "ERROR.*?:" |
    Group-Object -Property Line |
    Sort-Object Count -Descending |
    Select-Object Count, Name -First 10

Get-Content ".\log\portwayapi-$(Get-Date -Format 'yyyyMMdd').log" -Wait -Tail 50
```

```bash [Bash]
find ./log -name "*.log" -mmin -60 -exec grep -HnE "ERROR|EXCEPTION" {} +

grep -oE "ERROR[^:]*:" "./log/portwayapi-$(date +%Y%m%d).log" |
    sort | uniq -c | sort -rn | head -n 10

tail -n 50 -f "./log/portwayapi-$(date +%Y%m%d).log"
```

:::

Active tokens in `auth.db`:

```sql
SELECT Id, Username, CreatedAt, ExpiresAt, AllowedScopes, AllowedEnvironments, AllowedTenants
FROM Tokens
WHERE RevokedAt IS NULL
ORDER BY CreatedAt DESC;
```

Log message patterns:

```text
[INF] Rate limit enforced for {Identifier}
[WRN] Tokens detected in the tokens directory. Relocate them to a secure location
[ERR] Error processing endpoint {EndpointName}
[DBG] SQL Query Request: {Url}
```

## Network checks

::: code-group

```powershell [PowerShell]
Test-NetConnection -ComputerName "YOUR_SERVER" -Port 1433

Invoke-WebRequest -Uri "http://localhost:8020/services/Exact.Entity.REST.EG/Account" `
    -UseDefaultCredentials -Method Head

Get-NetTCPConnection -State Listen |
    Where-Object { $_.LocalPort -in @(80, 443, 8080) }
```

```bash [Bash]
nc -zv YOUR_SERVER 1433

curl -I http://localhost:8020/services/Exact.Entity.REST.EG/Account

ss -tlnp | grep -E ':(80|443|8080)\b'
```

:::

## Application not starting

Host state and startup output:

::: code-group

```bash [Docker]
docker compose ps
docker compose logs --tail=100 portway
```

```powershell [IIS]
Get-EventLog -LogName Application -Source "IIS*" -Newest 20
Get-WebAppPoolState -Name "PortwayAppPool"
Restart-WebAppPool -Name "PortwayAppPool"
```

:::

Startup errors in the application log:

::: code-group

```bash [Docker]
grep -E "Application start|FATAL|ERROR" ./log/portwayapi-*.log | head -50
```

```powershell [IIS]
Get-Content ".\log\portwayapi-$(Get-Date -Format 'yyyyMMdd').log" |
    Select-String -Pattern "Application start|FATAL|ERROR" |
    Select-Object -First 50
```

:::

## Resetting application state

::: danger
Back up first. A reset clears all logs.
:::

Backup:

::: code-group

```bash [Docker]
backup="./backup_$(date +%Y%m%d_%H%M%S)"
mkdir -p "$backup"
cp -r ./tokens ./environments ./endpoints ./log "$backup"/
docker compose cp portway:/app/auth.db "$backup"/
```

```powershell [IIS]
$backupDir = ".\backup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
New-Item -ItemType Directory -Path $backupDir

Copy-Item ".\tokens\*" "$backupDir\tokens\" -Recurse
Copy-Item ".\auth.db" "$backupDir\"
Copy-Item ".\environments\*" "$backupDir\environments\" -Recurse
Copy-Item ".\endpoints\*" "$backupDir\endpoints\" -Recurse
```

:::

Reset:

::: code-group

```bash [Docker]
docker compose stop portway
rm -rf ./log/*
docker compose start portway
```

```powershell [IIS]
iisreset /stop
Remove-Item ".\log\*" -Recurse -Force
iisreset /start
```

:::

## Related topics

- [Monitoring](/guide/monitoring)
- [Security](/guide/security)
- [Deployment](/guide/deployment)
- [SQL Endpoints](/guide/endpoints-sql)
