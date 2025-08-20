# Health Checks

Portway provides comprehensive health check endpoints to monitor the status of the API, its dependencies, and underlying services. These endpoints are essential for monitoring, alerting, and automated deployment processes.

## Available Endpoints

### Basic Health Check

```http
GET /health
Authorization: Bearer {token}
```

Returns basic health status:

```json
{
  "status": "Healthy",
  "timestamp": "2024-01-20T10:30:00Z",
  "cache_expires_in": "15 seconds"
}
```

**Status Values:**
- `Healthy` - All checks passing
- `Degraded` - Some issues detected
- `Unhealthy` - Critical issues found

### Liveness Check

```http
GET /health/live
```

Simple endpoint for container orchestration:

```
Alive
```

**Features:**
- No authentication required
- Minimal overhead
- 5-second cache
- Used by load balancers and Kubernetes

### Detailed Health Check

```http
GET /health/details
Authorization: Bearer {token}
```

Comprehensive health information:

```json
{
  "status": "Healthy",
  "timestamp": "2024-01-20T10:30:00Z",
  "cache_expires_in": "60 seconds",
  "checks": [
    {
      "name": "Database",
      "status": "Healthy",
      "description": "Database connection successful",
      "duration": "45.23ms",
      "data": {
        "connectionString": "Configured",
        "responseTime": "12ms"
      },
      "tags": ["database", "sql"]
    },
    {
      "name": "Diskspace",
      "status": "Healthy",
      "description": "Disk space: 65% remaining",
      "duration": "2.15ms",
      "data": {
        "percentFree": "65%"
      },
      "tags": ["storage", "system"]
    },
    {
      "name": "ProxyEndpoints",
      "status": "Healthy",
      "description": "All proxy services are responding",
      "duration": "234.56ms",
      "data": {
        "Account": {
          "Status": "Healthy",
          "StatusCode": 200,
          "ReasonPhrase": "OK"
        },
        "Products": {
          "Status": "Healthy",
          "StatusCode": 401,
          "ReasonPhrase": "Unauthorized - Marked as Healthy"
        }
      },
      "tags": ["proxies", "external", "readiness"]
    }
  ],
  "totalDuration": "282.94ms",
  "version": "1.0.0"
}
```

## Health Check Components

### Database Check

Verifies SQL Server connectivity:

```json
{
  "name": "Database",
  "status": "Healthy",
  "description": "Database connection successful",
  "duration": "45.23ms",
  "data": {
    "connectionString": "Configured",
    "responseTime": "12ms"
  }
}
```

**Checks:**
- Connection availability
- Response time
- Authentication status

### Disk Space Check

Monitors available storage:

```json
{
  "name": "Diskspace",
  "status": "Degraded",
  "description": "Low disk space: 15% remaining",
  "duration": "2.15ms",
  "data": {
    "percentFree": "15%"
  }
}
```

**Thresholds:**
- Healthy: > 15% free
- Degraded: 5-15% free
- Unhealthy: < 5% free

### Proxy Endpoints Check

Tests external service connectivity:

```json
{
  "name": "ProxyEndpoints",
  "status": "Healthy",
  "description": "All proxy services are responding",
  "duration": "234.56ms",
  "data": {
    "Account": {
      "Status": "Healthy",
      "StatusCode": 200
    },
    "Products": {
      "Status": "Unhealthy",
      "Error": "Connection timeout"
    }
  }
}
```

**Features:**
- Random endpoint sampling
- Timeout handling (5 seconds)
- Authentication status checking

## Configuration

### appsettings.json

```json
{
  "HealthChecks": {
    "Enabled": true,
    "CacheTimeSeconds": 30,
    "ProxyTimeout": 5000,
    "DiskSpaceWarningThreshold": 15,
    "DiskSpaceCriticalThreshold": 5
  }
}
```

### Environment Variables

```bash
# Override cache duration
HEALTHCHECK_CACHE_TIME=60

# Disable proxy checks
HEALTHCHECK_SKIP_PROXY=true

# Custom disk thresholds
HEALTHCHECK_DISK_WARNING=20
HEALTHCHECK_DISK_CRITICAL=10
```

## Implementation Details

### Caching Strategy

Health checks use intelligent caching to prevent overload:

```csharp
// Cache durations
- /health: 15 seconds
- /health/live: 5 seconds
- /health/details: 60 seconds
```

### Response Headers

```http
Cache-Control: public, max-age=15
Expires: Sun, 20 Jan 2024 10:30:15 GMT
```

### Error Handling

Health checks handle failures gracefully:

1. **Database Errors**
   ```json
   {
     "name": "Database",
     "status": "Unhealthy",
     "description": "Connection failed: Timeout",
     "duration": "5000ms",
     "data": {
       "error": "SqlException: Connection timeout"
     }
   }
   ```

2. **Proxy Failures**
   ```json
   {
     "name": "ProxyEndpoints",
     "status": "Degraded",
     "description": "Some services unavailable",
     "data": {
       "Account": {
         "Status": "Healthy"
       },
       "Products": {
         "Status": "Unhealthy",
         "Error": "HTTP 503 Service Unavailable"
       }
     }
   }
   ```

