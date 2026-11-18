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

SQL connectivity per environment. Reports connection availability, response time and authentication result.

### Disk space check

Free disk space:

| Free space | Status |
|---|---|
| Above 15% | `Healthy` |
| 5% to 15% | `Degraded` |
| Below 5% | `Unhealthy` |

### Proxy endpoints check

Upstream reachability of proxy endpoints. All public proxy endpoints with GET are checked in parallel, with a 10-second timeout per endpoint. An upstream `401` counts as reachable.

## Response headers

Responses include `Cache-Control: public, max-age={cache duration}` and a matching `Expires` header.

## Load balancer configuration

### NGINX Plus

```nginx
upstream portway_api {
    server backend1:8080;
    server backend2:8080;
    
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
    
    server api1 10.0.1.10:8080 check inter 5s
    server api2 10.0.1.11:8080 check inter 5s
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
            port: 8080
          initialDelaySeconds: 10
          periodSeconds: 10
          timeoutSeconds: 5
```

### Readiness probe

```yaml
readinessProbe:
  httpGet:
    path: /health
    port: 8080
  initialDelaySeconds: 15
  periodSeconds: 20
  timeoutSeconds: 10
```

### Startup probe

```yaml
startupProbe:
  httpGet:
    path: /health/live
    port: 8080
  failureThreshold: 30
  periodSeconds: 10
```

## Troubleshooting

### Diagnostic commands

::: code-group

```powershell [Windows]
# Test basic health
Invoke-WebRequest -Uri "http://localhost:8080/health"

# Check liveness
Invoke-WebRequest -Uri "http://localhost:8080/health/live"

# Get detailed status
$response = Invoke-WebRequest -Uri "http://localhost:8080/health/details" `
  -Headers @{"Authorization"="Bearer $token"}
$response.Content | ConvertFrom-Json | Format-List
```

```bash [Linux]
# Test basic health
curl http://localhost:8080/health

# Check liveness
curl http://localhost:8080/health/live

# Get detailed status
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/health/details | jq .
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
