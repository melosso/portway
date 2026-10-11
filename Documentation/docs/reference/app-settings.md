---
title: Application Settings
description: "appsettings.json sections, environment variables and configuration priority"
---

# Application Settings

Gateway-wide settings are read from `appsettings.json`. Environments and endpoints are configured separately: [Environment Settings](/reference/environment-settings), [Entity Configuration](/reference/entity-config).

## Configuration sources

From highest to lowest priority:

| Source | Content |
|---|---|
| `PORTWAY_*` environment variables | Deployment values, see [Environment variables](#environment-variables) |
| Command-line arguments | `--Section:Key=value` |
| Environment variables | Deployment values (`Section__Key`) |
| `appsettings.overrides.json` | Settings saved from the console; reloaded on change |
| `appsettings.{ASPNETCORE_ENVIRONMENT}.json` | Per-deployment overrides |
| `appsettings.json` | Shipped defaults, with every section below |

The console never rewrites `appsettings.json`. An environment variable takes precedence over the same key in `appsettings.overrides.json`. The console displays a key set by an environment variable as read-only, with the variable name. `PUT /ui/api/settings` returns `400` for that key.

## Sections

| Section | Reference |
|---|---|
| `Serilog` | [Logging](/reference/logging) |
| `OpenApi` | [OpenAPI Settings](/reference/openapi-settings) |
| `RateLimiting` | [Rate Limiting](/guide/rate-limiting) |
| `RequestTrafficLogging` | [Auditing](/reference/audit) |
| `Caching` | [Caching](/reference/caching) |
| `Telemetry` | [Telemetry](/guide/telemetry) |
| `Mcp` | [MCP Server](/guide/mcp) |
| `Oidc` | [Single Sign-On](/guide/sso) |
| `Console` | Below, and [Web UI](/guide/webui) |

## Console

```json
{
  "Console": {
    "Enabled": true,
    "PublicOrigins": [],
    "CorsOrigins": ["https://app.example.com"],
    "SecureCookies": true,
    "Customization": {
      "EnableLandingPage": true,
      "PromoText": "Welcome to **Portway**.",
      "LoginFooter": "No account? Contact your [administrator](mailto:admin@example.com)."
    }
  }
}
```

| Property | Type | Default | Description |
|---|---|---|---|
| `Enabled` | boolean | `true` | Serves the console at `/ui` |
| `AdminApiKey` | string | `""` | Deprecated enable switch; not a password. The first account is named `admin` when set |
| `SeedPassword` | string | `""` | Fixed password for the first account, without a forced change; demo instances only |
| `PublicOrigins` | string[] | `[]` | Origins allowed to reach `/ui` from outside the local network; one `*` per segment |
| `CorsOrigins` | string[] | `[]` | Browser origins allowed by CORS |
| `SecureCookies` | boolean | `false` | Session cookies over HTTPS only |
| `Customization.EnableLandingPage` | boolean | `true` | Landing page at `/`; `false` redirects `/` to `/docs` |
| `Customization.PromoText` | string | `""` | Markdown banner |
| `Customization.PromoLogin` | boolean | `false` | Shows the banner on `/login` |
| `Customization.LoginFooter` | string | `""` | Markdown below the sign-in form |

`WebUi` is the deprecated name of this section and is still read, with a warning at startup. The source with the highest priority wins, whichever name it uses. Within one file, `Console` takes precedence over `WebUi`. Console saves are written under `Console`.

`PublicOrigins` is matched against the client-supplied `Origin` header and does not authenticate; keep the console behind a proxy, VPN or firewall when it is not meant to be public.

## ForwardedHeaders

```json
{
  "ForwardedHeaders": {
    "KnownProxies": ["127.0.0.1", "::1"],
    "KnownNetworks": ["10.0.0.0/8"]
  }
}
```

| Property | Type | Default | Description |
|---|---|---|---|
| `KnownProxies` | string[] | `[]` | Trusted proxy addresses |
| `KnownNetworks` | string[] | `[]` | Trusted proxy ranges (CIDR) |

Portway honors `X-Forwarded-For` only from a listed proxy. With both lists empty, the connecting address is the client address; behind a proxy, per-IP rate limiting, the sign-in lockout and the console network gate then apply to the proxy address. **Settings → Security → Client addresses** shows the address Portway resolved and warns about an untrusted forwarded address. Cloudflare client addresses are restored from `CF-Connecting-IP` for requests from Cloudflare ranges, without an entry here.

## SqlConnectionPooling

```json
{
  "SqlConnectionPooling": {
    "Enabled": true,
    "ApplicationName": "Portway API - Remote integration gateway",
    "MinPoolSize": 5,
    "MaxPoolSize": 100,
    "ConnectionTimeout": 15,
    "CommandTimeout": 30
  }
}
```

| Property | Type | Default | Description |
|---|---|---|---|
| `Enabled` | boolean | `true` | Connection pooling |
| `ApplicationName` | string | | Application name sent to the database |
| `MinPoolSize` | integer | `5` | Minimum pooled connections |
| `MaxPoolSize` | integer | `100` | Maximum pooled connections |
| `ConnectionTimeout` | integer | `15` | Connect timeout in seconds |
| `CommandTimeout` | integer | `30` | Query timeout in seconds |

## FileStorage

```json
{
  "FileStorage": {
    "StorageDirectory": "storage/files",
    "MaxFileSizeBytes": 52428800,
    "UseMemoryCache": true,
    "MemoryCacheTimeSeconds": 60,
    "MaxTotalMemoryCacheMB": 200,
    "BlockedExtensions": [".exe", ".dll", ".bat", ".sh"]
  }
}
```

| Property | Type | Default | Description |
|---|---|---|---|
| `StorageDirectory` | string | `storage/files` | Root for relative `BaseDirectory` values |
| `MaxFileSizeBytes` | integer | `52428800` | Maximum upload size (50 MB) |
| `UseMemoryCache` | boolean | `true` | Read cache for recently used files |
| `MemoryCacheTimeSeconds` | integer | `60` | Cache entry lifetime |
| `MaxTotalMemoryCacheMB` | integer | `200` | Cache size limit |
| `BlockedExtensions` | string[] | Executables, scripts, server pages, macro-enabled Office formats | Extensions refused on upload |

## EndpointReloading

```json
{
  "EndpointReloading": {
    "Enabled": true,
    "DebounceMs": 2000
  }
}
```

| Property | Type | Default | Description |
|---|---|---|---|
| `Enabled` | boolean | `true` | Reloads endpoints when files under `endpoints/` change |
| `DebounceMs` | integer | `2000` | Delay before a reload |

## DatabaseMaintenance

```json
{
  "DatabaseMaintenance": {
    "Enabled": true,
    "Schedule": "03:00",
    "Backup": {
      "Enabled": true,
      "Retention": 7
    }
  }
}
```

| Property | Type | Default | Description |
|---|---|---|---|
| `Enabled` | boolean | `true` | Runs `PRAGMA quick_check`, `ANALYZE` and, for a bloated file, `VACUUM` on `auth.db`, `mcp.db`, `metrics.db` and the traffic log database |
| `Schedule` | string | `03:00` | Local time of the daily run, `HH:mm` |
| `Backup.Enabled` | boolean | `true` | Writes a snapshot archive at the same time |
| `Backup.Retention` | integer | `7` | Number of archives kept, 1 to 100 |

A database that fails `quick_check` is skipped for `ANALYZE` and `VACUUM` and logged as an error. **Settings → Maintenance** displays the result per database. **Run now** starts a run.

A snapshot is a `.zip` in `.backups/snapshots/` with `auth.db`, `mcp.db`, `environments/` and `endpoints/`. The databases are copied with `VACUUM INTO` and are consistent while Portway runs. `metrics.db` and `tokens/` are not included. **Settings → Backups** creates, lists, downloads and restores snapshots. Downloading and restoring require the administrator role and are recorded in the audit trail. `Backup.Enabled` is independent of `Enabled`.

A snapshot contains password hashes, token hashes and connection strings encrypted with `PORTWAY_ENCRYPTION_KEY`. The same key is required to read the connection strings after a restore.

**Restore** extracts a snapshot into `.backups/restore-pending/`. The next start applies it before any database opens: the current `auth.db`, `mcp.db`, `environments/` and `endpoints/` are copied to `.backups/pre-restore-{time}/`, then replaced by the snapshot content. Items missing from the snapshot are not changed. **Cancel** removes the pending restore. A snapshot entry outside `auth.db`, `mcp.db`, `environments/` and `endpoints/` is refused.

## Host and path

| Property | Default | Description |
|---|---|---|
| `AllowedHosts` | `*` | Accepted `Host` headers, separated by `;` (e.g. `api.example.com;*.example.com`) |
| `PathBase` | `""` | Path prefix when hosted under a sub-path (e.g. `/portway`) |

## Feature switches

| Property | Default | Turns off |
|---|---|---|
| `Oidc:Enabled` | `true` | All single sign-on providers; read per request |
| `OpenApi:Enabled` | `true` | The OpenAPI document and `/docs` |
| `Mcp:Enabled` | `true` in `appsettings.json` | The MCP server |
| `RequestTrafficLogging:Enabled` | `false` | Traffic logging |
| `Console:Customization:EnableLandingPage` | `true` | The landing page |

Console locations: `Oidc:Enabled` under **Settings → Security → Deployment & Access**, `OpenApi:Enabled` under **Settings → OpenAPI**, `Mcp:Enabled`, `Mcp:ChatEnabled` and the chat provider under **Settings → AI**, `RequestTrafficLogging:Enabled` under **Settings → System → Logging**, `Console:Customization:EnableLandingPage` under **Settings → General**.

## Environment variables

Any key is set with `__` as separator (e.g. `RateLimiting__IpLimit`). Portway-specific variables; list values are comma-separated:

| Variable | Configuration key | Legacy name |
|---|---|---|
| `PORTWAY_ENCRYPTION_KEY` | Encryption key for secrets at rest; required outside Development | |
| `PORTWAY_CHAT_API_KEY` | AI provider key for MCP chat; takes precedence over the stored key | |
| `PORTWAY_WEBUI_ENABLED` | `Console:Enabled` | `WebUi__Enabled` |
| `PORTWAY_ADMIN_KEY` | `Console:AdminApiKey` | `WebUi__AdminApiKey` |
| `PORTWAY_SECURE_COOKIES` | `Console:SecureCookies` | `WebUi__SecureCookies` |
| `PORTWAY_OPENAPI_MARKDOWN` | `OpenApi:MarkdownEnabled` | `OpenApi__MarkdownEnabled` |
| `PORTWAY_PUBLIC_ORIGINS` | `Console:PublicOrigins` (list) | `WebUi__PublicOrigins__0` |
| `PORTWAY_SEED_PASSWORD` | `Console:SeedPassword` | `WebUi__SeedPassword` |
| `PORTWAY_PROMO_TEXT` | `Console:Customization:PromoText` | `WebUi__Customization__PromoText` |
| `PORTWAY_LOGIN_FOOTER` | `Console:Customization:LoginFooter` | `WebUi__Customization__LoginFooter` |
| `PORTWAY_KNOWN_PROXIES` | `ForwardedHeaders:KnownProxies` (list) | `ForwardedHeaders__KnownProxies__0` |
| `PORTWAY_KNOWN_NETWORKS` | `ForwardedHeaders:KnownNetworks` (list) | `ForwardedHeaders__KnownNetworks__0` |
| `PORTWAY_OIDC_ENABLED` | `Oidc:Enabled` | `Oidc__Enabled` |
| `PORTWAY_MCP_ENABLED` | `Mcp:Enabled` | `Mcp__Enabled` |
| `PORTWAY_CHAT_ENABLED` | `Mcp:ChatEnabled` | `Mcp__ChatEnabled` |
| `PORTWAY_TELEMETRY_PROVIDER` | `Telemetry:Provider` | `Telemetry__Provider` |
| `PORTWAY_OTLP_ENDPOINT` | `Telemetry:Otlp:Endpoint` | `Telemetry__Otlp__Endpoint` |
| `PORTWAY_SERVICE_NAME` | `Telemetry:ServiceName` | `Telemetry__ServiceName` |
| `PORTWAY_ALLOWED_HOSTS` | `AllowedHosts` | `AllowedHosts` |
| `PORTWAY_PATH_BASE` | `PathBase` | `PathBase` |
| `PORTWAY_USE_HTTPS` | Kestrel serves HTTPS | `Use_HTTPS` |
| `PORTWAY_PROXY_USERNAME` | Outbound proxy user | `PROXY_USERNAME` |
| `PORTWAY_PROXY_PASSWORD` | Outbound proxy password | `PROXY_PASSWORD` |
| `PORTWAY_PROXY_DOMAIN` | Outbound proxy domain | `PROXY_DOMAIN` |
| `PORTWAY_KEYVAULT_URI` | Azure Key Vault URI | `KEYVAULT_URI` |

A legacy name logs a deprecation warning at startup; the `PORTWAY_` name wins when both are set. For a list, set either the `PORTWAY_` name or the indexed legacy names: indexed entries beyond the list length are kept.

:::warning
`PORTWAY_USE_HTTPS=true` requires a certificate for Kestrel (e.g. `Kestrel__Certificates__Default__Path`); without one, startup fails. Leave it unset behind a TLS-terminating proxy.
:::

## Related topics

- [Environment Settings](/reference/environment-settings)
- [Security](/guide/security)
- [Deployment](/guide/deployment)
- [Logging](/reference/logging)
