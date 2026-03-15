# Logging

> Serilog configuration reference for log levels, file rotation, and structured output.

## Log Outputs

### Console Logging
- Displays real-time logs with timestamp formatting
- Information level and above shown by default
- Color-coded by severity level
- Useful for development and debugging

### File Logging
- Stored in the `/log` directory
- Daily rotation with pattern: `portwayapi-YYYYMMDD.log`
- 10MB file size limit with automatic rollover
- Retains 10 days of log files
- Buffered writing for performance

## Log Levels

| Level | Description | Examples |
|-------|-------------|----------|
| Debug | Detailed diagnostic information | Database queries, method execution |
| Information | Normal operational events | API requests, successful operations |
| Warning | Unexpected but handled situations | Missing configuration, fallback behavior |
| Error | Failures and exceptions | Database errors, API failures |
| Fatal | Critical failures | Application startup failures |


## Configuration

Logging is configured in `appsettings.json` under the `Serilog` section. See [Application Settings](/reference/app-settings) for the full configuration schema.

## Log File Management

### Rotation Policy
- Daily rotation at midnight
- Size-based rotation at 10MB
- Automatic file naming with date suffix

### Retention Policy
- Keeps last 10 log files
- Older files automatically deleted
- Configurable retention period

### File Naming Convention
```
log/
├── portwayapi-20240120.log
├── portwayapi-20240119.log
└── portwayapi-20240118.log
```

## Performance Logging

### Request Timing
```
[DBG] Incoming request: POST /api/500/Orders
[DBG] Outgoing response: 200 for /api/500/Orders - Took 125ms
```

### Rate Limiting
```
[INF] Rate limiter initialized - IP: 100/60s, Token: 1000/60s
[INF] IP 192.168.1.100 has exceeded rate limit, blocking for 60s
[DBG] Rate limit for IP 192.168.1.100 has expired, allowing traffic
```

## Structured Logging

### Event Properties
The logging system captures structured data for better analysis:
- Request method and path
- User identity and token information
- Environment and endpoint names
- Duration and status codes
- Error details and stack traces

### Context Enrichment
Logs are automatically enriched with:
- Machine name
- Application version
- Request correlation IDs
- User context
- Environment information

## Troubleshooting

### Common Issues

1. **Missing Log Files**
   - Check write permissions on log directory
   - Verify log path configuration
   - Ensure application has started successfully

2. **Excessive Log Volume**
   - Adjust minimum log levels
   - Enable filtering for noisy components
   - Configure appropriate retention policies

3. **Performance Impact**
   - Enable buffered writing
   - Increase flush intervals
   - Filter high-frequency events

### Diagnostic Tools

PowerShell commands for log analysis:
```powershell
# Find errors in today's log
Get-Content "log/portwayapi-$(Get-Date -Format 'yyyyMMdd').log" | Select-String "ERR"

# Count requests by endpoint
Get-Content "log/portwayapi-*.log" | Select-String "Processing.*endpoint:" | Group-Object

# Monitor log growth
Get-ChildItem "log" -Filter "*.log" | Sort-Object LastWriteTime -Descending | Select-Object Name, Length
```