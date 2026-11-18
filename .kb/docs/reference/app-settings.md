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

The console never rewrites `appsettings.json`. An environment variable takes precedence over the same key in `appsettings.overrides.json`. The console displays a key set by an environment variable as read-only, with the variable name. `PUT /_/api/settings` returns `400` for that key.

## Console editing

Every key on this page is editable under **Settings** or **Trace** in the console, except the keys below:

| Key | Reason |
|---|---|
| `Serilog:WriteTo` | Sink list keyed by position |
| `Hosting:ApiPort`, `Hosting:ConsolePort`, `Kestrel` | Listener ports. A wrong value makes the gateway unreachable |
| `OpenApi:SecurityDefinition:Name`, `In`, `Type`, `Scheme` | Bearer is the accepted token scheme |

Validation rules for console writes:

| Value | Rule |
|---|---|
| Paths (`FileStorage:StorageDirectory`, `RequestTrafficLogging:LogDirectory`, `RequestTrafficLogging:SqlitePath`) | Relative to the Portway folder. Outside `endpoints`, `environments`, `tokens`, `wwwroot`, `.core` and `.backups`. File storage shares no folder with log files |
| Routes (`Mcp:Path`, `Telemetry:Prometheus:Path`) | Lower-case path outside `/api`, `/_`, `/docs`, `/health` and `/openapi`. The two routes differ |
| Secrets (`Caching:Redis:ConnectionString`, `RateLimiting:Redis:ConnectionString`, `Proxy:Password`) | Write-only. Stored encrypted with the deployment key. The console displays whether a value is set |
| `Console:SeedPassword`, `Console:SeedToken`, `Console:AdminApiKey` | Removable, never set from the console |
| `AllowedHosts`, `Console:PublicOrigins`, `ForwardedHeaders:*` | Refused when the change excludes the current console session |

A setting saved from the console replaces anything configured in your `appsettings.json` file.

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
| `Console` | Below, and [Console](/guide/webui) |

## Console

```json
{
  "Console": {
    "Enabled": true,
    "PublicOrigins": [],
    "CorsOrigins": ["https://app.example.com"],
    "SecureCookies": true,
    "Customization": {
      "LandingPageEnabled": true,
      "PromoText": "Welcome to **Portway**.",
      "LoginFooter": "No account? Contact your [administrator](mailto:admin@example.com)."
    }
  }
}
```

| Property | Type | Default | Description |
|---|---|---|---|
| `Enabled` | boolean | `true` | Serves the console at `/_` |
| `AdminApiKey` | string | `""` | Deprecated enable switch; not a password. The first account is named `admin` when set |
| `SeedPassword` | string | `""` | Fixed password for the first account, without a forced change. For demo instances |
| `SeedToken` | string | `""` | Fixed full-access token created on first start when no token exists; 32 or more characters. For demo instances |
| `PublicOrigins` | string[] | `[]` | Origins allowed to reach `/_` from outside the local network; one `*` per segment |
| `CorsOrigins` | string[] | `[]` | Browser origins allowed by CORS |
| `SecureCookies` | boolean | `false` | Restricts session cookies to HTTPS |
| `Customization.LandingPageEnabled` | boolean | `true` | Landing page at `/`; `false` redirects `/` to `/docs` |
| `Customization.PromoText` | string | `""` | Markdown banner |
| `Customization.PromoLogin` | boolean | `false` | Shows the banner on `/login` |
| `Customization.LoginFooter` | string | `""` | Markdown below the sign-in form |

`PublicOrigins` is matched against the client-supplied `Origin` header and does not authenticate. A console that is not public requires a proxy, VPN or firewall rule in front of Portway.

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

Portway reads `X-Forwarded-For` from a proxy in `KnownProxies` or `KnownNetworks` and ignores it from other addresses. If both are empty: the connecting address is the client address. With a reverse proxy, per-IP rate limiting, the sign-in lockout and the console network gate then apply to the proxy address. **Settings → Security → Client addresses** shows the address Portway resolved and warns about an untrusted forwarded address. Cloudflare client addresses are restored from `CF-Connecting-IP` for requests from Cloudflare ranges, without an entry here.