## Monitoring Integration

### Prometheus Metrics

```prometheus
# Health check status
portway_health_status{component="database"} 1
portway_health_status{component="diskspace"} 1
portway_health_status{component="proxy"} 0

# Response times
portway_health_duration_ms{component="database"} 45.23
portway_health_duration_ms{component="proxy"} 234.56

# Disk space
portway_disk_free_percent 65
```

### Grafana Dashboard

```json
{
  "dashboard": {
    "panels": [
      {
        "title": "System Health",
        "type": "stat",
        "targets": [
          {
            "expr": "portway_health_status",
            "legendFormat": "{{component}}"
          }
        ]
      },
      {
        "title": "Response Times",
        "type": "graph",
        "targets": [
          {
            "expr": "portway_health_duration_ms",
            "legendFormat": "{{component}}"
          }
        ]
      }
    ]
  }
}
```

### Azure Monitor

```json
{
  "type": "Microsoft.Insights/metricAlerts",
  "properties": {
    "criteria": {
      "allOf": [
        {
          "name": "HealthCheckFailure",
          "metricName": "HealthCheckStatus",
          "operator": "LessThan",
          "threshold": 1,
          "timeAggregation": "Average"
        }
      ]
    },
    "actions": [
      {
        "actionGroupId": "/subscriptions/.../actionGroups/critical"
      }
    ]
  }
}
```

## Load Balancer Configuration

### IIS ARR

```xml
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <rule name="Health Check">
          <match url="^health/live$" />
          <action type="Rewrite" url="http://backend/health/live" />
        </rule>
      </rules>
    </rewrite>
    <applicationRequestRouting>
      <health checkUrl="http://backend/health/live" />
    </applicationRequestRouting>
  </system.webServer>
</configuration>
```

### NGINX

```nginx
upstream portway_api {
    server backend1:5000;
    server backend2:5000;
    
    # Health check configuration
    health_check uri=/health/live interval=5s;
}

server {
    location /health/live {
        proxy_pass http://portway_api;
        proxy_cache off;
    }
}
```

### HAProxy

```haproxy
backend portway_api
    option httpchk GET /health/live
    http-check expect status 200
    
    server api1 10.0.1.10:5000 check inter 5s
    server api2 10.0.1.11:5000 check inter 5s
```

## Kubernetes Integration

### Liveness Probe

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: portway-api
spec:
  template:
    spec:
      containers:
      - name: portway
        livenessProbe:
          httpGet:
            path: /health/live
            port: 5000
          initialDelaySeconds: 10
          periodSeconds: 10
          timeoutSeconds: 5
```

### Readiness Probe

```yaml
readinessProbe:
  httpGet:
    path: /health
    port: 5000
    httpHeaders:
    - name: Authorization
      value: Bearer ${HEALTH_CHECK_TOKEN}
  initialDelaySeconds: 15
  periodSeconds: 20
  timeoutSeconds: 10
```

### Startup Probe

```yaml
startupProbe:
  httpGet:
    path: /health/live
    port: 5000
  failureThreshold: 30
  periodSeconds: 10
```

## Troubleshooting

### Common Issues

1. **Health Check Timeouts**
   ```
   Symptom: Health checks timing out
   Cause: Slow database or proxy responses
   Solution: Increase timeout values or investigate performance
   ```

2. **Cache Issues**
   ```
   Symptom: Stale health data
   Cause: Cache not expiring properly
   Solution: Check cache headers and configuration
   ```

3. **Authentication Failures**
   ```
   Symptom: 401 Unauthorized on /health
   Cause: Missing or invalid token
   Solution: Provide valid authentication token
   ```

### Diagnostic Commands

```powershell
# Test basic health
Invoke-WebRequest -Uri "http://localhost:5000/health" `
  -Headers @{"Authorization"="Bearer $token"}

# Check liveness
Invoke-WebRequest -Uri "http://localhost:5000/health/live"

# Get detailed status
$response = Invoke-WebRequest -Uri "http://localhost:5000/health/details" `
  -Headers @{"Authorization"="Bearer $token"}
$response.Content | ConvertFrom-Json | Format-List
```

### Log Analysis

```log
[10:30:00 INF] Health check cache refreshed. Status: Healthy
[10:30:01 WRN] Health check failed: Low disk space: 10% remaining
[10:30:02 ERR] Proxy endpoint 'Products' failed: Connection timeout
```

## Best Practices

### 1. Authentication Strategy

Create a seperate health token if necessary:

```csharp
// Create dedicated health check token
TokenGenerator.exe health-monitor -s "health*" --expires 365

// Use in monitoring systems
$env:HEALTH_CHECK_TOKEN = "eyJhbGci..."
```

### 2. Performance Optimization

Make sure to tweak the health settings for it to fit your requirements:

```json
{
  "HealthChecks": {
    "ParallelExecution": true,
    "MaxConcurrentChecks": 5,
    "TimeoutSeconds": 10
  }
}
```
