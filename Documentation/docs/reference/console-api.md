---
title: Console API
description: "Console API routes under /_/api, session authentication and CSRF"
---

# Console API

The console at `/_` uses the routes under `/_/api`. They are authenticated by a console session, not by API tokens, and may change between releases.

## Authentication

```http
GET /_/api/auth/csrf
```

```http
POST /_/api/auth
Content-Type: application/json

{ "username": "admin", "password": "your-password", "csrf": "..." }
```

A successful sign-in returns `{ "ok": true }` and sets the `portway_auth` and `portway_csrf` cookies. An account that must change its password returns `{ "ok": false, "must_change_password": true }`; `POST /_/api/auth/password` with `username`, `password`, `newPassword` and a new `csrf` completes the sign-in.

Write requests (`POST`, `PUT`, `PATCH`, `DELETE`) send the `portway_csrf` cookie value in an `X-CSRF-Token` header; without it they return `403`. Accounts with the `viewer` role receive `403` on writes, except linking and unlinking their own single sign-on identity ([Account roles](/guide/accounts#account-roles)).

## Routes

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/_/api/overview` | Dashboard data |
| `GET` | `/_/api/metrics` | Request metrics (`period`: `24h`, `7d`, `30d`) |
| `DELETE` | `/_/api/metrics` | Deletes all recorded request metrics |
| `GET` | `/_/api/metrics/health` | Success rate, failures and latency percentiles (`period`: `1h`, `24h`, `7d`, `30d`; `env`, `endpoint`, `version`, `method`) |
| `GET` | `/_/api/events` | Server-sent events |
| `GET` | `/_/api/logs` | Entries from the three newest `log/portwayapi-*.log` files, newest first (`limit` default `200`, maximum `2000`; `offset`; `level` `ALL`, `INF`, `WRN`, `ERR`, `DBG`) |
| `GET` | `/_/api/audit` | Configuration change history |
| `POST` | `/_/api/audit/{id}/restore` | Restores a saved configuration version |
| `GET` | `/_/api/endpoints` | Endpoints by type |
| `GET`, `PUT`, `DELETE` | `/_/api/endpoints/{type}/{name}` | Reads, saves or deletes an `entity.json` |
| `POST` | `/_/api/endpoints/{type}` | Creates an endpoint |
| `POST` | `/_/api/endpoints/{type}/validate` | Validates an endpoint without saving |
| `GET`, `POST` | `/_/api/environments` | Lists or creates environments |
| `PUT` | `/_/api/environments/settings` | Saves `environments/settings.json` |
| `GET`, `PUT`, `DELETE` | `/_/api/environments/{name}` | Reads, saves or deletes an environment |
| `GET`, `PUT` | `/_/api/environments/{name}/raw` | Environment file as JSON text |
| `POST` | `/_/api/environments/{name}/test` | Tests the connection |
| `GET`, `PUT` | `/_/api/settings` | Application settings; writes go to `appsettings.overrides.json` |
| `GET`, `POST` | `/_/api/tokens` | Lists or creates tokens |
| `PUT`, `DELETE` | `/_/api/tokens/{id}` | Updates or archives a token |
| `POST` | `/_/api/tokens/{id}/rotate` | Rotates a token |
| `POST` | `/_/api/tokens/{id}/unarchive` | Restores an archived token |
| `GET` | `/_/api/tokens/{id}/audit` | Token history |
| `GET` | `/_/api/ratelimits` | Active rate limit blocks |
| `GET`, `POST`, `DELETE` | `/_/api/mcp/config` | MCP chat configuration |
| `GET`, `POST` | `/_/api/users` | Console accounts |
| `PUT`, `DELETE` | `/_/api/users/{id}` | Updates or deletes an account |
| `GET` | `/_/api/users/me` | Signed-in account |
| `GET`, `POST` | `/_/api/oidc/providers` | Single sign-on providers |
| `PUT`, `DELETE` | `/_/api/oidc/providers/{id}` | Updates or deletes a provider |

## Token fields

Fields accepted by `POST /_/api/tokens` and `PUT /_/api/tokens/{id}`:

| Field | Type | Description |
|---|---|---|
| `username` | string | Token name; create only, required |
| `description` | string | Description |
| `allowed_scopes` | string | Endpoint patterns, comma-separated; default `*` |
| `allowed_environments` | string | Environment patterns, comma-separated; default `*` |
| `allowed_tenants` | object | Header name to allowed values, e.g. `{ "X-Company-Id": ["100", "200"] }` |
| `expires_in_days` | integer | Lifetime on create |
| `expires_at` | string | Expiry date on update |
| `rate_limit_requests` | integer | Per-token request limit |
| `rate_limit_window_seconds` | integer | Window for that limit |

Create returns `{ "ok": true, "token": "..." }`; the token value is returned only in this response. A change that narrows the last full-access token returns `409`.

## Related topics

- [Web UI](/guide/webui)
- [Tokens](/guide/tokens)
- [Token Audit Log](/reference/token-audit)
