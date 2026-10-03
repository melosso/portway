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
| `appsettings.overrides.json` | Settings saved from the console; reloaded on change |
| Environment variables | Deployment values (`Section__Key`, `PORTWAY_*`) |
| `appsettings.{ASPNETCORE_ENVIRONMENT}.json` | Per-deployment overrides |
| `appsettings.json` | Shipped defaults, with every section below |

The console never rewrites `appsettings.json`. A value in `appsettings.overrides.json` takes precedence over the same key in an environment variable.

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
| `WebUi` | Below, and [Web UI](/guide/webui) |

## WebUi

```json
{
  "WebUi": {
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
| `Enabled` | boolean | Set when `AdminApiKey` is set | Serves the console at `/ui` |
| `AdminApiKey` | string | `""` | Legacy enable switch; not a password. The first account is named `admin` when set |
| `SeedPassword` | string | `""` | Fixed password for the first account, without a forced change; demo instances only |
| `PublicOrigins` | string[] | `[]` | Origins allowed to reach `/ui` from outside the local network; one `*` per segment |
| `CorsOrigins` | string[] | `[]` | Browser origins allowed by CORS |
| `SecureCookies` | boolean | `false` | Session cookies over HTTPS only |
| `Customization.EnableLandingPage` | boolean | `true` | Landing page at `/`; `false` redirects `/` to `/docs` |
| `Customization.PromoText` | string | `""` | Markdown banner |
| `Customization.PromoLogin` | boolean | `false` | Shows the banner on `/login` |
| `Customization.LoginFooter` | string | `""` | Markdown below the sign-in form |

The first start creates an administrator account with a one-time password, written once to the log. `PublicOrigins` is matched against the client-supplied `Origin` header and does not authenticate; keep the console behind a proxy, VPN or firewall when it is not meant to be public.

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
| `WebUi:Customization:EnableLandingPage` | `true` | The landing page |

Console locations: `Oidc:Enabled` under **Settings → Security → Deployment & Access**, `OpenApi:Enabled` and `Mcp:Enabled` under **Settings → Integrations**, `Mcp:ChatEnabled` and the chat provider under **Settings → AI**, `RequestTrafficLogging:Enabled` under **Settings → Storage & Logs → Logging**, `WebUi:Customization:EnableLandingPage` under **Settings → General**.

## Environment variables

Any key is set with `__` as separator (e.g. `WebUi__PublicOrigins__0`, `Oidc__Enabled`). Portway-specific variables:

| Variable | Configuration key | Legacy name |
|---|---|---|
| `PORTWAY_ENCRYPTION_KEY` | Encryption key for secrets at rest; required outside Development | |
| `PORTWAY_CHAT_API_KEY` | AI provider key for MCP chat; takes precedence over the stored key | |
| `PORTWAY_WEBUI_ENABLED` | `WebUi:Enabled` | `WebUi__Enabled` |
| `PORTWAY_ADMIN_KEY` | `WebUi:AdminApiKey` | `WebUi__AdminApiKey` |
| `PORTWAY_SECURE_COOKIES` | `WebUi:SecureCookies` | `WebUi__SecureCookies` |
| `PORTWAY_ALLOWED_HOSTS` | `AllowedHosts` | `AllowedHosts` |
| `PORTWAY_PATH_BASE` | `PathBase` | `PathBase` |
| `PORTWAY_USE_HTTPS` | Kestrel serves HTTPS | `Use_HTTPS` |
| `PORTWAY_PROXY_USERNAME` | Outbound proxy user | `PROXY_USERNAME` |
| `PORTWAY_PROXY_PASSWORD` | Outbound proxy password | `PROXY_PASSWORD` |
| `PORTWAY_PROXY_DOMAIN` | Outbound proxy domain | `PROXY_DOMAIN` |
| `PORTWAY_KEYVAULT_URI` | Azure Key Vault URI | `KEYVAULT_URI` |

A legacy name logs a deprecation warning at startup; the `PORTWAY_` name wins when both are set.

:::warning
`PORTWAY_USE_HTTPS=true` requires a certificate for Kestrel (e.g. `Kestrel__Certificates__Default__Path`); without one, startup fails. Leave it unset behind a TLS-terminating proxy.
:::

## Related topics

- [Environment Settings](/reference/environment-settings)
- [Security](/guide/security)
- [Deployment](/guide/deployment)
- [Logging](/reference/logging)
