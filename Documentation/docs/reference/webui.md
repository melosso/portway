# Web UI API Reference

The Web UI exposes REST endpoints for programmatic access to admin features.

## Authentication

The Web UI uses cookie-based authentication. Set `WebUi__AdminApiKey` to enable.

### Login

```http
POST /ui/api/auth
Content-Type: application/json

{
  "apiKey": "your-admin-api-key"
}
```

Response:
```json
{
  "ok": true,
  "expires": "2025-01-01T00:00:00Z"
}
```

Sets the `portway_auth` cookie.

---

## Endpoints

### GET /ui/api/overview

Dashboard overview data.

```http
GET /ui/api/overview
Authorization: Bearer {token}
```

Response:
```json
{
  "version": "1.0.0+build.123",
  "uptime_seconds": 3600,
  "endpoints": {
    "sql": 5,
    "proxy": 3,
    "static": 2,
    "files": 1,
    "webhooks": 2
  },
  "environments": ["dev", "test", "prod"],
  "server_name": "localhost"
}
```

### GET /ui/api/endpoints

All configured endpoints grouped by type.

```http
GET /ui/api/endpoints
Authorization: Bearer {token}
```

Response:
```json
{
  "sql": [
    {
      "name": "Products",
      "namespace": "Catalog",
      "path": "endpoints/SQL/Catalog/Products/entity.json",
      "schema": "dbo",
      "primary_key": "ProductId",
      "allowed_columns": ["ProductId", "Name", "Price"]
    }
  ],
  "proxy": [...],
  "static": [...],
  "files": [...],
  "webhooks": [...]
}
```

### GET /ui/api/environments

Environment configuration.

```http
GET /ui/api/environments
Authorization: Bearer {token}
```

Response:
```json
{
  "server_name": "localhost",
  "allowed_environments": ["dev", "test", "prod"],
  "environments": {
    "dev": { "connection_string": "..." },
    "prod": { "connection_string": "..." }
  }
}
```

### PATCH /ui/api/environments

Update environment configuration.

```http
PATCH /ui/api/environments
Authorization: Bearer {token}
Content-Type: application/json

{
  "allowed_environments": ["dev", "staging", "prod"],
  "environments": {
    "dev": { "connection_string": "Server=dev;..." },
    "prod": { "connection_string": "Server=prod;..." }
  }
}
```

### GET /ui/api/settings

Full application settings dump.

```http
GET /ui/api/settings
Authorization: Bearer {token}
```

Response:
```json
{
  "rate_limiting": {
    "enabled": true,
    "ip_limit": 100,
    "ip_window_seconds": 60,
    "token_limit": 100,
    "token_window_seconds": 60
  },
  "caching": {
    "enabled": true,
    "provider": "Memory",
    "default_duration_seconds": 300
  },
  "sql_pooling": {
    "enabled": true,
    "min_pool_size": 5,
    "max_pool_size": 100
  },
  "logging": {
    "min_level": "Information",
    "sinks": ["Console", "File"]
  }
}
```

### GET /ui/api/tokens

List API tokens.

```http
GET /ui/api/tokens
Authorization: Bearer {token}
```

```http
GET /ui/api/tokens?include_revoked=true
Authorization: Bearer {token}
```

Response:
```json
[
  {
    "id": 1,
    "username": "api-user",
    "description": "Production API",
    "created_at": "2025-01-01 00:00:00",
    "expires_at": null,
    "revoked_at": null,
    "allowed_scopes": "*",
    "allowed_environments": "*",
    "is_active": true
  }
]
```

### POST /ui/api/tokens

Create a new token.

```http
POST /ui/api/tokens
Authorization: Bearer {token}
Content-Type: application/json

{
  "username": "new-user",
  "description": "Read-only access",
  "allowed_scopes": "read",
  "allowed_environments": "dev,test",
  "expires_in_days": 90
}
```

