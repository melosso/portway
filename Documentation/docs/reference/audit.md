# Auditing

Portway provides detailed auditing. Apart from logging, there is an extensive request traffic logging framework. This provides capabilities to track and analyze all API requests, including timing, payloads, and response data. 

::: tip
This feature is useful for auditing, but can also help with debugging, and performance monitoring.
:::

## Overview

Request Traffic Logging captures comprehensive details about every API request that passes through the system. The feature can be configured to store logs in either file-based storage or SQLite database, with options to control what data is captured and how long it's retained.

## Configuration

### Basic Settings

```json
{
  "RequestTrafficLogging": {
    "Enabled": false,
    "QueueCapacity": 10000,
    "StorageType": "file",
    "SqlitePath": "log/traffic_logs.db",
    "LogDirectory": "log/traffic",
    "MaxFileSizeMB": 50,
    "MaxFileCount": 5,
    "FilePrefix": "proxy_traffic_",
    "BatchSize": 100,
    "FlushIntervalMs": 1000,
    "IncludeRequestBodies": false,
    "IncludeResponseBodies": false,
    "MaxBodyCaptureSizeBytes": 4096,
    "CaptureHeaders": true,
    "EnableInfoLogging": true
  }
}
```

### Configuration Options

| Setting | Description | Default |
|---------|-------------|---------|
| `Enabled` | Enables traffic logging | `false` |
| `QueueCapacity` | Max queued log entries | `10000` |
| `StorageType` | Storage type: "file" or "sqlite" | `"file"` |
| `SqlitePath` | Path to SQLite database | `"log/traffic_logs.db"` |
| `LogDirectory` | Directory for log files | `"log/traffic"` |
| `MaxFileSizeMB` | Max size per log file | `50` |
| `MaxFileCount` | Number of files to retain | `5` |
| `FilePrefix` | Prefix for log filenames | `"proxy_traffic_"` |
| `BatchSize` | Entries per batch write | `100` |
| `FlushIntervalMs` | Write interval in ms | `1000` |
| `IncludeRequestBodies` | Capture request bodies | `false` |
| `IncludeResponseBodies` | Capture response bodies | `false` |
| `MaxBodyCaptureSizeBytes` | Max body size to capture | `4096` |
| `CaptureHeaders` | Capture request headers | `true` |
| `EnableInfoLogging` | Log at INFO level | `true` |

## Storage Types

### File Storage

When `StorageType` is set to `"file"`, logs are stored as JSON files with automatic rotation:

```
log/traffic/
├── proxy_traffic_20240120_103015.json
├── proxy_traffic_20240120_083045.json
└── proxy_traffic_20240119_154530.json
```

**File Format:**
- Each line contains a JSON object representing one request
- Files are rotated based on size (`MaxFileSizeMB`)
- Old files are deleted when count exceeds `MaxFileCount`
- Filenames include timestamp for easy identification

### SQLite Storage

When `StorageType` is set to `"sqlite"`, logs are stored in a SQLite database:

**Database Schema:**
```sql
CREATE TABLE TrafficLogs (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Timestamp TEXT NOT NULL,
    Method TEXT NOT NULL,
    Path TEXT NOT NULL,
    QueryString TEXT,
    Environment TEXT,
    EndpointName TEXT,
    TargetUrl TEXT,
    StatusCode INTEGER,
    RequestSize INTEGER,
    ResponseSize INTEGER,
    DurationMs INTEGER,
    Username TEXT,
    ClientIp TEXT,
    TraceId TEXT NOT NULL,
    RequestHeaders TEXT,
    RequestBody TEXT,
    ResponseBody TEXT
);

CREATE INDEX idx_timestamp ON TrafficLogs (Timestamp);
```

## Log Entry Format

Each traffic log entry contains:

