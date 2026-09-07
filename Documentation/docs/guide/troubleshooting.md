---
title: Troubleshooting
description: "A practical guide for diagnosing and resolving issues with Portway gateway deployments, from authentication failures to performance degradation"
---

# Troubleshooting

The issues you are most likely to hit in production, and how to resolve them.

## Common issues

### Authentication failures

Authentication issues come in two forms: requests that cannot be verified at all, and requests with credentials that lack the right permissions.

#### Missing or invalid tokens

A `401 Unauthorized`, or "Authentication required" and "Invalid or expired token" in your logs, means the request arrived without a token the gateway can verify: a missing Authorization header, an expired or revoked token, or a malformed Bearer header.

Check what the client is actually sending. The header should look like this:

```http
Authorization: Bearer YOUR_TOKEN
```

If the header looks right, open the [Web UI](/guide/webui) and go to **Tokens** to confirm the token exists and has not been revoked or expired. If it is stale, create a replacement there and revoke the old one.

When several clients fail at once, the pattern tells you where to look. A cluster of failures from one integration usually points to a deployment shipped with an outdated token, while failures spread across many clients suggest a change on the gateway side.

::: tip Security Best Practice
Tokens are API keys. Storing them in environment variables or a dedicated secret manager, and keeping them out of version control, is recommended.
:::

#### Insufficient token permissions

A `403 Forbidden` means the token is valid but not allowed to do what the request asks. The logs show "Access denied to endpoint" or "Access denied to environment". Either the token lacks the scope for that endpoint, or it does not cover the environment being reached.

You can inspect what a token is allowed to do directly from its file:

::: code-group

```powershell [PowerShell]
# View token file content
Get-Content ".\tokens\username.txt" | ConvertFrom-Json | Format-List
```

```bash [Bash]
# View token file content
cat ./tokens/username.txt | jq .
```

:::

Then compare that against what the endpoint configuration expects:

```json
{
  "AllowedEnvironments": ["prod", "dev"],
  "AllowedScopes": "Products,Orders"
}
```

If the access is legitimate, edit the token in the [Web UI](/guide/webui) under **Tokens** to add the missing scopes or environments.

### Rate limiting issues

`429 Too Many Requests`, or "Rate limit exceeded" and "IP blocked" in the logs, means someone is sending more requests than the configured thresholds allow. That can be genuine high-volume usage, integration testing, or a retry loop without backoff.

Start by checking what the current limits actually are:

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

Then look at who is hitting them:

::: code-group

```powershell [PowerShell]
# Search for rate limit events
Select-String -Path ".\log\*.log" -Pattern "Rate limit" |
    Sort-Object -Property LastWriteTime -Descending |
    Select-Object -First 20
```

```bash [Bash]
# Search for rate limit events
grep -h "Rate limit" ./log/*.log | tail -n 20
```

:::

If the pattern is isolated to one client or IP, exponential backoff in their retry logic fixes it at the source. If legitimate usage has outgrown the thresholds, raising the limits is the better answer.

For immediate relief during an incident, restarting Portway resets all counters:

::: code-group

```bash [Docker]
docker compose restart portway
```

```powershell [IIS]
Restart-WebAppPool -Name "PortwayAppPool"
```

:::

::: warning A note on restarts
Rate limiting uses in-memory token buckets, so restarting resets every counter to zero. That helps in an emergency, but it is not a fix if clients consistently hit limits. Follow up on the request pattern or the configuration.
:::

### Connection issues

#### When your database won't connect

Database connection failures show up as `500 Internal Server Error` on SQL endpoints, when the gateway cannot reach your database or the connection drops.

First verify the connection string is correct and complete:

```json
{
  "ConnectionString": "Server=YOUR_SERVER;Database=500;Trusted_Connection=True;Connection Timeout=15;TrustServerCertificate=true;"
}
```

Then test whether the gateway server can reach the database at all:

::: code-group

```powershell [PowerShell]
# Test SQL connection
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
# Test SQL connection (requires sqlcmd)
sqlcmd -S YOUR_SERVER -d 500 -Q "SELECT 1" && echo "Connection successful"
```

