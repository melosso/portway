---
title: Web UI API Reference
description: "Everything you can do by clicking through the Web UI, you can also do programmatically"
---

# Web UI API Reference

Everything you can do by clicking through the Web UI, you can also do programmatically. The UI is built on the same REST endpoints documented here, which makes them a convenient surface for scripting your admin tasks.

## Authentication

The Web UI uses cookie-based authentication against a console account. See [Web UI](/guide/webui#configuration) for how the first account is created.

### Login

Fetch a one-time CSRF token, then post it with the credentials:

```http
GET /ui/api/auth/csrf
```

```json
{ "csrf": "..." }
```

```http
POST /ui/api/auth
Content-Type: application/json

{
  "username": "admin",
  "password": "your-password",
  "csrf": "..."
}
```

Response:
```json
{ "ok": true }
```

Sets the `portway_auth` and `portway_csrf` cookies.

An account that has not chosen its own password yet answers with `{ "ok": false, "must_change_password": true }` and no cookies. Complete the sign-in by posting a new password with a fresh CSRF token:

```http
POST /ui/api/auth/password
Content-Type: application/json

{
  "username": "admin",
  "password": "your-current-password",
  "newPassword": "your-new-password",
  "csrf": "..."
}
```

### Making changes

On `POST`, `PUT`, `PATCH` and `DELETE` under `/ui/api`, send the `portway_csrf` cookie value back in an `X-CSRF-Token` header. Requests without it return `403`.

These also return `403` for an account holding the `viewer` role, apart from linking and unlinking its own single sign-on identity. See [Account roles](/guide/security#account-roles).

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

### POST /ui/api/endpoints/{type}/validate

Checks an endpoint configuration without saving it, using the same rules the loader applies at startup. This is a convenient pre-flight before writing an `entity.json`, whether by hand or from automation.

```http
POST /ui/api/endpoints/sql/validate
Content-Type: application/json
X-CSRF-Token: {csrf}

{ "content": { "DatabaseObjectName": "Products", "DatabaseSchema": "dbo" } }
```

Response:
```json
{ "valid": true, "errors": [] }
```

The `content` field accepts either a JSON object or a string containing JSON. Validation covers JSON syntax, the per-type required fields (`DatabaseObjectName` for SQL and Webhook, `Url` and `Methods` for Proxy and Composite), and the namespace naming rules. A failed check returns `valid: false` with each problem listed in `errors`; the endpoint on disk is never touched.

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
  },
  "security": {
    "webui_auth_enabled": true,
    "admin_accounts": 2,
    "https_enabled": true,
    "secure_cookies": true,
    "client_ip": "203.0.113.9",
    "behind_proxy": true,
    "forwarded_ignored": false,
    "console_public": false,
    "trusted_proxies_configured": true,
    "csrf_protection": true
  },
  "features": {
    "oidc": true,
    "openapi": true,
    "traffic_logging": false,
    "landing_page": true,
    "oidc_providers": 1
  },
  "deployment": {
    "public_origins": [],
    "known_proxies": ["127.0.0.1"],
    "known_networks": []
  },
  "writable": [
    { "key": "RateLimiting:Enabled", "kind": "bool", "requires_restart": true }
  ]
}
```

`security.client_ip` is the client address Portway saw for the request. `forwarded_ignored` is `true` when a forwarded address arrived with no proxy trusted to send it. `writable` lists the keys `PUT /ui/api/settings` accepts, with the validation applied to each.

### PUT /ui/api/settings

Applies a flat object of configuration keys together, or none of them.

```http
PUT /ui/api/settings
Content-Type: application/json
X-CSRF-Token: {csrf}

{
  "RateLimiting:IpLimit": 200,
  "ForwardedHeaders:KnownProxies": ["127.0.0.1", "::1"]
}
```

Response:
```json
{ "ok": true, "restart_required": true }
```

The call returns `400` with an `error` and the offending `field` for a key outside the writable list, a value outside its range, or a change to `WebUi:PublicOrigins` or the proxy lists that would stop your own requests reaching the console.

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

### PUT /ui/api/tokens/\{id\}

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

### DELETE /ui/api/tokens/\{id\}

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

## SSE events

The UI subscribes to real-time updates via Server-Sent Events.

### Event types

| Event | Data | Description |
|-------|------|-------------|
| `health` | `{status: "Healthy|Degraded|Unhealthy"}` | Health status change |
| `endpoint_reload` | `{type, count}` | Endpoints reloaded |
| `token_created` | `{id, username}` | New token created |
| `token_revoked` | `{id, username}` | Token revoked |

---

## Error responses

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

## Rate limiting

UI API endpoints are subject to rate limiting. Returns `429 Too Many Requests` when exceeded.
