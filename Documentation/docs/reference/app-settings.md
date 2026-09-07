---
title: Application Settings
description: "When you want to shape how Portway behaves at its core (logging, security, rate limiting, and service wiring), appsettings.json is where you'll spend your time"
---

# Application Settings

When you want to shape how Portway behaves at its core (logging, security, rate limiting, and service wiring), `appsettings.json` is where you'll spend your time. This page walks through each section, explains what it controls, and shows how you can override values per deployment without touching the base file.

## Configuration files

| File | Purpose | Priority |
|------|---------|----------|
| `appsettings.json` | Base configuration | Lowest |
| `appsettings.Development.json` | Development overrides | Medium |
| `appsettings.Production.json` | Production overrides | Highest |
| Environment variables | Runtime overrides | Highest |

## Core configuration structure

```json
{
  "AllowedHosts": "*",
  "PathBase": "",
  "WebUi": { ... },
  "OpenApi": { ... },
  "RateLimiting": { ... },
  "ForwardedHeaders": { ... },
  "RequestTrafficLogging": { ... },
  "SqlConnectionPooling": { ... },
  "Caching": { ... },
  "FileStorage": { ... },
  "Telemetry": { ... },
  "Mcp": { ... },
  "EndpointReloading": { ... },
  "Serilog": { ... }
}
```

Each section is described below.

## Logging configuration

Portway uses Serilog for structured logging with configurable sinks and filtering.

### Basic structure

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.EntityFrameworkCore.Database.Command": "Warning",
        "System": "Warning",
        "Microsoft.AspNetCore": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console"
      },
      {
        "Name": "File",
        "Args": {
          "path": "log/portwayapi-.log",
          "rollingInterval": "Day",
          "fileSizeLimitBytes": 10485760,
          "rollOnFileSizeLimit": true,
          "retainedFileCountLimit": 10,
          "buffered": true,
          "flushToDiskInterval": "00:00:30"
        }
      }
    ]
  }
}
```

Serilog replaces the standard .NET logging factory here, so the `Serilog` section is the only one that shapes output. A `Logging` section, if you add one, is read by nothing.

### Changing log levels

To change the logging level, modify the `Default` value in `appsettings.json`:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug"  // Change to Debug, Information, Warning, Error, or Fatal
    }
  }
}
```

Serilog owns the effective level, so this is the setting that changes what reaches the console and the log files. What each level covers, along with rotation, retention and structured output, is described in [Logging](/reference/logging).

## OpenAPI configuration

The `OpenApi` section controls the generated API documentation and the Scalar UI that renders it.

```json
{
  "OpenApi": {
    "Enabled": true,
    "Title": "Portway: API Gateway",
    "Version": "v1"
  }
}
```

Every property, including the contact block, security definition and the full set of Scalar display options, is listed in [OpenAPI settings](/reference/openapi-settings).

## Rate limiting configuration

The `RateLimiting` section sets the per-IP and per-token request budgets.

```json
{
  "RateLimiting": {
    "Enabled": true,
    "IpLimit": 100,
    "IpWindow": 60,
    "TokenLimit": 1000,
    "TokenWindow": 60,
    "Store": "Memory"
  }
}
```

For the property reference, per-token overrides, Redis-backed buckets and tuning advice, see [Rate limiting](/guide/rate-limiting).

## Forwarded headers

When Portway runs directly on Kestrel, the client IP it sees is the one connecting to it, which is exactly what per-IP rate limiting and the Web UI network gate rely on. Once you place a reverse proxy in front (nginx, IIS, or similar), that connecting IP becomes the proxy instead, and every client starts to look like the same address. The real client IP is still available in the `X-Forwarded-For` header, and this section is how you tell Portway which proxies it can trust to set it.

### Configuration structure

```json
{
  "ForwardedHeaders": {
    "KnownProxies": ["127.0.0.1", "::1"],
    "KnownNetworks": ["10.0.0.0/8"]
  }
}
```

### Property reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `KnownProxies` | string[] | `[]` | IP addresses of trusted reverse proxies |
| `KnownNetworks` | string[] | `[]` | CIDR ranges of trusted reverse proxies |

### How it works

Portway only honors `X-Forwarded-For` when the request arrives from an address you have listed here, which keeps clients from spoofing their own IP. You can register individual proxy addresses in `KnownProxies`, describe a whole range in `KnownNetworks`, or combine both when your setup calls for it. A common starting point for a proxy sharing the host with Portway is `127.0.0.1` and `::1`.

