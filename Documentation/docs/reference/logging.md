# Logging

Portway implements comprehensive logging using Serilog, providing detailed insights into API operations, errors, and system health. The logging system captures events with configurable verbosity levels and outputs to both console and file storage.

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

## Log Categories

### Startup Logging

```
🔑 Portway Token Generator
=================================
✅ Portway has started successfully
🌎 Environment: Production
🖥️ Host: SERVER-01
💾 Working Directory: C:\PortwayApi
⏰ Current Time: 2025-05-20 10:30:00
🔧 .NET Version: 9.0.0
💻 OS: Microsoft Windows 10.0.x
```

### API Request Logging

```
📥 Incoming request: GET /api/600/Products?$top=10
🔄 Processing SQL endpoint: Products for GET
✅ Successfully processed query for Products
📤 Outgoing response: 200 for /api/600/Products - Took 45ms
```

### Authentication Logging

```
✅ Authorized admin for GET /api/600/Orders
❌ Token lacks permission for endpoint Orders. Available scopes: Products
❌ Invalid or expired token used for /api/600/Customers
❌ Authorization header missing for /api/600/Products
```

### Database Operations

```
🔧 Building EDM model for entity: Products
🔄 Converting OData to SQL for entity: Products
✅ Successfully converted OData to SQL
📊 SQL Connection Pool Status: Active=5, Idle=15
✅ Connection pool prewarmed with 5 connections
```

### Error Logging

```
❌ Error processing GET request for /api/600/InvalidEndpoint
❌ Error during SQL query for endpoint: Products
❌ Error during proxy request: Accounts
❌ Database initialization failed: Invalid connection string
```

## Configuration Settings

The logging system can be configured through:

### Application Settings
- Minimum log levels per category
- Output destinations
- File rotation settings
- Buffer and flush intervals

### Environment Variables
- Override default log levels
- Enable/disable specific categories
- Set custom output paths

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

### Connection Pool Monitoring
```
🔌 Created SQL SERVER rate limit bucket for pool
🔄 Reopened maintenance connection for pool
📊 SQL Connection Pool Status: Connection pool statistics logging
```

### Request Timing
```
📥 Incoming request: POST /api/600/Orders
📤 Outgoing response: 200 for /api/600/Orders - Took 125ms
```

### Rate Limiting
```
🚦 Rate limiter initialized - IP: 100/60s, Token: 1000/60s
🚫 IP 192.168.1.100 has exceeded rate limit, blocking for 60s
✅ Rate limit for IP 192.168.1.100 has expired, allowing traffic
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

## Best Practices

### 1. Log Level Selection
- Use Information for standard operations
- Reserve Debug for development troubleshooting
- Use Warning for handled exceptions
- Use Error for failures requiring attention

### 2. Sensitive Data Protection
- Authorization headers are automatically redacted
- Passwords and tokens masked in logs
- Personal data handled according to privacy settings

### 3. Performance Considerations
- Buffered file writing for efficiency
- Asynchronous logging operations
- Filtered exclusions for high-frequency endpoints

### 4. Log Analysis
- Use structured properties for filtering
- Correlate requests using trace IDs
- Monitor error patterns for alerts

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

## Security Considerations

### Access Control
- Log directory should be protected from web access
- Restrict read permissions to authorized personnel
- Implement log forwarding for centralized security

### Compliance Requirements
- Retain logs according to regulatory requirements
- Ensure PII is properly handled
- Implement audit trails for log access

### Data Protection
- Automatic redaction of sensitive headers
- Configurable PII handling
- Encrypted storage options available