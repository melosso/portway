---
title: Health Checks
description: "Health check endpoints, response format, component checks, and load balancer / container integration"
---

# Health Checks

Three health endpoints report the gateway status at increasing detail. All `/health*` paths are exempt from rate limiting.

## Available endpoints

### Basic health check

Unauthenticated, cached for 15 seconds.

```http
GET /health
```


```json
{
  "status": "Healthy",
  "timestamp": "2024-01-20T10:30:00Z",
  "cache_expires_in": "15 seconds"
}
```

| Status | Meaning |
|---|---|
| `Healthy` | All checks pass |
| `Degraded` | At least one check degraded |
| `Unhealthy` | At least one check failed |

### Liveness check

```http
GET /health/live
```

Unauthenticated, cached for 5 seconds, without downstream checks. Intended for liveness probes and load balancers.

```
Alive
```

### Detailed health check

```http
GET /health/details
Authorization: Bearer {token}
```

Each component check, cached for 60 seconds. Requires a token.

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

SQL connectivity per environment:

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

Reports connection availability, response time and authentication result.

### Disk space check

Free disk space:

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

| Free space | Status |
|---|---|
| Above 15% | `Healthy` |
| 5% to 15% | `Degraded` |
| Below 5% | `Unhealthy` |

### Proxy endpoints check

Upstream reachability of proxy endpoints:

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

All public proxy endpoints with GET are checked in parallel, with a 10-second timeout per endpoint. An upstream `401` counts as reachable.

## Implementation details

### Caching strategy

Cache durations:

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

### Failure examples

Database failure:

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

Proxy failure:

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
Invoke-WebRequest -Uri "http://localhost:5000/health"

# Check liveness
Invoke-WebRequest -Uri "http://localhost:5000/health/live"

# Get detailed status
$response = Invoke-WebRequest -Uri "http://localhost:5000/health/details" `
  -Headers @{"Authorization"="Bearer $token"}
$response.Content | ConvertFrom-Json | Format-List
```

```bash [Bash]
# Test basic health
curl http://localhost:5000/health

# Check liveness
curl http://localhost:5000/health/live

# Get detailed status
curl -H "Authorization: Bearer $TOKEN" http://localhost:5000/health/details | jq .
```

:::

### Log analysis

```log
[2024-01-20 10:30:00 DBG] Health check cache refreshed. Status: Healthy
[2024-01-20 10:30:01 WRN] Health check status: Low disk space, 10% remaining
[2024-01-20 10:30:02 WRN] Health check status: Unhealthy proxy endpoints detected (Products)
[2024-01-20 10:30:02 WRN] Health check status: Unhealthy SQL environments detected (prod)
```

Per-endpoint and per-environment failure reasons are logged at `Debug` and returned by `/health/details`.