Response:
```json
{
  "ok": true,
  "token": {
    "token": "pw_abc123...",
    "username": "new-user",
    "expires_at": "2025-04-01T00:00:00Z"
  }
}
```

### PUT /ui/api/tokens/{id}

Update token properties.

```http
PUT /ui/api/tokens/1
Authorization: Bearer {token}
Content-Type: application/json

{
  "allowed_scopes": "read,write",
  "allowed_environments": "dev,test,prod",
  "description": "Updated description",
  "expires_at": "2025-06-01T00:00:00Z"
}
```

### DELETE /ui/api/tokens/{id}

Revoke a token.

```http
DELETE /ui/api/tokens/1
Authorization: Bearer {token}
```

Response:
```json
{
  "ok": true
}
```

### POST /ui/api/tokens/{id}/rotate

Rotate (revoke and recreate) a token.

```http
POST /ui/api/tokens/1/rotate
Authorization: Bearer {token}
```

Response:
```json
{
  "ok": true,
  "token": {
    "token": "pw_xyz789...",
    "username": "api-user"
  }
}
```

### POST /ui/api/tokens/{id}/unarchive

Unarchive a revoked token.

```http
POST /ui/api/tokens/1/unarchive
Authorization: Bearer {token}
```

### GET /ui/api/tokens/{id}/audit

Get token audit log.

```http
GET /ui/api/tokens/1/audit
Authorization: Bearer {token}
```

Response:
```json
[
  {
    "operation": "Created",
    "timestamp": "2025-01-01 00:00:00",
    "details": "Token created",
    "ip_address": "192.168.1.1",
    "user_agent": "Portway/1.0"
  },
  {
    "operation": "Revoked",
    "timestamp": "2025-01-15 00:00:00",
    "details": "Revoked by admin",
    "ip_address": "192.168.1.1",
    "user_agent": "Portway/1.0"
  }
]
```

### GET /ui/api/logs

Browse application logs.

```http
GET /ui/api/logs?page=1&limit=50
Authorization: Bearer {token}
```

Query Parameters:
| Parameter | Default | Description |
|-----------|---------|-------------|
| `page` | 1 | Page number |
| `limit` | 50 | Items per page (max 100) |
| `level` | - | Filter by level (Debug, Information, Warning, Error) |
| `search` | - | Search in message |

Response:
```json
{
  "page": 1,
  "total_pages": 10,
  "logs": [
    {
      "timestamp": "2025-01-01T10:30:00.000Z",
      "level": "Information",
      "message": "Application started",
      "source": "Program"
    }
  ]
}
```

### GET /ui/api/events

Server-Sent Events stream for real-time updates.

```http
GET /ui/api/events
```

Events:

```json
event: health
data: {"status":"Healthy"}
```

```json
event: endpoint_reload
data: {"type":"sql","count":5}
```

---

## WebSocket Events

The UI subscribes to real-time events via SSE (Server-Sent Events).

### Event Types

| Event | Data | Description |
|-------|------|-------------|
| `health` | `{status: "Healthy|Degraded|Unhealthy"}` | Health status change |
| `endpoint_reload` | `{type, count}` | Endpoints reloaded |
| `token_created` | `{id, username}` | New token created |
| `token_revoked` | `{id, username}` | Token revoked |

---

## Error Responses

### 401 Unauthorized

```json
{
  "error": "Unauthorized",
  "message": "Valid authentication required"
}
```

### 403 Forbidden

```json
{
  "error": "Access denied",
  "clientIp": "192.168.1.100",
  "requestedPath": "/ui/dashboard"
}
```

### 404 Not Found

```json
{
  "error": "Not found",
  "message": "Resource does not exist"
}
```

### 409 Conflict

```json
{
  "error": "Conflict",
  "message": "Token with this username already exists"
}
```

---

## Rate Limiting

UI API endpoints are subject to rate limiting. Returns `429 Too Many Requests` when exceeded.