Leaving both lists empty is perfectly valid, and it is the default. In that case `X-Forwarded-For` is ignored entirely and the connecting address is used as-is. That is the safe choice when nothing sits in front of Portway, though behind a proxy it means per-IP rate limiting, the console sign-in lockout, and the network-based Web UI gate will all see the proxy rather than the real caller. If you rely on any of those, registering your proxy here is recommended.

The Web UI shows which of the two applies. Open **Settings → Security** and read the **Client addresses** row: it gives the client address Portway saw for your request, and warns when a forwarded address arrived with no proxy trusted to send it. Add the proxy from **Deployment & Access** on the same page.

::: Note
If you front Portway with Cloudflare, its client IP is recovered separately from the `CF-Connecting-IP` header when the request genuinely originates from a Cloudflare address, so you do not need to list Cloudflare ranges here.
:::

The Settings posture panel in the Web UI reflects whether any trusted proxies are configured, which is a quick way to confirm the setup took effect.

## Request traffic logging

The `RequestTrafficLogging` section records every proxied request to file or SQLite. It stays off until you enable it.

```json
{
  "RequestTrafficLogging": {
    "Enabled": false,
    "StorageType": "file"
  }
}
```

The full property reference, the stored record shape and retention behaviour live in [Audit and traffic logging](/reference/audit). Response bodies are captured here too: set `IncludeResponseBodies`, and cap what gets stored with `MaxBodyCaptureSizeBytes`.

## SQL connection pooling

### Configuration structure

```json
{
  "SqlConnectionPooling": {
    "ApplicationName": "Portway API - Remote integration gateway",
    "MinPoolSize": 5,
    "MaxPoolSize": 100,
    "ConnectionTimeout": 15,
    "CommandTimeout": 30,
    "Enabled": true
  }
}
```

### Property reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ApplicationName` | string | - | Application identifier |
| `MinPoolSize` | integer | `5` | Minimum pool connections |
| `MaxPoolSize` | integer | `100` | Maximum pool connections |
| `ConnectionTimeout` | integer | `15` | Connection timeout (seconds) |
| `CommandTimeout` | integer | `30` | Command timeout (seconds) |
| `Enabled` | boolean | `true` | Enable connection pooling |

## Caching configuration

The `Caching` section controls response caching for proxy and SQL endpoints, backed by memory or Redis.

```json
{
  "Caching": {
    "Enabled": true,
    "DefaultCacheDurationSeconds": 300,
    "ProviderType": "Memory"
  }
}
```

The property reference, the Redis block and per-endpoint duration overrides are documented in [Caching](/reference/caching).

## File storage configuration

Controls how Portway stores and serves files for `File`-type endpoints.

### Configuration structure

```json
{
  "FileStorage": {
    "StorageDirectory": "storage/files",
    "MaxFileSizeBytes": 52428800,
    "UseMemoryCache": true,
    "MemoryCacheTimeSeconds": 60,
    "MaxTotalMemoryCacheMB": 200,
    "BlockedExtensions": [".exe", ".dll", ".bat", ".sh", "..."]
  }
}
```

### Property reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `StorageDirectory` | string | `"storage/files"` | Directory where uploaded files are stored |
| `MaxFileSizeBytes` | integer | `52428800` | Maximum upload size (default 50 MB) |
| `UseMemoryCache` | boolean | `true` | Cache frequently accessed files in memory |
| `MemoryCacheTimeSeconds` | integer | `60` | How long a file stays in the memory cache |
| `MaxTotalMemoryCacheMB` | integer | `200` | Total memory budget for the file cache |
| `BlockedExtensions` | array | `[".exe", ".dll", ...]` | File extensions that are refused on upload |

:::info
The default block list covers executable, script, and macro-enabled office formats. Extend it to suit your security policy; shrink it only with caution.
:::

## Endpoint reloading configuration

Controls hot-reload behaviour when endpoint JSON files change on disk.

### Configuration structure

```json
{
  "EndpointReloading": {
    "Enabled": true,
    "DebounceMs": 2000
  }
}
```

### Property reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | boolean | `true` | Watch endpoint files and reload on change without restart |
| `DebounceMs` | integer | `2000` | Minimum milliseconds between reloads for the same file (prevents double-fires from editors) |

## Telemetry

Controls how request traces and metrics leave the gateway. Telemetry is off by default; selecting a provider activates it.

