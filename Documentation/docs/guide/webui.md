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

::: warning Admin key handling
Set the admin key via the `WebUi__AdminApiKey` environment variable or Azure Key Vault, never in `appsettings.json` — that file is not covered by Portway's automatic encryption. The shipped placeholder value is rejected in production (authentication stays disabled until replaced). Use a random key of 32+ characters; see [Security → Web UI admin key](/guide/security#web-ui-admin-key).
:::

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

Session cookies are HMAC-SHA256 signed with a 12-hour expiry. By default, the UI is accessible only from the local network. Set `WebUi__PublicOrigins` to allow access from external origins and enable `WebUi__SecureCookies` for HTTPS-only deployments.

All mutating UI API calls (POST/PUT/PATCH/DELETE) are protected by a CSRF double-submit check: the `portway_csrf` cookie issued at login must be echoed in the `X-CSRF-Token` header. The bundled pages handle this automatically; external automation must send the header itself.

Every configuration change made through the UI (environments, endpoints, MCP settings) is recorded in an audit trail, and the previous file version is backed up automatically. Both are visible on the Settings page under **Security & Change Controls**, where changes can also be restored.

The Web UI is optional. The gateway API functions without it.
