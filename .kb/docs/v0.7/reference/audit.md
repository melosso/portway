---
title: Auditing
description: "Request traffic logging to file or SQLite: configuration, storage, entry format, redaction and queries"
---

# Auditing

Request traffic logging records per-request metadata (timing, status, user, client address, headers and optionally bodies) to file or SQLite storage.

## Configuration

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

## Storage types

### File storage

With `StorageType: "file"`, entries are written to rotating JSON files:

```
log/traffic/
├── proxy_traffic_20240120_103015.json
├── proxy_traffic_20240120_083045.json
└── proxy_traffic_20240119_154530.json
```

- One JSON object per line
- Rotation at `MaxFileSizeMB`
- The oldest files beyond `MaxFileCount` are deleted
- File names contain the creation time

### SQLite storage

With `StorageType: "sqlite"`, entries are written to this table:
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

## Log entry format

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

`Id` is present in SQLite storage only. `TargetUrl` is set for proxy requests. Body fields are `null` unless body capture is enabled.

## Security features

### Header sanitization

Redacted header values: `Authorization`, `Cookie`, `X-API-Key`, `API-Key`, `Password`, `X-Auth-Token`, `Token`, `Secret`, `Credential`, `Access-Token`, `X-Access-Token`, and every header and query parameter name declared in an environment's `Authentication.Methods`.

### Body capture controls

Body capture is disabled by default, limited to `MaxBodyCaptureSizeBytes` (longer bodies end with `...`) and applies to JSON and XML content only.

### Storage access

Store traffic logs outside web-served directories, with file permissions limited to the Portway process.

## Performance considerations

Entries are queued and written in background batches. Tuning for high volume:

| Setting | Recommendation |
|---------|---------------|
| `IncludeRequestBodies` / `IncludeResponseBodies` | Disabled outside debugging |
| `BatchSize` | Higher (e.g. 500) for fewer writes |
| `FlushIntervalMs` | Higher (e.g. 5000) when I/O is the bottleneck |
| `QueueCapacity` | Higher when the application log reports a full queue |
| `StorageType` | `file` has higher throughput than `sqlite` |

## Querying traffic logs

### File storage queries

::: code-group

```powershell [Windows]
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

```bash [Linux]
# Find slow requests
cat log/traffic/proxy_traffic_*.json |
    jq 'select(.DurationMs > 1000) | {Timestamp, Method, Path, DurationMs}'

# Count requests by endpoint
cat log/traffic/proxy_traffic_*.json |
    jq -r '.EndpointName' | sort | uniq -c | sort -rn

# Find failed requests
cat log/traffic/proxy_traffic_*.json |
    jq 'select(.StatusCode >= 400) | {Timestamp, Path, StatusCode}'
```

:::

### SQLite queries

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
```

## Troubleshooting

| Symptom | Check |
|---------|-------|
| Logs not written | `Enabled: true` in config; write permissions on `LogDirectory` / `SqlitePath`; check application logs |
| Missing entries | Queue may be full, increase `QueueCapacity`; verify `FlushIntervalMs` is not too high |
| High performance impact | Disable body capture; increase `BatchSize` and `FlushIntervalMs`; switch to file storage |
| Disk filling up | Reduce `MaxFileCount`; reduce `MaxBodyCaptureSizeBytes`; disable body capture |
| SQLite errors | Check file permissions; ensure path directory exists; validate with `sqlite3 log/traffic_logs.db .tables` |