```json
{
  "Id": 12345,
  "Timestamp": "2024-01-20T10:30:15Z",
  "Method": "GET",
  "Path": "/api/600/Products",
  "QueryString": "?$top=10",
  "Environment": "600",
  "EndpointName": "Products",
  "TargetUrl": "http://localhost:8020/services/Exact.Entity.REST.EG/Product",
  "StatusCode": 200,
  "RequestSize": 0,
  "ResponseSize": 2048,
  "DurationMs": 45,
  "Username": "api-user",
  "ClientIp": "192.168.1.100",
  "TraceId": "a1b2c3d4",
  "RequestHeaders": {
    "Accept": "application/json",
    "Authorization": "[REDACTED]",
    "User-Agent": "MyApp/1.0"
  },
  "RequestBody": null,
  "ResponseBody": null
}
```

## Field Descriptions

| Field | Description |
|-------|-------------|
| `Id` | Unique identifier (SQLite only) |
| `Timestamp` | UTC timestamp of request |
| `Method` | HTTP method (GET, POST, etc.) |
| `Path` | Request path |
| `QueryString` | Query parameters |
| `Environment` | Target environment (e.g., "600") |
| `EndpointName` | Name of the endpoint |
| `TargetUrl` | Proxied URL (for proxy requests) |
| `StatusCode` | HTTP response status |
| `RequestSize` | Size of request body in bytes |
| `ResponseSize` | Size of response body in bytes |
| `DurationMs` | Request duration in milliseconds |
| `Username` | Authenticated user |
| `ClientIp` | Client IP address |
| `TraceId` | Unique request identifier |
| `RequestHeaders` | Request headers (sensitive values redacted) |
| `RequestBody` | Request body (if enabled) |
| `ResponseBody` | Response body (if enabled) |

## Security Features

### Header Sanitization

Sensitive headers are automatically redacted:
- Authorization
- Cookie
- X-API-Key
- API-Key
- Password
- X-Auth-Token
- Token
- Secret
- Credential
- Access-Token
- X-Access-Token

### Body Capture Controls

Request and response bodies are:
- Disabled by default
- Limited by `MaxBodyCaptureSizeBytes`
- Truncated with "..." suffix if exceeding limit
- Only captured for JSON/XML content types

### Access Control

- Log files/database should be protected from web access
- Consider using separate storage with restricted permissions
- Implement log rotation to manage sensitive data retention

## Performance Considerations

Using request traffic logging **will come with a performance penalty**. This framework is built with minimal impact on request processing in mind, but you should be aware of the trade-offs.

To optimize performance if the impact is too significant:

#### Queue Management
- Adjust the `QueueCapacity` setting in `appsettings.json` based on your traffic volume
- The system automatically drops oldest entries when the queue is full (preventing memory exhaustion)
- A background service processes entries in batches, reducing I/O overhead

#### Batch Processing
- Increase `BatchSize` to reduce write frequency (default: 100)
- Extend `FlushIntervalMs` to accumulate more entries before writing (default: 1000ms)
- Balance between data freshness and I/O efficiency

#### Resource Optimization
- Disable body capture (`IncludeRequestBodies` and `IncludeResponseBodies`) to reduce memory usage
- Limit `MaxBodyCaptureSizeBytes` to capture only essential data
- Consider file storage over SQLite for high-volume scenarios
- Use shorter retention periods (`MaxFileCount`) to manage disk space

###s# Performance Tuning Tips
1. Start with minimal logging (headers only, no bodies)
2. Monitor queue saturation and adjust capacity
3. Increase batch size for high-traffic APIs
4. Consider sampling strategies for extremely high-volume endpoints
5. Use dedicated storage drives for log files

## Querying Traffic Logs

### File Storage Queries

Using PowerShell:
```powershell
# Find slow requests
Get-Content "log/traffic/proxy_traffic_*.json" | 
    ConvertFrom-Json | 
    Where-Object { $_.DurationMs -gt 1000 } |
    Select-Object Timestamp, Method, Path, DurationMs

# Count requests by endpoint
Get-Content "log/traffic/proxy_traffic_*.json" | 
    ConvertFrom-Json | 
    Group-Object EndpointName | 
    Select-Object Count, Name | 
    Sort-Object Count -Descending

# Find failed requests
Get-Content "log/traffic/proxy_traffic_*.json" | 
    ConvertFrom-Json | 
    Where-Object { $_.StatusCode -ge 400 } |
    Select-Object Timestamp, Path, StatusCode
```

