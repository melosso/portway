# Troubleshooting

This guide is designed for consultants, administrators, and technical support staff who need to diagnose and resolve issues with Portway gateway deployments. Rather than just listing fixes, this guide helps you understand the underlying causes of problems so you can more effectively support your users and prevent recurring issues.

**Quick actions:**

[[toc]]

## Common Issues

The following issues represent the most frequent problems encountered in production Portway deployments. Each includes guidance for both immediate resolution and long-term prevention.

### Authentication Failures

Authentication issues are the most common problems reported by API consumers. These occur when requests cannot be verified or when credentials lack proper permissions.

#### Missing or Invalid Tokens

**Symptoms:**
- Users receive `401 Unauthorized` responses
- "Authentication required" error messages
- "Invalid or expired token" errors in logs

**Common Causes:**
- Missing Authorization header in client requests
- Expired or revoked authentication tokens
- Incorrectly formatted Bearer token headers
- Token associated with deleted or disabled user accounts

**Diagnostic Steps:**

1. **Verify token format in user requests:**
   Ensure client applications include the proper header:
   ```http
   Authorization: Bearer YOUR_TOKEN
   ```

2. **Check token validity:**
   Use TokenGenerator to verify the token and associated user still exist:
   ```powershell
   # Run the Management Console
   .\PortwayMgt.exe
   ```

3. **Review authentication logs:**
   Look for patterns in failed authentication attempts to identify systemic issues.

**Solutions:**

- **Generate new token:** Create a fresh token for the user and ensure old tokens are properly revoked:
  ```powershell
  # Run the Management Console
   .\PortwayMgt.exe
  ```

- **User guidance:** Provide clear documentation on proper Authorization header format
- **Security review:** If multiple users are affected, verify that token storage and transmission are secure

::: tip Security Best Practice
Tokens are essentially API keys. Ensure users store them securely using environment variables or dedicated secret management systems. Never allow tokens to be committed to version control.
:::

#### Insufficient Token Permissions

**Symptoms:**
- Users receive `403 Forbidden` responses  
- "Access denied to endpoint" errors
- "Access denied to environment" errors in logs

**Common Causes:**
- Token lacks required scopes for the requested endpoint
- User attempting to access environments not permitted for their token
- Endpoint configuration restricts access more than token allows

**Diagnostic Steps:**

1. **Review token permissions:**
   ```powershell
   # View token file content
   Get-Content ".\tokens\username.txt" | ConvertFrom-Json | Format-List
   ```

2. **Verify endpoint configuration:**
   Check if the endpoint allows the user's environment and required scopes:
   ```json
   {
     "AllowedEnvironments": ["prod", "dev"],
     "AllowedScopes": "Products,Orders"
   }
   ```

3. **Cross-reference user needs:**
   Confirm what access the user legitimately requires for their integration.

**Solutions:**

- **Update token scopes:** Use Management Console to modify permissions:
  ```powershell
  .\PortwayMgt.exe # Select option 4: Update token scopes
  ```

- **Review endpoint access:** Ensure endpoint configuration matches business requirements
- **Document permissions:** Maintain clear documentation of what scopes are needed for different use cases

### Rate Limiting Issues

Rate limiting protects the gateway from being overwhelmed by too many requests. When users encounter these limits, it indicates either legitimate high usage or potentially problematic request patterns that need investigation.

#### IP or Token Rate Limiting

**Symptoms:**
- Users report `429 "Too Many Requests"` responses
- "Rate limit exceeded" errors in application logs
- "IP blocked" messages for specific addresses

**Common Causes:**
- Development teams making rapid test calls during integration
- Automated scripts or batch processes with aggressive request patterns
- Legitimate high-volume usage exceeding configured thresholds
- Potential abuse or misconfigured client applications

**Diagnostic Steps:**

1. **Check current rate limit configuration:**
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

2. **Review rate limit violations in logs:**
   ```powershell
   # Search for rate limit events
   Select-String -Path ".\log\*.log" -Pattern "Rate limit" | 
       Sort-Object -Property LastWriteTime -Descending | 
       Select-Object -First 20
   ```

