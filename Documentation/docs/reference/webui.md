---
title: Web UI API Reference
description: "Console API routes under /ui/api, session authentication and CSRF"
---

# Web UI API Reference

The console at `/ui` uses the routes under `/ui/api`. They are authenticated by a console session, not by API tokens, and may change between releases.

## Authentication

```http
GET /ui/api/auth/csrf
```

```http
POST /ui/api/auth
Content-Type: application/json

{ "username": "admin", "password": "your-password", "csrf": "..." }
```

A successful sign-in returns `{ "ok": true }` and sets the `portway_auth` and `portway_csrf` cookies. An account that must change its password returns `{ "ok": false, "must_change_password": true }`; `POST /ui/api/auth/password` with `username`, `password`, `newPassword` and a new `csrf` completes the sign-in.

Write requests (`POST`, `PUT`, `PATCH`, `DELETE`) send the `portway_csrf` cookie value in an `X-CSRF-Token` header; without it they return `403`. Accounts with the `viewer` role receive `403` on writes, except linking and unlinking their own single sign-on identity ([Account roles](/guide/security#account-roles)).

## Routes

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/ui/api/overview` | Dashboard data |
| `GET` | `/ui/api/metrics` | Request metrics |
| `GET` | `/ui/api/events` | Server-sent events |
| `GET` | `/ui/api/logs` | Application log entries (`limit`, `offset`, `level`) |
| `GET` | `/ui/api/audit` | Configuration change history |
| `POST` | `/ui/api/audit/{id}/restore` | Restores a saved configuration version |
| `GET` | `/ui/api/endpoints` | Endpoints by type |
| `GET`, `PUT`, `DELETE` | `/ui/api/endpoints/{type}/{name}` | Reads, saves or deletes an `entity.json` |
| `POST` | `/ui/api/endpoints/{type}` | Creates an endpoint |
| `POST` | `/ui/api/endpoints/{type}/validate` | Validates an endpoint without saving |
| `GET`, `POST` | `/ui/api/environments` | Lists or creates environments |
| `PUT` | `/ui/api/environments/settings` | Saves `environments/settings.json` |
| `GET`, `PUT`, `DELETE` | `/ui/api/environments/{name}` | Reads, saves or deletes an environment |
| `GET`, `PUT` | `/ui/api/environments/{name}/raw` | Environment file as JSON text |
| `POST` | `/ui/api/environments/{name}/test` | Tests the connection |
| `GET`, `PUT` | `/ui/api/settings` | Application settings; writes go to `appsettings.overrides.json` |
| `GET`, `POST` | `/ui/api/tokens` | Lists or creates tokens |
| `PUT`, `DELETE` | `/ui/api/tokens/{id}` | Updates or archives a token |
| `POST` | `/ui/api/tokens/{id}/rotate` | Rotates a token |
| `POST` | `/ui/api/tokens/{id}/unarchive` | Restores an archived token |
| `GET` | `/ui/api/tokens/{id}/audit` | Token history |
| `GET` | `/ui/api/ratelimits` | Active rate limit blocks |
| `GET`, `POST`, `DELETE` | `/ui/api/mcp/config` | MCP chat configuration |
| `GET`, `POST` | `/ui/api/users` | Console accounts |
| `PUT`, `DELETE` | `/ui/api/users/{id}` | Updates or deletes an account |
| `GET` | `/ui/api/users/me` | Signed-in account |
| `GET`, `POST` | `/ui/api/oidc/providers` | Single sign-on providers |
| `PUT`, `DELETE` | `/ui/api/oidc/providers/{id}` | Updates or deletes a provider |

## Token fields

Fields accepted by `POST /ui/api/tokens` and `PUT /ui/api/tokens/{id}`:

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

Create returns `{ "ok": true, "token": "..." }`; the token value is shown only in this response. A change that narrows the last full-access token returns `409`.

## Related topics

- [Web UI](/guide/webui)
- [Tokens](/guide/tokens)
- [Token Audit Log](/reference/token-generator)