| Field | Required | Type | Description |
|---|---|---|---|
| `Provider` | No | string | `"None"`, `"Otlp"`, or `"Prometheus"`. Defaults to `"None"` |
| `ServiceName` | No | string | Service name reported to the backend. Defaults to `Portway.Api` |
| `ResourceAttributes` | No | string | Comma-separated `key=value` pairs attached as resource attributes |
| `Otlp:Endpoint` | No | string | gRPC endpoint of your OTLP collector. Defaults to `http://localhost:4317` |
| `Prometheus:Path` | No | string | Route the scrape endpoint is served on. Defaults to `/metrics` |

```json
"Telemetry": {
  "Provider": "Otlp",
  "ServiceName": "portway-prod",
  "ResourceAttributes": "deployment.environment=production,host.name=gw01",
  "Otlp": {
    "Endpoint": "http://otel-collector.internal:4317"
  },
  "Prometheus": {
    "Path": "/metrics"
  }
}
```

The `Otlp` provider pushes traces and metrics to a collector over gRPC; the `Prometheus` provider serves metrics on a scrape endpoint instead. Configurations from earlier releases keep working: a flat `"Enabled": true` selects the OTLP provider, and a flat `OtlpEndpoint` is used whenever `Otlp:Endpoint` is not set. See the [Telemetry guide](/guide/opentelemetry) for the full walkthrough.

## MCP configuration

The `Mcp` section exposes endpoints as Model Context Protocol tools.

```json
{
  "Mcp": {
    "Enabled": false,
    "Path": "/mcp",
    "RequireAuthentication": true
  }
}
```

The property reference, per-endpoint exposure and the built-in tools are covered in the [MCP server guide](/guide/mcp).

## General settings

### AllowedHosts

```json
{
  "AllowedHosts": "*"
}
```

Configure which hosts can access the application:
- `"*"` - Allow all hosts
- `"example.com"` - Allow specific domain
- `"*.example.com"` - Allow subdomains
- `"example.com;api.example.com"` - Multiple hosts

### PathBase

```json
{
  "PathBase": ""
}
```

Base path for the application (e.g., `/api`).

## Web UI configuration

The built-in admin interface settings.

```json
{
  "WebUi": {
    "AdminApiKey": "your-secure-password",
    "PublicOrigins": ["https://example.com"],
    "SecureCookies": true,
    "Customization": {
      "EnableLandingPage": true
    }
  }
}
```

### Property reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AdminApiKey` | string | `""` | Seeds the first console account on a fresh install. Not used for sign-in afterwards |
| `PublicOrigins` | array | `[]` | Origins allowed to reach `/ui` from outside the local network. Not a CORS setting: see `CorsOrigins` for that |
| `SecureCookies` | boolean | `false` | Require HTTPS for auth cookies |
| `Customization.EnableLandingPage` | boolean | `true` | Show the landing page at `/` for local/allowed clients. Set to `false` to redirect all root requests straight to `/docs` (useful for production systems where the UI should not be discoverable). |
| `Customization.PromoText` | string | `""` | Markdown banner shown at the top of the login page |
| `Customization.PromoLogin` | boolean | `false` | Allow the promo-bar to be shown at `/login` |
| `Customization.LoginFooter` | string | `""` | Markdown text shown below the login form |

### Security

- Portway asks for a sign-in as soon as one console account exists. `AdminApiKey` only creates that first account, so you can clear it once you can sign in
- Without `PublicOrigins`, only local network IPs can access the UI
- `PublicOrigins` is matched against the request's `Origin` header, which any client can set. It widens who can reach the sign-in page; it does not authenticate anyone. Keep the console behind your proxy, VPN, or firewall when it should not be public
- Cookie auth uses HMAC-SHA256 signing
- Set `Customization.EnableLandingPage` to `false` on internet-facing or production deployments to prevent the admin UI from being surfaced at the root path

### Customization example

```json
{
  "WebUi": {
    "Customization": {
      "PromoText": "Welcome to **Portway**. Check the [documentation](https://github.com/melosso/portway) to get started.",
      "LoginFooter": "No account? Contact your [administrator](mailto:admin@example.com)."
    }
  }
}
```

Both fields support standard Markdown (bold, links, inline code).

## Environment-Specific configuration

### Development settings

`appsettings.Development.json`:
```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug",
      "Override": {
        "Microsoft.EntityFrameworkCore.Database.Command": "Information"
      }
    }
  },
  "RequestTrafficLogging": {
    "Enabled": true,
    "IncludeRequestBodies": true,
    "IncludeResponseBodies": true
  }
}
```

### Production settings

`appsettings.Production.json`:
```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Warning",
      "Override": {
        "PortwayApi": "Information"
      }
    }
  },
  "RateLimiting": {
    "IpLimit": 200,
    "TokenLimit": 1000
  },
  "AllowedHosts": "api.company.com"
}
```