3. **Identify affected users/IPs:**
   Look for patterns in the logs to determine if this is isolated to specific users or widespread.

**Solutions:**

- **Immediate relief:** Restart the application pool to reset all rate limit counters:
  ```powershell
  # Restart IIS Application Pool
  Restart-WebAppPool -Name "PortwayAppPool"
  ```

- **Long-term fix:** Adjust rate limits in configuration if legitimate usage patterns require higher thresholds
- **User guidance:** Advise development teams to implement proper retry logic with exponential backoff

::: warning Important Note
Rate limiting uses in-memory token buckets. Restarting the application resets all counters to zero. This provides immediate relief but should be followed by addressing the root cause.
:::

::: warning Important Note
The rate limiting system uses in-memory token buckets to track usage. This means when you restart the application, all rate limit counters are reset to zero. While this can help in emergency situations, it's not a long-term solution if you're consistently hitting limits.
:::

### Connection Issues

Connection problems can be frustrating because they often indicate issues with underlying services that your gateway depends on. Let's walk through the two main types you'll encounter.

#### When Your Database Won't Connect

Database connection failures will typically show up as `500 Internal Server Error` responses when you try to access SQL-based endpoints. These errors happen when the gateway can't reach your SQL Server or when the connection is dropped unexpectedly.

Your first step should be verifying that your connection string is correct and complete:

```json
{
  "ConnectionString": "Server=YOUR_SERVER;Database=600;Trusted_Connection=True;Connection Timeout=15;TrustServerCertificate=true;"
}
```

Before diving into complex troubleshooting, test whether you can connect to SQL Server at all from your gateway server:

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

If basic connectivity works but you're still having issues, the problem might be with connection pooling settings. The gateway manages a pool of database connections to improve performance, but if these settings are misconfigured, you might see intermittent failures:

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

#### When Proxy Endpoints Stop Responding

Proxy endpoints act as intermediaries between your API consumers and your backend services. When these fail, you'll typically see timeout errors, "Error processing endpoint" messages, or `503 Service Unavailable` responses. This pretty common in legacy applications, where high availability of an API isn't guaranteed.

Start by testing whether the target service is actually available. Try accessing it directly:

```powershell
# Test endpoint connectivity
Invoke-WebRequest -Uri "http://localhost:8020/services/Exact.Entity.REST.EG/Account" -UseDefaultCredentials
```

If the direct connection works, check your proxy configuration to ensure the URL and settings are correct:

```json
{
  "Url": "http://localhost:8020/services/Exact.Entity.REST.EG/Account",
  "Methods": ["GET", "POST"],
  "AllowedEnvironments": ["prod", "dev"]
}
```

Sometimes proxy issues are related to environment-specific configurations. Review your environment settings to make sure they match what the backend service expects:

```powershell
# Check current environment settings
Get-Content ".\environments\600\settings.json" | ConvertFrom-Json
```

### Health Check Failures

The gateway includes built-in health monitoring to help you identify problems before they impact your users. When health checks fail, it's usually indicating a resource constraint or connectivity issue that needs immediate attention.

#### When You're Running Out of Disk Space

One of the most critical health issues you can encounter is low disk space. When the system detects critically low storage, health checks will show `"Unhealthy"` status with warnings about remaining disk space. This can lead to log write failures and eventually cause the entire application to stop functioning.

Start by checking exactly how much space you have available:

```powershell
# Check available disk space
Get-PSDrive -PSProvider FileSystem | 
    Select-Object Name, @{Name="FreeGB";Expression={[math]::Round($_.Free/1GB,2)}}, 
                  @{Name="UsedGB";Expression={[math]::Round($_.Used/1GB,2)}}, 
                  @{Name="TotalGB";Expression={[math]::Round(($_.Free + $_.Used)/1GB,2)}}
```

If you're running low on space, the quickest relief usually comes from cleaning up old log files. The gateway can generate substantial logs over time, especially with traffic logging enabled:

```powershell
# Remove logs older than 30 days
Get-ChildItem ".\log" -Recurse -File | 
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } | 
    Remove-Item -Force
```

For ongoing space management, configure automatic log rotation to prevent this problem from recurring:

```json
{
  "RequestTrafficLogging": {
    "MaxFileSizeMB": 50,
    "MaxFileCount": 5
  }
}
```

#### When Your Backend Services Aren't Responding

Sometimes health checks will report that "one or more proxy services are not responding properly." This indicates that while your gateway is running fine, some of the backend services it depends on are having problems.

To get detailed information about which specific services are failing, request a detailed health report:

```http
GET /health/details
Authorization: Bearer YOUR_TOKEN
```

Once you know which endpoints are problematic, test them individually to isolate the issue:

```powershell
# Test specific endpoint
$headers = @{
    "Authorization" = "Bearer YOUR_TOKEN"
}
Invoke-RestMethod -Uri "https://your-gateway/api/600/Products" -Headers $headers
```

If specific endpoints are consistently failing, review their error logs to understand what's happening:

```powershell
# Find endpoint-specific errors
Select-String -Path ".\log\*.log" -Pattern "endpoint: Products" | 
    Where-Object { $_ -match "ERROR" }
```

### Performance Issues

Performance problems can be subtle at first but significantly impact user experience as they worsen. The gateway includes monitoring capabilities to help you identify and resolve these issues before they become critical.

#### When Everything Feels Slow

If you're experiencing high latency on API calls, timeout errors, or seeing duration measurements over `1000ms` in your logs, you're dealing with performance degradation. This can stem from various causes, including database bottlenecks, network issues, or resource constraints.

First, enable detailed traffic logging to get visibility into exactly where time is being spent:

```json
{
  "RequestTrafficLogging": {
    "Enabled": true,
    "EnableInfoLogging": true
  }
}
```

With logging enabled, you can analyze which requests are taking the longest to complete. If you're using SQLite for traffic logging, you can query this data directly:

```sql
-- Find slow requests (using SQLite logging)
SELECT Path, QueryString, DurationMs, StatusCode
FROM TrafficLogs
WHERE DurationMs > 1000
ORDER BY DurationMs DESC
LIMIT 20;
```

Often, performance issues are related to database connection management. If your connection pool is too small or configured incorrectly, requests may wait for available connections. Try optimizing these settings:

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

Effective troubleshooting requires the right tools and knowledge of where to look for information. The Portway gateway generates extensive diagnostic data, but knowing how to access and interpret this information is crucial for quick problem resolution.

### Understanding Your Log Files

The gateway creates several different types of logs, each serving a specific purpose in helping you understand what's happening in your system. Knowing which log to check for which type of problem will save you significant time during troubleshooting.

#### Where to Find Your Logs

Your logs are organized in a logical structure, with different types of information stored in different locations:

| Log Type | Default Location | What You'll Find Here |
|----------|-----------------|-------------|
| Application Logs | `./log/portwayapi-*.log` | General application events, errors, and startup information |
| Traffic Logs (File) | `./log/traffic/proxy_traffic_*.json` | Detailed request/response information in JSON format |
| Traffic Logs (SQLite) | `./log/traffic_logs.db` | Queryable database of all traffic for analysis |
| Auth Database | `./auth.db` | Token authentication data and user information |

#### Essential PowerShell Commands for Log Analysis

When you're troubleshooting an active issue, these PowerShell commands will help you quickly find relevant information:

To find recent errors across all log files:
```powershell
# Find all errors in last hour
$oneHourAgo = (Get-Date).AddHours(-1)
Get-ChildItem ".\log\*.log" | 
    Where-Object { $_.LastWriteTime -gt $oneHourAgo } | 
    Select-String -Pattern "ERROR|EXCEPTION" | 
    Format-Table -AutoSize
```

To understand what types of errors are most common:
```powershell
# Count errors by type
Get-Content ".\log\portwayapi-$(Get-Date -Format 'yyyyMMdd').log" | 
    Select-String -Pattern "ERROR.*?:" | 
    Group-Object -Property Line | 
    Sort-Object Count -Descending | 
    Select-Object Count, Name -First 10
```

