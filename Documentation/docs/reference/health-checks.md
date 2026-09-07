---
title: Health Checks
description: "Health check endpoints, response format, component checks, and load balancer / container integration"
---

# Health Checks

Whether it's a load balancer deciding where to send traffic or you checking in on a quiet Sunday evening, something will regularly ask Portway how it's doing. These endpoints provide the answer at three levels of detail.

## Available endpoints

### Basic health check

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

### Liveness check

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

### Detailed health check

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

## Health check components

### Database check

Verifies SQL database connectivity:

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

### Disk space check

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

### Proxy endpoints check

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
- All public proxy endpoints that allow GET are checked in parallel
- Timeout handling (10 seconds per endpoint)
- Authentication status checking (a 401 from the upstream counts as reachable)

## Implementation details

### Caching strategy

Health checks use intelligent caching to prevent overload:

| Endpoint | Cache duration |
|----------|---------------|
| `/health` | 15 seconds |
| `/health/live` | 5 seconds |
| `/health/details` | 60 seconds |

### Response headers

```http
Cache-Control: public, max-age=15
Expires: Sun, 20 Jan 2024 10:30:15 GMT
```

### Error handling

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

## Load balancer configuration

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

## Kubernetes integration

### Liveness probe

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

### Readiness probe

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

### Startup probe

```yaml
startupProbe:
  httpGet:
    path: /health/live
    port: 5000
  failureThreshold: 30
  periodSeconds: 10
```

## Troubleshooting

### Diagnostic commands

::: code-group

```powershell [PowerShell]
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

```bash [Bash]
# Test basic health
curl -H "Authorization: Bearer $TOKEN" http://localhost:5000/health

# Check liveness
curl http://localhost:5000/health/live

# Get detailed status
curl -H "Authorization: Bearer $TOKEN" http://localhost:5000/health/details | jq .
```

:::

### Log analysis

```log
[10:30:00 INF] Health check cache refreshed. Status: Healthy
[10:30:01 WRN] Health check failed: Low disk space: 10% remaining
[10:30:02 ERR] Proxy endpoint 'Products' failed: Connection timeout
```
