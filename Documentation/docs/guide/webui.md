---
title: Web UI
description: "Browser-based interface for monitoring endpoints, managing tokens, and browsing logs"
---

# Web UI

When you'd rather click through your gateway than query it, the Web UI gives you a browser view of your endpoints, tokens, logs, and settings. Sign-in uses an account. Until one exists, `/ui` is served without asking for anything, and a warning says so at startup.

## Configuration

The two settings you need to get started are the permitted origins and the cookie policy. The complete `WebUi` property reference, including the landing page and login page customisation options, lives in [Application settings](/reference/app-settings#web-ui-configuration).

```yaml
environment:
  - WebUi__PublicOrigins__0=https://example.com
  - WebUi__SecureCookies=true
```

Access the UI at `http://localhost:8080/ui` and sign in with a username and password.

The first account comes from `WebUi__AdminApiKey` if you already have one set: on the first start it becomes the account `admin`, with the key as its password. Once you can sign in, that setting is no longer read, and you can clear it from **Settings → Security → Deployment & Access**. Create the rest on the **Users** page, or from the shell:

```bash
portway accounts create <username> <password>
portway accounts password <username> <new-password>
```

::: warning Losing access
If nobody can sign in, reset a password with `portway accounts password`, run from the directory Portway runs in. See [Security → Recovering an account](/guide/security#recovering-an-account).
:::

## Pages

| Page | Description |
|---|---|
| **Dashboard** | Version, uptime, endpoint counts by type, health status |
| **Endpoints** | All configured endpoints grouped by type |
| **Environments** | Allowed environments and server names |
| **Tokens** | Create, revoke, rotate, and audit access tokens |
| **Users** | Accounts that can sign in, their roles and status |
| **Settings** | Security posture, feature switches, deployment access, rate limiting, caching, SQL pooling, logging |
| **Logs** | Paginated application log viewer |

## UI API endpoints

The UI exposes a REST API for automation and integration:

```
GET    /ui/api/overview
GET    /ui/api/endpoints
GET    /ui/api/environments
GET    /ui/api/settings
PUT    /ui/api/settings
GET    /ui/api/users
GET    /ui/api/users/me
POST   /ui/api/users
PUT    /ui/api/users/{id}
DELETE /ui/api/users/{id}
GET    /ui/api/tokens
POST   /ui/api/tokens
PUT    /ui/api/tokens/{id}
DELETE /ui/api/tokens/{id}
POST   /ui/api/tokens/{id}/rotate
GET    /ui/api/tokens/{id}/audit
GET    /ui/api/logs
GET    /ui/api/events
```

All `/ui/api/*` endpoints require the `portway_auth` session cookie, set at sign-in.

`PUT /ui/api/settings` takes a flat object of configuration keys and applies them together, or none of them. Only a fixed list of keys is writable; anything else is refused by name. Changes are written to `appsettings.overrides.json`, layered over `appsettings.json` so that file stays yours. The response says whether a restart is needed.

The **Security** section shows how the deployment is reachable: the client address Portway saw for your request, whether it honors forwarded headers, and whether the console is limited to the local network. **Deployment & Access** edits the settings behind those readings: trusted proxies, trusted proxy networks, and public console origins.

Those three settings decide who reaches the console, so Portway tests a change against your own request before storing it, and returns `400` when the new values would exclude you. Add an entry that matches your own address or origin, or edit `appsettings.json` on the server.

The seeding key can be removed here as well. `WebUi:AdminApiKey` accepts only an empty value from the console, which clears it.

## Security

Session cookies are HMAC-SHA256 signed with a 12-hour expiry, using `portway.key` next to `auth.db`. Deleting that file signs everyone out. By default, the UI is accessible only from the local network. Set `WebUi__PublicOrigins` to allow access from external origins and enable `WebUi__SecureCookies` for HTTPS-only deployments.

Accounts hold either the `administrator` or the `viewer` role. Viewers can read every page, change their own password, and link their own single sign-on identity. Every other write returns `403`. See [Account roles](/guide/security#account-roles).

Accounts can also sign in through an OpenID Connect provider alongside a password. See [Single sign-on](/guide/sso).

All mutating UI API calls (POST/PUT/PATCH/DELETE) are protected by a CSRF double-submit check: the `portway_csrf` cookie issued at login must be echoed in the `X-CSRF-Token` header. The bundled pages handle this automatically; external automation must send the header itself.

Every configuration change made through the UI (environments, endpoints, MCP settings) is recorded in an audit trail, and the previous file version is backed up automatically. Both are visible on the Settings page under **Security & Change Controls**, where changes can also be restored.

The Web UI is optional. The gateway API functions without it.
