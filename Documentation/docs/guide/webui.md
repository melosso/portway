---
title: Web UI
description: "Browser console for endpoints, environments, tokens, users, settings and logs"
---

# Web UI

The console at `/ui` manages endpoints, environments, tokens, users and settings, and displays the dashboard and application logs. It writes the same configuration files the gateway reads. The gateway API runs without it.

## Access

```yaml
environment:
  - WebUi__Enabled=true
  - WebUi__PublicOrigins__0=https://example.com
  - PORTWAY_SECURE_COOKIES=true
```

Setting `WebUi__Enabled=true` enables the console. It is reachable from the local network only; `WebUi__PublicOrigins` allows external origins; `PORTWAY_SECURE_COOKIES` restricts cookies to HTTPS. All `WebUi` settings: [Application settings](/reference/app-settings#webui).

## Accounts

The first start creates an administrator account with a random one-time password, logged once and changed at first sign-in; its name is logged with it. `WebUi__SeedPassword` sets a fixed password for demo instances. Further accounts are created under **Users** or from the shell:

```bash
portway accounts create <username> <password>
portway accounts password <username> <new-password>
```

Shell recovery for bare-metal and Docker installs: [Recovering an account](/guide/accounts#recovering-an-account). Roles: [Account roles](/guide/accounts#account-roles). OpenID Connect sign-in: [Single sign-on](/guide/sso).

## Sessions

Session cookies are HMAC-SHA256 signed with `portway.key` (next to `auth.db`) and expire after 12 hours. Deleting `portway.key` ends all sessions.

## Settings changes

Settings saved in the console are written to `appsettings.overrides.json`, layered over `appsettings.json`. A save applies all submitted keys or none, and the response states whether a restart is required.

The section **Settings → Security → Deployment & Access** sets trusted proxies, trusted proxy networks and public console origins. A change that would exclude the current request returns `400`.

Every console change to environments, endpoints and MCP settings is recorded in the audit trail, and the previous file version is backed up. Both are listed under **Settings → Security → Recent Configuration Changes**, with restore.

## Health

`/ui/health` reports `/api` requests. Console (`/ui`), documentation and `/health` requests are excluded. Data source: `GET /ui/api/metrics/health`.

The dashboard health tiles display the last 24 hours (success rate, failures, p95, request count) and link to `/ui/health`.

### Figures

| Figure | Definition |
|---|---|
| Success rate | `(requests - 5xx responses) / requests`. Target 99.9%. |
| Failures | `5xx` responses. An unhandled exception is recorded as `500`. |
| Client errors | `4xx` responses, including `401`, `403`, `404` and `429`. Not failures. |
| p50, p90, p95, p99 | Nearest-rank latency percentiles, in milliseconds, measured from the metrics middleware to the end of the response. Targets: p50 < 100 ms, p95 < 300 ms, p99 < 1 s. |

### Filters

| Control | Query parameter | Values |
|---|---|---|
| Period | `period` | `1h`, `24h` (default), `7d`, `30d` |
| Environment | `env` | Environments recorded in the period |
| Endpoint | `endpoint` | Endpoint names recorded in the period, e.g. `CRM/Accounts` |
| Version | `version` | Versions recorded in the period, e.g. `v2`. Hidden when no versioned endpoint was called. |
| Method | `method` | HTTP methods recorded in the period |

Each filter is an exact, case-insensitive match. Filters combine with AND and apply to the figures, the latency distribution and the breakdown.

### Latency distribution

Request counts per latency bucket: ≤5, ≤10, ≤25, ≤50, ≤100, ≤250, ≤500 ms, ≤1, ≤2.5, ≤5, ≤10 s and >10 s. Bars up to the selected percentile (`p50`, `p90`, `p95`, `p99`) are highlighted; a solid line marks the percentile and a dashed line the mean.

### Breakdown

One row per environment, endpoint, version and method, with requests, success rate, failures, p50, p95 and p99. Default order: failures, then p95, both descending. Maximum 500 rows. A column header sorts by that column; a row sets the four filters to its values.

### Labels

| Label | Recorded value |
|---|---|
| Environment | The route environment when listed in `AllowedEnvironments`; otherwise empty (Unmatched). |
| Endpoint | The configured endpoint name resolved from the route; otherwise empty (Unmatched). |
| Version | The `v{n}` route segment of a versioned endpoint; empty for `v1`. |
| Method | `GET`, `POST`, `PUT`, `PATCH`, `DELETE`, `MERGE`, `QUERY`, `HEAD`, `OPTIONS`; any other method is `OTHER`. |

### Retention and clearing

Retention: 31 days in `metrics.db`; 31 days and at most 500,000 requests in memory, reloaded from `metrics.db` on start. Each instance keeps its own data. **Purge** (administrator role, `DELETE /ui/api/metrics`) deletes every request recorded before the purge from memory and `metrics.db`, which also resets the dashboard traffic, error and top endpoint cards. The purge is recorded in the audit trail. **Clear filters** resets the environment, endpoint, version and method filters, including those set by selecting a breakdown row.

## API

The console API under `/ui/api` uses the `portway_auth` session cookie and a CSRF header on writes: [Console API](/reference/console-api).
