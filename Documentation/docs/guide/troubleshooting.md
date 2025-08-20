# Troubleshooting

A comprehensive guide to diagnosing and resolving common issues with your Portway API gateway.

**Quick actions:**

[[toc]]

## Common Issues

### Authentication Failures

#### Missing or Invalid Token

**Symptoms:**
- 401 Unauthorized responses
- "Authentication required" error messages
- "Invalid or expired token" errors

**Solutions:**

1. **Verify token is being sent:**
   ```http
   Authorization: Bearer YOUR_TOKEN
   ```

2. **Check user associated to the token exists:**

    Check if the token has the necessary permissions.
   
   ```powershell
   # Run TokenGenerator
   cd tools\TokenGenerator
   .\TokenGenerator.exe
   ```

4. **Generate new token if needed:**

    Make sure to revoke the old user/token afterwards:

   ```powershell
   # Run TokenGenerator
   cd tools\TokenGenerator
   .\TokenGenerator.exe
   ```

::: tip
Always store tokens securely and never commit them to version control. Use environment variables or secure vaults for production deployments.
:::

#### Scope Restrictions

**Symptoms:**
- 403 Forbidden responses
- "Access denied to endpoint" errors
- "Access denied to environment" errors

**Solutions:**

1. **Check token scopes:**
   ```powershell
   # View token file content
   Get-Content ".\tokens\username.txt" | ConvertFrom-Json | Format-List
   ```

2. **Update token scopes:**
   ```powershell
   # Using TokenGenerator
   .\TokenGenerator.exe
   # Select option 4: Update token scopes
   ```

3. **Verify endpoint configuration:**
   ```json
   {
     "AllowedEnvironments": ["prod", "dev"],
     "AllowedScopes": "Products,Orders"
   }
   ```

### Rate Limiting Issues

#### IP Blocking

**Symptoms:**
- 429 Too Many Requests responses
- "Rate limit exceeded" errors
- "IP blocked" messages in logs

**Solutions:**

1. **Check rate limit configuration:**
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

2. **Monitor rate limit logs:**
   ```powershell
   # Search for rate limit events
   Select-String -Path ".\log\*.log" -Pattern "Rate limit" | 
       Sort-Object -Property LastWriteTime -Descending | 
       Select-Object -First 20
   ```

3. **Clear blocked IPs (restart required):**
   ```powershell
   # Restart IIS Application Pool
   Restart-WebAppPool -Name "PortwayAppPool"
   ```

::: warning
Rate limiting operates using in-memory token buckets. Restarting the application will reset all rate limit counters.
:::

### Connection Issues

#### Database Connection Failures

**Symptoms:**
- 500 Internal Server Error on SQL endpoints
- "Database operation failed" errors
- Connection timeout messages

**Solutions:**

1. **Verify connection string:**
   ```json
   {
     "ConnectionString": "Server=YOUR_SERVER;Database=600;Trusted_Connection=True;Connection Timeout=15;TrustServerCertificate=true;"
   }
   ```

2. **Test SQL connectivity:**
   ```powershell
   # Test SQL connection
   $conn = New-Object System.Data.SqlClient.SqlConnection
   $conn.ConnectionString = "Server=YOUR_SERVER;Database=600;Trusted_Connection=True;"
   try {
       $conn.Open()
       Write-Host "Connection successful"
   } catch {
       Write-Host "Connection failed: $_"
   } finally {
       $conn.Close()
   }
   ```

3. **Check connection pool settings:**
   ```json
   {
     "SqlConnectionPooling": {
       "MinPoolSize": 5,
       "MaxPoolSize": 100,
       "ConnectionTimeout": 15,
       "CommandTimeout": 30,
       "Enabled": true
     }
   }
   ```

#### Proxy Endpoint Failures

**Symptoms:**
- Timeout errors on proxy endpoints
- "Error processing endpoint" messages
- 503 Service Unavailable responses

**Solutions:**

1. **Verify target service availability:**
   ```powershell
   # Test endpoint connectivity
   Invoke-WebRequest -Uri "http://localhost:8020/services/Exact.Entity.REST.EG/Account" -UseDefaultCredentials
   ```

