---
title: Console
description: "Browser console for endpoints, environments, tokens, users, settings and logs"
---

# Console

The console at `/_` manages endpoints, environments, tokens, users and settings, and displays the dashboard and application logs. It writes the same configuration files the gateway reads. The gateway API runs without it.

## Access

```yaml
environment:
  - PORTWAY_PUBLIC_ORIGINS=https://example.com
  - PORTWAY_SECURE_COOKIES=true
```

The console is enabled by default and accepts client addresses from loopback and `allowedHosts` in `environments/network-access-policy.json`. If `allowedHosts` has two or fewer entries, the IP addresses of the host are added. `PORTWAY_WEBUI_ENABLED=false` turns it off. `PORTWAY_PUBLIC_ORIGINS` (comma-separated) allows external origins. `PORTWAY_SECURE_COOKIES` restricts cookies to HTTPS. All `Console` settings: [Application settings](/reference/app-settings#console).

## Accounts

Accounts are created under **Users** or from the shell. First account, roles, sessions and recovery: [Accounts](/guide/accounts). OpenID Connect sign-in: [Single sign-on](/guide/sso).

## Settings changes

Settings saved in the console are written to `appsettings.overrides.json`. Values in that file override `appsettings.json`. Environment variables override that file. A key set by an environment variable is read-only in the console and displays the variable name. A save applies all submitted keys or none, and the response states whether a restart is required. Editable keys and validation rules: [Console editing](/reference/app-settings#console-editing).

The section **Settings → Security → Deployment & Access** sets trusted proxies, trusted proxy networks, public console origins, CORS origins, allowed hosts and the path base. A change that would exclude the current request returns `400`.

Secrets (Redis connection strings, the outbound proxy password) are write-only. The console displays whether a value is set. **Clear** removes a value on save.

Every console change to environments, endpoints and MCP settings is recorded in the audit trail, and the previous file version is backed up. Both are listed under **Settings → Security → Recent Configuration Changes**, with restore.

## Health

`/_/health` reports `/api` requests. Console (`/_`), documentation and `/health` requests are excluded. Data source: `GET /_/api/metrics/health`.

The dashboard health tiles display the last 24 hours (success rate, failures, p95, request count) and link to `/_/health`.

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

Retention: 31 days in `metrics.db`; 31 days and at most 500,000 requests in memory, reloaded from `metrics.db` on start. Each instance keeps its own data. **Purge** (administrator role, `DELETE /_/api/metrics`) deletes every request recorded before the purge from memory and `metrics.db`, which also resets the dashboard traffic, error and top endpoint cards. The purge is recorded in the audit trail. **Clear filters** resets the environment, endpoint, version and method filters.

## Trace

**Trace** (`/_/trace`) contains the [request traffic logging](/reference/audit) settings:

| Card | Settings |
|---|---|
| Traffic Logging | Enabled, storage provider and its path, file rotation, queue, batch, flush interval, headers, summary line |
| Body Capture | Request bodies, response bodies, size limit |
| Log Levels | `Serilog:MinimumLevel:Default` and per-source overrides, one `Source=Level` per line |

Turning on body capture requires confirmation. Bodies are logged unfiltered. Traffic logging changes apply after a restart.

## Logs

**Logs** displays entries from the three newest `log/portwayapi-*.log` files, newest first. The level filter (`INF`, `WRN`, `ERR`, `DBG`) and page size (100, 200, 500, 1000) apply to all three files. File settings: [Logging](/reference/logging).

## API

The console API under `/_/api` uses the `portway_auth` session cookie and a CSRF header on writes: [Console API](/reference/console-api).