### SQLite Queries

```sql
-- Top 10 slowest requests
SELECT 
    Timestamp,
    Method,
    Path,
    DurationMs,
    StatusCode
FROM TrafficLogs
ORDER BY DurationMs DESC
LIMIT 10;

-- Request count by endpoint
SELECT 
    EndpointName,
    COUNT(*) as RequestCount,
    AVG(DurationMs) as AvgDuration,
    MAX(DurationMs) as MaxDuration
FROM TrafficLogs
GROUP BY EndpointName
ORDER BY RequestCount DESC;

-- Error rate by hour
SELECT 
    strftime('%Y-%m-%d %H:00', Timestamp) as Hour,
    COUNT(*) as TotalRequests,
    SUM(CASE WHEN StatusCode >= 400 THEN 1 ELSE 0 END) as Errors,
    ROUND(CAST(SUM(CASE WHEN StatusCode >= 400 THEN 1 ELSE 0 END) AS FLOAT) / COUNT(*) * 100, 2) as ErrorRate
FROM TrafficLogs
GROUP BY Hour
ORDER BY Hour DESC;

-- User activity summary
SELECT 
    Username,
    COUNT(*) as RequestCount,
    COUNT(DISTINCT EndpointName) as UniqueEndpoints,
    AVG(DurationMs) as AvgDuration
FROM TrafficLogs
WHERE Username IS NOT NULL
GROUP BY Username
ORDER BY RequestCount DESC;
```

## Use Cases

There may be various scenarios where this proves valuable. Some common use cases include:

### 1. Performance Monitoring
- Identify slow requests
- Track response times by endpoint
- Monitor request patterns
- Detect performance degradation

### 2. Security Auditing
- Track user access patterns
- Identify suspicious activity
- Monitor failed authentication attempts
- Audit data access

### 3. Debugging
- Trace request flow
- Examine request/response payloads
- Correlate errors with requests
- Reproduce issues

### 4. Usage Analytics
- Measure endpoint popularity
- Track user behavior
- Identify usage patterns
- Plan capacity

## Best Practices

### 1. Storage Selection
- Use file storage for simplicity
- Choose SQLite for complex queries
- Consider external databases for scale

### 2. Data Retention
- Set appropriate `MaxFileCount`
- Implement regular cleanup
- Balance retention vs. storage costs

### 3. Performance Tuning
- Adjust `BatchSize` for throughput
- Configure `FlushIntervalMs` for latency
- Monitor queue capacity usage

### 4. Security Configuration
- Disable body capture in production
- Protect log storage location
- Implement access controls
- Regular audit of log access

## Troubleshooting

### Common Issues

1. **Logs Not Being Written**
   - Verify `Enabled` is set to `true`
   - Check write permissions
   - Ensure storage path exists
   - Review application logs for errors

2. **Missing Data**
   - Check queue capacity limits
   - Verify flush intervals
   - Review body capture settings
   - Confirm header capture is enabled

3. **Performance Impact**
   - Reduce batch size
   - Increase flush interval
   - Disable body capture
   - Consider sampling high-traffic endpoints

4. **Storage Issues**
   - Monitor disk space
   - Review rotation settings
   - Check file permissions
   - Validate SQLite connection

### Diagnostic Commands

```powershell
# Check if logging is enabled
Get-Content "appsettings.json" | ConvertFrom-Json | Select-Object -ExpandProperty RequestTrafficLogging

# Monitor log directory size
Get-ChildItem "log/traffic" -Recurse | Measure-Object -Property Length -Sum

# View recent traffic logs
Get-Content "log/traffic/proxy_traffic_$(Get-Date -Format 'yyyyMMdd')*.json" | Select-Object -Last 10 | ConvertFrom-Json
```
