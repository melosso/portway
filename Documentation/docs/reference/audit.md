# Auditing

Request traffic logging captures per-request detail — timing, payloads, headers, and response data — and writes it to file or SQLite storage for later querying.

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
  "Path": "/api/500/Products",
  "QueryString": "?$top=10",
  "Environment": "prod",
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
| `Environment` | Target environment (e.g., "prod") |
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

Traffic logging adds I/O overhead. The queue-based design minimises impact on request latency — writes happen in background batches — but high-volume deployments should tune the settings below.

| Setting | Recommendation |
|---------|---------------|
| `IncludeRequestBodies` / `IncludeResponseBodies` | Keep disabled unless actively debugging |
| `BatchSize` | Increase (e.g. 500) on high-traffic APIs to reduce write frequency |
| `FlushIntervalMs` | Increase (e.g. 5000) if I/O is a bottleneck |
| `QueueCapacity` | Increase if log entries are being dropped (watch for queue-full warnings in application logs) |
| `StorageType` | Prefer `file` over `sqlite` for raw throughput |

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

## Troubleshooting

| Symptom | Check |
|---------|-------|
| Logs not written | `Enabled: true` in config; write permissions on `LogDirectory` / `SqlitePath`; check application logs |
| Missing entries | Queue may be full — increase `QueueCapacity`; verify `FlushIntervalMs` is not too high |
| High performance impact | Disable body capture; increase `BatchSize` and `FlushIntervalMs`; switch to file storage |
| Disk filling up | Reduce `MaxFileCount`; reduce `MaxBodyCaptureSizeBytes`; disable body capture |
| SQLite errors | Check file permissions; ensure path directory exists; validate with `sqlite3 log/traffic_logs.db .tables` |

### Diagnostic Commands

```powershell
# Check if logging is enabled
Get-Content "appsettings.json" | ConvertFrom-Json | Select-Object -ExpandProperty RequestTrafficLogging

# Monitor log directory size
Get-ChildItem "log/traffic" -Recurse | Measure-Object -Property Length -Sum

# View recent traffic logs
Get-Content "log/traffic/proxy_traffic_$(Get-Date -Format 'yyyyMMdd')*.json" | Select-Object -Last 10 | ConvertFrom-Json
```