For real-time monitoring during active troubleshooting:
```powershell
# Monitor log file in real-time
Get-Content ".\log\portwayapi-$(Get-Date -Format 'yyyyMMdd').log" -Wait -Tail 50
```

### Database Diagnostics

The gateway uses SQLite databases to store authentication and traffic data. These databases contain valuable information for troubleshooting authentication issues and analyzing usage patterns.

#### Checking Authentication Status

When users report authentication problems, start by verifying their token status in the database:

```sql
-- Using SQLite browser or command line
SELECT Id, Username, CreatedAt, ExpiresAt, AllowedScopes, AllowedEnvironments
FROM Tokens
WHERE RevokedAt IS NULL
ORDER BY CreatedAt DESC;
```

This query shows you all active tokens, when they were created, when they expire, and what permissions they have.

#### Understanding Traffic Patterns and Errors

The traffic logs database is particularly useful for identifying patterns in errors or performance issues:

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

This query helps you identify which endpoints are experiencing the highest error rates, giving you a clear starting point for investigation.

### Network and Connectivity Diagnostics

Sometimes the issue isn't with the gateway itself, but with the network connections it depends on. These PowerShell commands help you verify connectivity to essential services:

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

These tests will quickly tell you if the problem is a basic connectivity issue versus something more complex within the application itself.

## Understanding Error Messages

When troubleshooting issues, the specific error codes and messages you encounter provide valuable clues about what's going wrong. Rather than just memorizing these codes, understanding what they actually mean will help you diagnose problems more effectively.

### Common Error Codes and What They Really Mean

| Status Code | Error Message | What's Actually Happening | How to Fix It |
|------------|---------------|---------------------------|---------------|
| `400` | "Environment '{env}' is not allowed" | The environment specified in your URL path isn't configured as valid for this endpoint | Check the allowed environments list in your endpoint's `settings.json` file |
| `401` | "Authentication required" | Your request doesn't include a valid Authorization header with a Bearer token | Add the proper Authorization header to your request |
| `403` | "Access denied to endpoint" | Your token is valid but doesn't have permission to access this specific endpoint | Update your token's scopes using the TokenGenerator tool |
| `404` | "Endpoint '{name}' not found" | The gateway can't find a configuration file for the endpoint you're trying to access | Verify that the endpoint configuration file exists and is properly named |
| `429` | "Too many requests" | You've exceeded the rate limits set for your IP address or token | Wait for the rate limit window to reset, or increase the limits in configuration |
| `500` | "Database operation failed" | The gateway can't connect to or query the SQL Server database | Check your connection string and verify SQL Server is accessible |
| Blank | No content/blank page | Usually indicates TLS/SSL certificate issues | Bind a proper SSL certificate to your website in IIS |

### Recognizing Log Message Patterns

The gateway uses a familiar logging pattern to help you quickly identify different types of events:

```text
[INF] Rate limit enforced for {Identifier} - Someone hit the rate limits 
[WRN] okens detected in the tokens directory. Relocate them to a secure location - Warning, take action
[ERR] Error processing endpoint {EndpointName} - Backend service issue
[DBG] SQL Query Request: {Url} - Database query being executed
```

These patterns help you quickly scan logs and identify the types of issues you're dealing with.

## Emergency Procedures

Sometimes things go seriously wrong and you need to get the system back online quickly. These procedures are for emergency situations when normal troubleshooting isn't sufficient.

### When the Application Won't Start at All

If your gateway won't start, the problem is usually at the infrastructure level rather than within the application code itself. Start with the most basic diagnostics:

First, check the Windows Event Viewer for any critical startup errors:
```powershell
Get-EventLog -LogName Application -Source "IIS*" -Newest 20
```

Next, verify that IIS and your application pool are in the correct state:
```powershell
# Check application pool status
Get-WebAppPoolState -Name "PortwayAppPool"

# Restart application pool
Restart-WebAppPool -Name "PortwayAppPool"
```

