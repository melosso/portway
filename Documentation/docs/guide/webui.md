# Web UI

> Browser-based interface for monitoring endpoints, managing tokens, and browsing logs.

The Web UI is disabled by default. Set `WebUi__AdminApiKey` to enable it. Without this setting, `/ui` is not served.

## Configuration

| Variable | Description | Default |
|---|---|---|
| `WebUi__AdminApiKey` | Login password for the UI | _(disabled)_ |
| `WebUi__PublicOrigins` | CORS origins permitted to access the UI | Local only |
| `WebUi__SecureCookies` | Require HTTPS for session cookies | `false` |

```yaml
environment:
  - WebUi__AdminApiKey=your-secure-password
  - WebUi__PublicOrigins__0=https://example.com
  - WebUi__SecureCookies=true
```

Once configured, access the UI at `http://localhost:8080/ui` and log in with the admin key.

## Pages

| Page | Description |
|---|---|
| **Dashboard** | Version, uptime, endpoint counts by type, health status |
| **Endpoints** | All configured endpoints grouped by type |
| **Environments** | Allowed environments and server names |
| **Tokens** | Create, revoke, rotate, and audit access tokens |
| **Settings** | Rate limiting, caching, SQL pooling, logging configuration |
| **Logs** | Paginated application log viewer |

## UI API endpoints

The UI exposes a REST API for automation and integration:

```
GET    /ui/api/overview
GET    /ui/api/endpoints
GET    /ui/api/environments
GET    /ui/api/settings
GET    /ui/api/tokens
POST   /ui/api/tokens
PUT    /ui/api/tokens/{id}
DELETE /ui/api/tokens/{id}
POST   /ui/api/tokens/{id}/rotate
GET    /ui/api/tokens/{id}/audit
GET    /ui/api/logs
GET    /ui/api/events
```

All `/ui/api/*` endpoints require the `portway_auth` session cookie (set at login) or the `Authorization: Bearer {adminApiKey}` header.

## Security

Session cookies are HMAC-SHA256 signed with a 24-hour expiry. By default, the UI is accessible only from the local network. Set `WebUi__PublicOrigins` to allow access from external origins and enable `WebUi__SecureCookies` for HTTPS-only deployments.

The Web UI is optional. The gateway API functions without it.
