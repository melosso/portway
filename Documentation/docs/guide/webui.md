# Web UI

A built-in admin interface for managing Portway, allowing you to *partially* monitor and configure the application outside of the console.

## Quick Start

1. Set `WebUi__AdminApiKey` in your configuration
2. Visit `http://localhost:8080/ui`
3. Log in with your admin API key

If this setting isn't configured, the interface won't launch.

## Configuration

| Variable | Description | Default |
|----------|-------------|---------|
| `WebUi__AdminApiKey` | Login password | (disabled) |
| `WebUi__PublicOrigins` | Allowed CORS origins | (local only) |
| `WebUi__SecureCookies` | Require HTTPS for cookies | `false` |

```yaml
environment:
  - WebUi__AdminApiKey=your-secure-password
  - WebUi__PublicOrigins__0=https://example.com
  - WebUi__SecureCookies=true
```

## Features

| Page | Description |
|------|-------------|
| **Dashboard** | Overview: version, uptime, endpoint counts, health status |
| **Endpoints** | View all configured endpoints by type (SQL, Proxy, Static, Files, Webhooks) |
| **Environments** | Manage allowed environments and connection strings |
| **Tokens** | Create, revoke, rotate, and audit API tokens |
| **Settings** | View rate limiting, caching, SQL pooling, logging config |
| **Logs** | Browse and search application logs |

## Security

- Access restricted by IP (local network) by default
- Add `WebUi__PublicOrigins` to allow external access
- Cookie-based authentication with HMAC-SHA256
- All token operations are audited

## API Endpoints

The UI also exposes these endpoints for automation:

```
GET  /ui/api/overview      - Dashboard data
GET  /ui/api/endpoints    - All endpoints
GET  /ui/api/environments - Environment config
GET  /ui/api/settings     - Full settings dump
GET  /ui/api/tokens       - List tokens
POST /ui/api/tokens       - Create token
PUT  /ui/api/tokens/{id}  - Update token
DELETE /ui/api/tokens/{id} - Revoke token
POST /ui/api/tokens/{id}/rotate - Rotate token
GET  /ui/api/tokens/{id}/audit - Token audit log
GET  /ui/api/logs         - Paginated logs
GET  /ui/api/events       - Server-Sent Events stream
```

> [!TIP]
> The Web UI is optional. The API is fully functional without it.