## Security settings

### Host filtering and CORS

These are two separate controls, and it helps to keep them apart.

`AllowedHosts` filters on the `Host` header of incoming requests. The default `"*"` accepts any host, and you can narrow it to the names you serve:

```json
{
  "AllowedHosts": "api.company.com;app.company.com"
}
```

Browser cross-origin access is governed by `WebUi:CorsOrigins` instead, which is an explicit allowlist of origins. [Web UI settings](/reference/webui) covers it along with `PublicOrigins`, which controls who may reach the admin interface from outside the local network.

### Turning features off

Each of these turns off one subsystem and leaves its configuration in place:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Oidc:Enabled` | boolean | `true` | Every single sign-on provider at once. Off hides them all on the sign-in page and refuses the start and callback routes, including providers whose own flag is set |
| `OpenApi:Enabled` | boolean | `true` | The specification and the reference at `/docs` |
| `Mcp:Enabled` | boolean | `true` | The MCP server |
| `RequestTrafficLogging:Enabled` | boolean | `false` | Recording of proxied requests |
| `WebUi:Customization:EnableLandingPage` | boolean | `true` | The public page at the root path |

Portway reads `Oidc:Enabled` on each request, so a change applies without a restart and the provider records stay as they are. [Single sign-on](/guide/sso) covers the providers.

Set any of these from **Settings → Security → Feature Toggles** in the console, or in `appsettings.json`.

## Environment variables

### Common variables

| Variable | Description | Example |
|----------|-------------|---------|
| `PORTWAY_ENCRYPTION_KEY` | Encryption secret | (Hardcoded) |
| `PORTWAY_CHAT_API_KEY` | AI provider API key for Chat. Takes precedence over the encrypted database entry when set. | `sk-ant-...` |
| `Use_HTTPS` | Whether Kestrel serves HTTPS directly (see note) | `false` |
| `KEYVAULT_URI` | Azure Key Vault URI | `https://vault.azure.net` |
| `PROXY_USERNAME` | Proxy authentication user | `domain\user` |
| `PROXY_PASSWORD` | Proxy authentication password | `password` |
| `PROXY_DOMAIN` | Proxy domain | `CONTOSO` |
| `AllowedHosts` | Allowed host names | `*` |
| `PathBase` | Base path | `/api` |
| `Mcp__ChatEnabled` | Override `Mcp:ChatEnabled` at runtime | `true` |
| `WebUi__AdminApiKey` | Seeds the first console account | `secret` |
| `WebUi__PublicOrigins__0` | Origin allowed to reach `/ui` (array) | `https://example.com` |
| `Oidc__Enabled` | Turn single sign-on off | `false` |
| `ForwardedHeaders__KnownProxies__0` | Trusted reverse proxy (array) | `127.0.0.1` |
| `WebUi__SecureCookies` | Secure cookies | `true` |
| `WebUi__Customization__EnableLandingPage` | Show landing page at root | `false` |

:::warning
**`Use_HTTPS=true` requires a TLS certificate reachable by Kestrel.** Without one the container fails immediately at startup with `BackgroundService failed / Hosting failed to start`. In Docker deployments where an external reverse proxy (nginx, Caddy, Cloudflare Tunnel, etc.) handles SSL termination, leave this unset or set it to `false`. Only enable it when Portway is directly internet-facing **and** a certificate is supplied (e.g. via `Kestrel__Certificates__Default__Path`).
:::

### Configuration priority

1. Environment variables
2. `appsettings.{Environment}.json`
3. `appsettings.json`
4. Default values

## Troubleshooting configuration

### Configuration debugging

1. Enable detailed logging. Serilog sets the effective level, so lower `Serilog:MinimumLevel` rather than the `Logging` section:
```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug",
      "Override": {
        "Microsoft": "Information"
      }
    }
  }
}
```

2. Check environment variable:

::: code-group

```powershell [PowerShell]
$env:ASPNETCORE_ENVIRONMENT
```

```bash [Bash]
echo $ASPNETCORE_ENVIRONMENT
```

:::

3. Review startup logs for configuration issues

## A complete file to start from

The `appsettings.json` that ships with Portway is the working reference: it carries every section above with its default value, so you can read it as the canonical example. Rather than replacing it wholesale, keep it as your base and put deployment-specific values in `appsettings.Production.json`, as shown in [Environment-Specific configuration](#environment-specific-configuration).

## Related topics

- [Environment Settings](/reference/environment-settings)
- [Security Guide](/guide/security)
- [Deployment Guide](/guide/deployment)
- [Logging](/reference/logging)