## SqlConnectionPooling

```json
{
  "SqlConnectionPooling": {
    "Enabled": true,
    "ApplicationName": "Portway API - Remote integration gateway",
    "MinPoolSize": 5,
    "MaxPoolSize": 100,
    "ConnectionTimeoutSeconds": 15,
    "CommandTimeoutSeconds": 30
  }
}
```

| Property | Type | Default | Description |
|---|---|---|---|
| `Enabled` | boolean | `true` | Connection pooling |
| `ApplicationName` | string | | Application name sent to the database |
| `MinPoolSize` | integer | `5` | Minimum pooled connections |
| `MaxPoolSize` | integer | `100` | Maximum pooled connections |
| `ConnectionTimeoutSeconds` | integer | `15` | Connect timeout in seconds |
| `CommandTimeoutSeconds` | integer | `30` | Query timeout in seconds |

## FileStorage

```json
{
  "FileStorage": {
    "StorageDirectory": "storage/files",
    "MaxFileSizeBytes": 52428800,
    "UseMemoryCache": true,
    "MemoryCacheDurationSeconds": 60,
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
| `MemoryCacheDurationSeconds` | integer | `60` | Cache entry lifetime |
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
| `FreePageRatioThreshold` | number | `0.25` | `VACUUM` runs when free pages reach this fraction of the file |
| `RunOnStartup` | boolean | `false` | Adds one run shortly after start |
| `Backup.Enabled` | boolean | `true` | Writes a snapshot archive at the same time |
| `Backup.Retention` | integer | `7` | Number of archives retained, 1 to 100 |

A database that fails `quick_check` is skipped for `ANALYZE` and `VACUUM` and logged as an error. **Settings → Maintenance** displays the result per database. **Run now** starts a run.

A snapshot is a `.zip` in `.backups/snapshots/` with `auth.db`, `mcp.db`, `environments/` and `endpoints/`. The databases are copied with `VACUUM INTO` and are consistent while Portway runs. `metrics.db` and `tokens/` are not included. **Settings → Backups** creates, displays, downloads and restores snapshots. Downloading and restoring require the administrator role and are recorded in the audit trail. `Backup.Enabled` is independent of `Enabled`.

A snapshot contains password hashes, token hashes and connection strings encrypted with `PORTWAY_ENCRYPTION_KEY`. The same key is required to read the connection strings after a restore.

**Restore** extracts a snapshot into `.backups/restore-pending/`. The next start applies it before any database opens: the current `auth.db`, `mcp.db`, `environments/` and `endpoints/` are copied to `.backups/pre-restore-{time}/`, then replaced by the snapshot content. Items missing from the snapshot are not changed. **Cancel** removes the pending restore. A snapshot entry outside `auth.db`, `mcp.db`, `environments/` and `endpoints/` is refused.

## Host and path

| Property | Default | Description |
|---|---|---|
| `AllowedHosts` | `*` | Accepted `Host` headers, separated by `;` (e.g. `api.example.com;*.example.com`) |
| `PathBase` | `""` | Path prefix when hosted under a sub-path (e.g. `/portway`) |
| `Hosting:ApiPort` | `8080` | API listener port |
| `Hosting:ConsolePort` | `8081` | Console listener port |

The API port returns `404` for `/_/*` and `/index.html`, and redirects `/` to `/docs`. The console port serves every path. If both ports are equal: one listener serves the API and the console. Portway ignores `urls`, `ASPNETCORE_URLS` and `ASPNETCORE_HTTP_PORTS`. On IIS, the site binding sets the port and both keys are ignored.

## Feature switches

| Property | Default | Turns off |
|---|---|---|
| `Oidc:Enabled` | `true` | All single sign-on providers; read per request |
| `OpenApi:Enabled` | `true` | The OpenAPI document and `/docs` |
| `Mcp:Enabled` | `true` in `appsettings.json` | The MCP server |
| `RequestTrafficLogging:Enabled` | `false` | Traffic logging |
| `Console:Customization:LandingPageEnabled` | `true` | The landing page |

Console locations: `Oidc:Enabled` under **Settings → Security → Deployment & Access**, `OpenApi:Enabled` under **Settings → OpenAPI**, `Mcp:Enabled`, `Mcp:ChatEnabled` and the chat provider under **Settings → AI**, `RequestTrafficLogging:Enabled` under **Trace**, `Console:Customization:LandingPageEnabled` under **Settings → General**.

## Environment variables

Any key is set with `__` as separator (e.g. `RateLimiting__IpLimit`). Portway-specific variables; list values are comma-separated:

| Variable | Configuration key | Legacy name |
|---|---|---|
| `PORTWAY_ENCRYPTION_KEY` | Encryption key for secrets at rest; required outside Development |  |
| `PORTWAY_MCP_CHAT_API_KEY` | AI provider key for MCP chat; takes precedence over the stored key | `PORTWAY_CHAT_API_KEY` |
| `PORTWAY_CONSOLE_ENABLED` | `Console:Enabled` | `PORTWAY_WEBUI_ENABLED`, `Console__Enabled` |
| `PORTWAY_ADMIN_KEY` | `Console:AdminApiKey` (deprecated, no effect while `Console:Enabled` is set) | `Console__AdminApiKey` |
| `PORTWAY_SECURE_COOKIES` | `Console:SecureCookies` | `Console__SecureCookies` |
| `PORTWAY_PUBLIC_ORIGINS` | `Console:PublicOrigins` (list) | `Console__PublicOrigins__0` |
| `PORTWAY_SEED_PASSWORD` | `Console:SeedPassword` | `Console__SeedPassword` |
| `PORTWAY_SEED_TOKEN` | `Console:SeedToken` | `Console__SeedToken` |
| `PORTWAY_PROMO_TEXT` | `Console:Customization:PromoText` | `Console__Customization__PromoText` |
| `PORTWAY_PROMO_LOGIN_ENABLED` | `Console:Customization:PromoLogin` | `PORTWAY_PROMO_LOGIN`, `Console__Customization__PromoLogin` |
| `PORTWAY_LOGIN_FOOTER` | `Console:Customization:LoginFooter` | `Console__Customization__LoginFooter` |
| `PORTWAY_OPENAPI_MARKDOWN_ENABLED` | `OpenApi:ServeMarkdown` | `PORTWAY_OPENAPI_MARKDOWN`, `OpenApi__ServeMarkdown` |
| `PORTWAY_OPENAPI_DESCRIPTION` | `OpenApi:Description` | `OpenApi__Description` |
| `PORTWAY_KNOWN_PROXIES` | `ForwardedHeaders:KnownProxies` (list) | `ForwardedHeaders__KnownProxies__0` |
| `PORTWAY_KNOWN_NETWORKS` | `ForwardedHeaders:KnownNetworks` (list) | `ForwardedHeaders__KnownNetworks__0` |
| `PORTWAY_OIDC_ENABLED` | `Oidc:Enabled` | `Oidc__Enabled` |
| `PORTWAY_MCP_ENABLED` | `Mcp:Enabled` | `Mcp__Enabled` |
| `PORTWAY_MCP_CHAT_ENABLED` | `Mcp:ChatEnabled` | `PORTWAY_CHAT_ENABLED`, `Mcp__ChatEnabled` |
| `PORTWAY_TELEMETRY_PROVIDER` | `Telemetry:Provider` | `Telemetry__Provider` |
| `PORTWAY_TELEMETRY_OTLP_ENDPOINT` | `Telemetry:Otlp:Endpoint` | `PORTWAY_OTLP_ENDPOINT`, `Telemetry__Otlp__Endpoint` |
| `PORTWAY_TELEMETRY_SERVICE_NAME` | `Telemetry:ServiceName` | `PORTWAY_SERVICE_NAME`, `Telemetry__ServiceName` |
| `PORTWAY_ALLOWED_HOSTS` | `AllowedHosts` | `AllowedHosts` |
| `PORTWAY_PATH_BASE` | `PathBase` | `PathBase` |
| `PORTWAY_PORT_API` | `Hosting:ApiPort` | `Hosting__ApiPort` |
| `PORTWAY_PORT_CONSOLE` | `Hosting:ConsolePort` | `Hosting__ConsolePort` |
| `PORTWAY_HTTPS_ENABLED` | Kestrel serves HTTPS | `PORTWAY_USE_HTTPS`, `Use_HTTPS` |
| `PORTWAY_PROXY_USERNAME` | Outbound proxy user | `PROXY_USERNAME` |
| `PORTWAY_PROXY_PASSWORD` | Outbound proxy password | `PROXY_PASSWORD` |
| `PORTWAY_PROXY_DOMAIN` | Outbound proxy domain | `PROXY_DOMAIN` |
| `PORTWAY_KEY_VAULT_URI` | Azure Key Vault URI | `PORTWAY_KEYVAULT_URI`, `KEYVAULT_URI` |

NTLM authentication at an outbound corporate proxy requires all three `PORTWAY_PROXY_*` variables. `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` and `AZURE_CLIENT_SECRET` authenticate to Azure Key Vault ([Azure Key Vault](/reference/secrets#azure-key-vault)).

A legacy name logs a deprecation warning at startup; the `PORTWAY_` name wins when both are set. For a list, set either the `PORTWAY_` name or the indexed legacy names. Indexed entries beyond the list length remain in the configuration. `WebUi__*` names are not read: [Removed keys](#removed-keys).

:::warning
`PORTWAY_HTTPS_ENABLED=true` requires a certificate for Kestrel (e.g. `Kestrel__Certificates__Default__Path`); without one, startup fails. Leave it unset when a TLS-terminating proxy serves HTTPS.
:::

## Removed keys

Portway does not read these names. Startup logs one error per name found, with the source file and the current name.

| Former name | Current name |
|---|---|
| `WebUi` section | `Console` section |
| `RateLimiting:IpWindow` | `RateLimiting:IpWindowSeconds` |
| `RateLimiting:TokenWindow` | `RateLimiting:TokenWindowSeconds` |
| `RateLimiting:Store` | `RateLimiting:Provider` |
| `RateLimiting:RedisConnectionString` | `RateLimiting:Redis:ConnectionString` |
| `SqlConnectionPooling:ConnectionTimeout` | `SqlConnectionPooling:ConnectionTimeoutSeconds` |
| `SqlConnectionPooling:CommandTimeout` | `SqlConnectionPooling:CommandTimeoutSeconds` |
| `Caching:ProviderType` | `Caching:Provider` |
| `FileStorage:MemoryCacheTimeSeconds` | `FileStorage:MemoryCacheDurationSeconds` |
| `RequestTrafficLogging:StorageType` | `RequestTrafficLogging:Provider` |
| `RequestTrafficLogging:IncludeRequestBodies` | `RequestTrafficLogging:CaptureRequestBodies` |
| `RequestTrafficLogging:IncludeResponseBodies` | `RequestTrafficLogging:CaptureResponseBodies` |
| `RequestTrafficLogging:EnableInfoLogging` | `RequestTrafficLogging:InfoLoggingEnabled` |
| `Console:Customization:EnableLandingPage` | `Console:Customization:LandingPageEnabled` |
| `OpenApi:ScalarTheme` | `OpenApi:Scalar:Theme` |
| `OpenApi:ScalarLayout` | `OpenApi:Scalar:Layout` |
| `OpenApi:ScalarShowSidebar` | `OpenApi:Scalar:ShowSidebar` |
| `OpenApi:ScalarHideDownloadButton` | `OpenApi:Scalar:HideDownloadButton` |
| `OpenApi:ScalarHideModels` | `OpenApi:Scalar:HideModels` |
| `OpenApi:ScalarHideClientButton` | `OpenApi:Scalar:HideClientButton` |
| `OpenApi:ScalarHideTestRequestButton` | `OpenApi:Scalar:HideTestRequestButton` |
| `Mcp:RequireAuthentication` | None. Delete the key |
| `Mcp:AppsEnabled` | None. Delete the key |

## Related topics

- [Environment Settings](/reference/environment-settings)
- [Security](/guide/security)
- [Getting Started](/guide/getting-started)
- [Logging](/reference/logging)