:::

If basic connectivity works but failures persist, a pool that is too small for your traffic shows up as intermittent errors. The `SqlConnectionPooling` properties and their defaults are in [Application Settings](/reference/app-settings#sql-connection-pooling).

#### When proxy endpoints stop responding

Failing proxy endpoints surface as timeout errors, "Error processing endpoint" messages, or `503 Service Unavailable`. This is common with legacy backends where availability is not guaranteed.

Test whether the target service is reachable directly:

::: code-group

```powershell [PowerShell]
# Test endpoint connectivity
Invoke-WebRequest -Uri "http://localhost:8020/services/Exact.Entity.REST.EG/Account" -UseDefaultCredentials
```

```bash [Bash]
# Test endpoint connectivity
curl -I http://localhost:8020/services/Exact.Entity.REST.EG/Account
```

:::

If the direct connection works, check the proxy configuration for the URL and settings:

```json
{
  "Url": "http://localhost:8020/services/Exact.Entity.REST.EG/Account",
  "Methods": ["GET", "POST"],
  "AllowedEnvironments": ["prod", "dev"]
}
```

Environment settings are worth a look too, since they carry what the backend expects:

::: code-group

```powershell [PowerShell]
# Check current environment settings
Get-Content ".\environments\500\settings.json" | ConvertFrom-Json
```

```bash [Bash]
# Check current environment settings
cat ./environments/500/settings.json | jq .
```

:::

### Health check failures

#### When you're running out of disk space

Low storage shows as `"Unhealthy"` status with warnings about remaining disk space. Left alone it causes log write failures and eventually stops the application.

Check how much space is available:

::: code-group

```powershell [PowerShell]
# Check available disk space
Get-PSDrive -PSProvider FileSystem |
    Select-Object Name, @{Name="FreeGB";Expression={[math]::Round($_.Free/1GB,2)}},
                  @{Name="UsedGB";Expression={[math]::Round($_.Used/1GB,2)}},
                  @{Name="TotalGB";Expression={[math]::Round(($_.Free + $_.Used)/1GB,2)}}
```

```bash [Bash]
# Check available disk space
df -h
```

:::

Old log files are usually the quickest win, especially with traffic logging enabled:

::: code-group

```powershell [PowerShell]
# Remove logs older than 30 days
Get-ChildItem ".\log" -Recurse -File |
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } |
    Remove-Item -Force
```

```bash [Bash]
# Remove logs older than 30 days
find ./log -type f -mtime +30 -delete
```

:::

For ongoing space management, configure rotation so it does not recur:

```json
{
  "RequestTrafficLogging": {
    "MaxFileSizeMB": 50,
    "MaxFileCount": 5
  }
}
```

#### When your backend services aren't responding

"One or more proxy services are not responding properly" means the gateway is fine but a backend it depends on is not.

Request a detailed health report to see which services are failing:

```http
GET /health/details
Authorization: Bearer YOUR_TOKEN
```

Then test the problematic endpoints individually:

::: code-group

```powershell [PowerShell]
# Test specific endpoint
$headers = @{
    "Authorization" = "Bearer YOUR_TOKEN"
}
Invoke-RestMethod -Uri "https://your-gateway/api/500/Products" -Headers $headers
```

```bash [Bash]
# Test specific endpoint
curl -H "Authorization: Bearer YOUR_TOKEN" https://your-gateway/api/500/Products
```

:::

For endpoints that keep failing, check their error logs:

::: code-group

```powershell [PowerShell]
# Find endpoint-specific errors
Select-String -Path ".\log\*.log" -Pattern "endpoint: Products" |
    Where-Object { $_ -match "ERROR" }
```

```bash [Bash]
# Find endpoint-specific errors
grep "endpoint: Products" ./log/*.log | grep "ERROR"
```

:::

### Performance issues

High latency, timeouts, or durations over `1000ms` in the logs point to database bottlenecks, network issues, or resource constraints.

Enable detailed traffic logging to see where time is spent:

```json
{
  "RequestTrafficLogging": {
    "Enabled": true,
    "EnableInfoLogging": true
  }
}
```

With SQLite traffic logging you can query the slowest requests directly:

```sql
-- Find slow requests (using SQLite logging)
SELECT Path, QueryString, DurationMs, StatusCode
FROM TrafficLogs
WHERE DurationMs > 1000
ORDER BY DurationMs DESC
LIMIT 20;
```

Database connection management is a frequent cause. If the pool is too small, requests wait for a free connection, and raising `MaxPoolSize` in [`SqlConnectionPooling`](/reference/app-settings#sql-connection-pooling) is where to start. Queries cut off mid-run are a different problem: `CommandTimeout` bounds how long a single statement may run, so a query dying at exactly that mark needs either a higher timeout or a faster query.

## Diagnostic tools

### Understanding your log files

#### Where to find your logs

| Log Type | Default Location | What You'll Find Here |
|----------|-----------------|-------------|
| Application Logs | `./log/portwayapi-*.log` | General application events, errors, and startup information |
| Traffic Logs (File) | `./log/traffic/proxy_traffic_*.json` | Detailed request/response information in JSON format |
| Traffic Logs (SQLite) | `./log/traffic_logs.db` | Queryable database of all traffic for analysis |
| Auth Database | `./auth.db` | Token authentication data and user information |

#### Handy commands for log analysis

To find recent errors across all log files:

::: code-group

```powershell [PowerShell]
# Find all errors in last hour
$oneHourAgo = (Get-Date).AddHours(-1)
Get-ChildItem ".\log\*.log" |
    Where-Object { $_.LastWriteTime -gt $oneHourAgo } |
    Select-String -Pattern "ERROR|EXCEPTION" |
    Format-Table -AutoSize
```

```bash [Bash]
# Find all errors in log files modified in the last hour
find ./log -name "*.log" -mmin -60 -exec grep -HnE "ERROR|EXCEPTION" {} +
```

:::

To see which errors are most common:

::: code-group

```powershell [PowerShell]
# Count errors by type
Get-Content ".\log\portwayapi-$(Get-Date -Format 'yyyyMMdd').log" |
    Select-String -Pattern "ERROR.*?:" |
    Group-Object -Property Line |
    Sort-Object Count -Descending |
    Select-Object Count, Name -First 10
```

```bash [Bash]
# Count errors by type
grep -oE "ERROR[^:]*:" "./log/portwayapi-$(date +%Y%m%d).log" |
    sort | uniq -c | sort -rn | head -n 10
```

:::

For real-time monitoring during active troubleshooting:

::: code-group

```powershell [PowerShell]
# Monitor log file in real-time
Get-Content ".\log\portwayapi-$(Get-Date -Format 'yyyyMMdd').log" -Wait -Tail 50
```

```bash [Bash]
# Monitor log file in real-time
tail -n 50 -f "./log/portwayapi-$(date +%Y%m%d).log"
```

:::

### Database diagnostics

#### Checking authentication status

When a client reports authentication problems, verify their token status:

```sql
-- Using SQLite browser or command line
SELECT Id, Username, CreatedAt, ExpiresAt, AllowedScopes, AllowedEnvironments
FROM Tokens
WHERE RevokedAt IS NULL
ORDER BY CreatedAt DESC;
```

#### Understanding traffic patterns and errors

The traffic logs database shows which endpoints carry the highest error rates:

```sql
-- Error distribution by endpoint
SELECT EndpointName,
       COUNT(CASE WHEN StatusCode >= 400 THEN 1 END) as Errors,
       COUNT(*) as TotalRequests,
       ROUND(CAST(COUNT(CASE WHEN StatusCode >= 400 THEN 1 END) AS FLOAT) / COUNT(*) * 100, 2) as ErrorRate
FROM TrafficLogs
WHERE Timestamp > datetime('now', '-24 hours')
GROUP BY EndpointName
HAVING Errors > 0
ORDER BY ErrorRate DESC;
```

### Network and connectivity diagnostics

These tell you quickly whether the problem is basic connectivity or something inside the application:

::: code-group

```powershell [PowerShell]
# Test connectivity to SQL Server (adjust host/port for other providers)
Test-NetConnection -ComputerName "YOUR_SERVER" -Port 1433

# Test proxy endpoint
Invoke-WebRequest -Uri "http://localhost:8020/services/Exact.Entity.REST.EG/Account" `
    -UseDefaultCredentials -Method Head

# Check listening ports
Get-NetTCPConnection -State Listen |
    Where-Object { $_.LocalPort -in @(80, 443, 8080) }
```

```bash [Bash]
# Test connectivity to SQL Server (adjust host/port for other providers)
nc -zv YOUR_SERVER 1433

# Test proxy endpoint
curl -I http://localhost:8020/services/Exact.Entity.REST.EG/Account

# Check listening ports
ss -tlnp | grep -E ':(80|443|8080)\b'
```

:::

## Understanding error messages

### Error codes

| Status | Message | Cause | Fix |
|---|---|---|---|
| `400` | "Environment '{env}' is not allowed" | The environment specified in your URL path isn't configured as valid for this endpoint | Check the allowed environments list in your endpoint's `settings.json` file |
| `403` | "Access denied to endpoint" | Your token is valid but doesn't have permission to access this specific endpoint | Update the token's scopes in the Web UI under **Tokens** |
| `404` | "Endpoint '{name}' not found" | The gateway can't find a configuration file for the endpoint you're trying to access | Verify that the endpoint configuration file exists and is properly named |
| `429` | "Too many requests" | You've exceeded the rate limits set for your IP address or token | Wait for the rate limit window to reset, or increase the limits in configuration |
| `500` | "Database operation failed" | The gateway can't connect to or query the SQL Server database | Check your connection string and verify SQL Server is accessible |
| Blank | No content/blank page | Usually indicates TLS/SSL certificate issues | Bind a certificate in IIS, or check the TLS termination in front of the container |

### Recognizing log message patterns

```text
[INF] Rate limit enforced for {Identifier} - Someone hit the rate limits
[WRN] Tokens detected in the tokens directory. Relocate them to a secure location - Warning, take action
[ERR] Error processing endpoint {EndpointName} - Backend service issue
[DBG] SQL Query Request: {Url} - Database query being executed
```

## Emergency procedures

### Application not starting

When the gateway will not start, the cause is usually at the infrastructure level rather than in the application. Start by asking the host what it saw:

::: code-group

```bash [Docker]
# Container state and exit code
docker compose ps

# Startup output, including anything written before logging began
docker compose logs --tail=100 portway
```

```powershell [IIS]
# Critical startup errors from the Windows Event Viewer
Get-EventLog -LogName Application -Source "IIS*" -Newest 20

# Application pool state
Get-WebAppPoolState -Name "PortwayAppPool"
Restart-WebAppPool -Name "PortwayAppPool"
```

:::

If the host looks healthy but the application still won't start, check the application log for startup errors:

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

### Complete system reset (use with extreme caution)

::: danger Emergency Only
Only perform these steps when you've exhausted other options and after creating proper backups. This procedure will reset your gateway to a clean state, which may resolve persistent issues but will also clear all temporary data.
:::

Before doing anything drastic, create a complete backup of your critical configuration:

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

Once you have a backup, you can reset the application state:

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

After a reset, watch the application logs as it starts and test a few endpoints to confirm it came back cleanly.

## Keeping it healthy

- **Disk space.** Alert below 20% free and clear old logs on a schedule. Traffic logging generates substantial volume.
- **Health endpoints.** Automate checks against `/health` plus a few real endpoints. The [Telemetry](/guide/opentelemetry) guide covers feeding gateway metrics into an existing monitoring stack.
- **Backend connectivity.** Verify SQL and proxy targets after network changes or server maintenance.

## Related topics

- [Monitoring Guide](/guide/monitoring)
- [Security Guide](/guide/security)
- [Deployment Guide](/guide/deployment)
- [API Endpoints Guide](/guide/endpoints-sql)