2. **Check proxy configuration:**
   ```json
   {
     "Url": "http://localhost:8020/services/Exact.Entity.REST.EG/Account",
     "Methods": ["GET", "POST"],
     "AllowedEnvironments": ["prod", "dev"]
   }
   ```

3. **Review proxy headers:**
   ```powershell
   # Check current environment settings
   Get-Content ".\environments\600\settings.json" | ConvertFrom-Json
   ```

### Health Check Failures

#### Disk Space Issues

**Symptoms:**
- Health check shows "Unhealthy" status
- "Critical: Only X% disk space remaining" warnings
- Log write failures

**Solutions:**

1. **Check disk space:**
   ```powershell
   # Check available disk space
   Get-PSDrive -PSProvider FileSystem | 
       Select-Object Name, @{Name="FreeGB";Expression={[math]::Round($_.Free/1GB,2)}}, 
                     @{Name="UsedGB";Expression={[math]::Round($_.Used/1GB,2)}}, 
                     @{Name="TotalGB";Expression={[math]::Round(($_.Free + $_.Used)/1GB,2)}}
   ```

2. **Clean up old logs:**
   ```powershell
   # Remove logs older than 30 days
   Get-ChildItem ".\log" -Recurse -File | 
       Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } | 
       Remove-Item -Force
   ```

3. **Configure log rotation:**
   ```json
   {
     "RequestTrafficLogging": {
       "MaxFileSizeMB": 50,
       "MaxFileCount": 5
     }
   }
   ```

#### Endpoint Health Issues

**Symptoms:**
- "One or more proxy services are not responding properly"
- Specific endpoints returning errors
- Intermittent connectivity problems

**Solutions:**

1. **Run detailed health check:**
   ```http
   GET /health/details
   Authorization: Bearer YOUR_TOKEN
   ```

2. **Test individual endpoints:**
   ```powershell
   # Test specific endpoint
   $headers = @{
       "Authorization" = "Bearer YOUR_TOKEN"
   }
   Invoke-RestMethod -Uri "https://your-gateway/api/600/Products" -Headers $headers
   ```

3. **Review endpoint logs:**
   ```powershell
   # Find endpoint-specific errors
   Select-String -Path ".\log\*.log" -Pattern "endpoint: Products" | 
       Where-Object { $_ -match "ERROR" }
   ```

### Performance Issues

#### Slow Response Times

**Symptoms:**
- High latency on API calls
- Timeout errors
- "DurationMs" values over 1000ms in logs

**Solutions:**

1. **Enable traffic logging:**
   ```json
   {
     "RequestTrafficLogging": {
       "Enabled": true,
       "EnableInfoLogging": true
     }
   }
   ```

2. **Analyze slow queries:**
   ```sql
   -- Find slow requests (using SQLite logging)
   SELECT Path, QueryString, DurationMs, StatusCode
   FROM TrafficLogs
   WHERE DurationMs > 1000
   ORDER BY DurationMs DESC
   LIMIT 20;
   ```

3. **Optimize connection pooling:**
   ```json
   {
     "SqlConnectionPooling": {
       "MinPoolSize": 10,
       "MaxPoolSize": 200,
       "ConnectionTimeout": 30
     }
   }
   ```

## Diagnostic Tools

### Log Analysis

#### Log File Locations

| Log Type | Default Location | Description |
|----------|-----------------|-------------|
| Application Logs | `./log/portwayapi-*.log` | General application events |
| Traffic Logs (File) | `./log/traffic/proxy_traffic_*.json` | Request/response details |
| Traffic Logs (SQLite) | `./log/traffic_logs.db` | Queryable traffic database |
| Auth Database | `./auth.db` | Token authentication data |

#### Useful PowerShell Commands

```powershell
# Find all errors in last hour
$oneHourAgo = (Get-Date).AddHours(-1)
Get-ChildItem ".\log\*.log" | 
    Where-Object { $_.LastWriteTime -gt $oneHourAgo } | 
    Select-String -Pattern "ERROR|EXCEPTION" | 
    Format-Table -AutoSize

# Count errors by type
Get-Content ".\log\portwayapi-$(Get-Date -Format 'yyyyMMdd').log" | 
    Select-String -Pattern "ERROR.*?:" | 
    Group-Object -Property Line | 
    Sort-Object Count -Descending | 
    Select-Object Count, Name -First 10

# Monitor log file in real-time
Get-Content ".\log\portwayapi-$(Get-Date -Format 'yyyyMMdd').log" -Wait -Tail 50
```