If IIS appears to be working but the application still won't start, check the application logs for startup errors:
```powershell
Get-Content ".\log\portwayapi-$(Get-Date -Format 'yyyyMMdd').log" | 
    Select-String -Pattern "Application start|FATAL|ERROR" | 
    Select-Object -First 50
```

### Complete System Reset (Use with Extreme Caution)

::: danger Emergency Only
Only perform these steps when you've exhausted other options and after creating proper backups. This procedure will reset your gateway to a clean state, which may resolve persistent issues but will also clear all temporary data.
:::

Before doing anything drastic, create a complete backup of your critical configuration:

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

Once you have a backup, you can reset the application state:

```powershell
# Stop IIS
iisreset /stop

# Clear logs (this removes diagnostic history)
Remove-Item ".\log\*" -Recurse -Force

# Start IIS
iisreset /start
```

After performing a reset, monitor the application logs carefully to ensure it starts up properly and test a few basic endpoints to verify functionality.

## Keeping it Healthy

Prevention is always better than cure when it comes to gateway operations. By following these practices, you can avoid many of the common issues described in this guide and catch problems before they impact your users.

### Proactive Monitoring and Maintenance

Regular maintenance doesn't have to be complicated, but it does need to be consistent. Here are the key activities that will keep your gateway running smoothly:

- **Keep an eye on your storage space.** Disk space issues are one of the most common causes of gateway failures, but they're also completely preventable. Set up monitoring to alert you when disk space drops below 20%, and establish a routine for cleaning up old log files. The gateway can generate substantial logs, especially with detailed traffic logging enabled.

- **Monitor your health endpoints regularly.** Don't wait for users to report problems; set up automated health checks that call your `/health` endpoint and alert you to issues. Consider setting up simple monitoring scripts that test both the basic health endpoint and a few key API endpoints to ensure end-to-end functionality.

- **Test connectivity to your backend services.** The gateway is only as reliable as the services it connects to. Regularly verify that your SQL Server connections are working and that proxy endpoints can reach their target services. This is especially important after any network changes or server maintenance.

- **Keep audit logs of configuration changes.** When you modify endpoint configurations, token scopes, or other settings, document what you changed and why. This information becomes invaluable when troubleshooting issues that appear after configuration updates.

- **Rotate your tokens periodically.** Authentication tokens should be treated like passwords, change them regularly and immediately revoke any tokens that are no longer needed. This reduces your security exposure and ensures that only current, authorized integrations have access to your gateway.

### Security/Considerations

Security isn't just about preventing attacks - it's also about maintaining clean diagnostic information and ensuring you can trust your troubleshooting data.

- **Never expose detailed error messages to external clients.** While detailed error information is crucial for troubleshooting, it can also reveal sensitive information about your internal systems to potential attackers. Configure your gateway to return generic error messages to clients while logging detailed information internally.

- **Monitor failed authentication attempts.** Keep track of repeated authentication failures, especially from the same IP addresses. This can indicate either misconfigured integrations that need attention or potential security threats that need investigation.

- **Secure your diagnostic tools.** The same database queries and log analysis tools that help you troubleshoot can also reveal sensitive information. Ensure that access to logs, databases, and diagnostic endpoints is properly restricted to authorized personnel only.

## Where to Go From Here

This troubleshooting guide covers the most common issues you'll encounter, but every environment is unique. As you become more familiar with your specific gateway configuration and usage patterns, you'll develop intuition about where to look first when problems arise.

For deeper information about specific aspects of gateway operation and configuration, these additional guides will provide more detailed guidance:

- **[Monitoring Guide](../monitoring)** - Set up comprehensive monitoring and alerting for your gateway
- **[Security Guide](../security)** - Implement robust security practices and threat monitoring  
- **[Deployment Guide](../deployment)** - Best practices for deploying and configuring your gateway
- **[API Endpoints Guide](../api-endpoints)** - Detailed information about configuring and managing your API endpoints

Remember that troubleshooting is a skill that improves with practice. The more familiar you become with your gateway's normal operation patterns, the more quickly you'll be able to identify and resolve issues when they occur.