### Database Diagnostics

#### Check Token Status

```sql
-- Using SQLite browser or command line
SELECT Id, Username, CreatedAt, ExpiresAt, AllowedScopes, AllowedEnvironments
FROM Tokens
WHERE RevokedAt IS NULL
ORDER BY CreatedAt DESC;
```

#### Analyze Traffic Patterns

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

### Network Diagnostics

```powershell
# Test connectivity to SQL Server
Test-NetConnection -ComputerName "YOUR_SERVER" -Port 1433

# Test proxy endpoint
Invoke-WebRequest -Uri "http://localhost:8020/services/Exact.Entity.REST.EG/Account" `
    -UseDefaultCredentials -Method Head

# Check listening ports
Get-NetTCPConnection -State Listen | 
    Where-Object { $_.LocalPort -in @(80, 443, 8080) }
```

## Error Reference

### Common Error Codes

| Status Code | Error Message | Common Cause | Solution |
|------------|---------------|--------------|----------|
| 400 | "Environment '{env}' is not allowed" | Invalid environment in URL | Check allowed environments in `settings.json` |
| 401 | "Authentication required" | Missing Authorization header | Add Bearer token to request |
| 403 | "Access denied to endpoint" | Token lacks required scope | Update token scopes in TokenGenerator |
| 404 | "Endpoint '{name}' not found" | Endpoint not configured | Verify endpoint configuration file exists |
| 429 | "Too many requests" | Rate limit exceeded | Wait for retry period or increase limits |
| 500 | "Database operation failed" | SQL connection issue | Check connection string and SQL Server status |
| Blank | None/blank page | TLS/SSL | Bind a SSL-certificate to the website | 


### Log Message Patterns

```text
🚫 Rate limit enforced for {Identifier}
❌ Invalid token: {MaskedToken}
❌ Error processing endpoint {EndpointName}
📥 SQL Query Request: {Url}
✅ Successfully processed query for {Endpoint}
🔄 Converting OData to SQL for entity: {EntityName}
```

## Emergency Procedures

### Application Won't Start

1. **Check Event Viewer:**
   ```powershell
   Get-EventLog -LogName Application -Source "IIS*" -Newest 20
   ```

2. **Verify IIS configuration:**
   ```powershell
   # Check application pool status
   Get-WebAppPoolState -Name "PortwayAppPool"
   
   # Restart application pool
   Restart-WebAppPool -Name "PortwayAppPool"
   ```

3. **Review startup logs:**
   ```powershell
   Get-Content ".\log\portwayapi-$(Get-Date -Format 'yyyyMMdd').log" | 
       Select-String -Pattern "Application start|FATAL|ERROR" | 
       Select-Object -First 50
   ```

### Complete System Reset

::: danger
Only perform these steps after backing up your configuration and tokens!
:::

1. **Backup critical files:**
   ```powershell
   # Create backup directory
   $backupDir = ".\backup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
   New-Item -ItemType Directory -Path $backupDir
   
   # Copy important files
   Copy-Item ".\tokens\*" "$backupDir\tokens\" -Recurse
   Copy-Item ".\auth.db" "$backupDir\"
   Copy-Item ".\environments\*" "$backupDir\environments\" -Recurse
   Copy-Item ".\endpoints\*" "$backupDir\endpoints\" -Recurse
   ```

2. **Reset application state:**
   ```powershell
   # Stop IIS
   iisreset /stop
   
   # Clear logs
   Remove-Item ".\log\*" -Recurse -Force
   
   # Start IIS
   iisreset /start
   ```

## Best Practices

::: tip Preventive Maintenance
1. Regularly monitor disk space and clean old logs
2. Set up automated health checks with alerting
3. Keep backup copies of configuration files
4. Document all custom configurations
5. Test connectivity to backend services regularly
:::

::: warning Security Considerations
- Never expose detailed error messages to clients
- Rotate tokens periodically
- Monitor failed authentication attempts
- Keep audit logs of all configuration changes
:::

## Next Steps

- [Monitoring Guide](../monitoring)
- [Security Guide](../security)
- [Deployment Guide](../deployment)
- [API Endpoints Guide](../api-endpoints)