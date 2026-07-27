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
  "LogSettings": { ... },
  "SqlConnectionPooling": { ... },
  "Caching": { ... },
  "FileStorage": { ... },
  "Telemetry": { ... },
  "Mcp": { ... },
  "EndpointReloading": { ... },
  "Serilog": { ... },
  "Logging": { ... }
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
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

### Log levels

| Level | Description | Use Case |
|-------|-------------|----------|
| `Debug` | Debugging information | Development troubleshooting |
| `Information` | General flow of events | Normal operations |
| `Warning` | Abnormal or unexpected events | Potential issues |
| `Error` | Error events | Application errors |
| `Fatal` | Critical failures | System failures |

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

Leaving both lists empty is perfectly valid, and it is the default. In that case `X-Forwarded-For` is ignored entirely and the connecting address is used as-is. That is the safe choice when nothing sits in front of Portway, though behind a proxy it means per-IP rate limiting and the network-based Web UI gate will see the proxy rather than the real caller. If you rely on either of those, registering your proxy here is recommended.

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

The full property reference, the stored record shape and retention behaviour live in [Audit and traffic logging](/reference/audit).

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
    "DebounceMs": 2000,
    "LogLevel": "Information"
  }
}
```

### Property reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | boolean | `true` | Watch endpoint files and reload on change without restart |
| `DebounceMs` | integer | `2000` | Minimum milliseconds between reloads for the same file (prevents double-fires from editors) |
| `LogLevel` | string | `"Information"` | Log level for reload events (`"Debug"`, `"Information"`, `"Warning"`) |

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

## Log settings

### Configuration structure

```json
{
  "LogSettings": {
    "LogResponseToFile": false
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `LogResponseToFile` | boolean | `false` | Write raw response bodies to the log file (useful for debugging; disable in production) |

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
| `AdminApiKey` | string | `""` | Password for web UI login (empty = disabled) |
| `PublicOrigins` | array | `[]` | Allowed CORS origins for external access |
| `SecureCookies` | boolean | `false` | Require HTTPS for auth cookies |
| `Customization.EnableLandingPage` | boolean | `true` | Show the landing page at `/` for local/allowed clients. Set to `false` to redirect all root requests straight to `/docs` (useful for production systems where the UI should not be discoverable). |
| `Customization.PromoText` | string | `""` | Markdown banner shown at the top of the login page |
| `Customization.PromoLogin` | boolean | `false` | Allow the promo-bar to be shown at `/login` |
| `Customization.LoginFooter` | string | `""` | Markdown text shown below the login form |

### Security

- Without `AdminApiKey`, the web UI is disabled
- Without `PublicOrigins`, only local network IPs can access the UI
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
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
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
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "PortwayApi": "Information"
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

### CORS configuration

CORS is configured to allow all origins in the default configuration:
```json
{
  "AllowedHosts": "*"
}
```

For production, restrict to specific domains:
```json
{
  "AllowedHosts": "api.company.com;app.company.com"
}
```

## Performance tuning

### Connection pool optimization

```json
{
  "SqlConnectionPooling": {
    "MinPoolSize": 10,
    "MaxPoolSize": 200,
    "ConnectionTimeout": 30,
    "CommandTimeout": 60,
    "Enabled": true
  }
}
```

### Rate limiting for high traffic

```json
{
  "RateLimiting": {
    "Enabled": true,
    "IpLimit": 500,
    "IpWindow": 60,
    "TokenLimit": 5000,
    "TokenWindow": 60
  }
}
```

### Traffic logging for debugging

```json
{
  "RequestTrafficLogging": {
    "Enabled": true,
    "StorageType": "sqlite",
    "IncludeRequestBodies": true,
    "IncludeResponseBodies": true,
    "MaxBodyCaptureSizeBytes": 8192
  }
}
```

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
| `WebUi__AdminApiKey` | Web UI password | `secret` |
| `WebUi__PublicOrigins__0` | CORS origin (array) | `https://example.com` |
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

### Common issues

1. **Application Won't Start**
   - Check JSON syntax in appsettings files
   - Verify required environment variables
   - Review startup logs

2. **Database Connection Failures**
   - Verify connection strings
   - Check SQL Server availability
   - Review firewall settings

3. **Rate Limiting Too Restrictive**
   - Adjust IpLimit and TokenLimit
   - Increase time windows
   - Monitor traffic patterns

4. **Logging Not Working**
   - Check log file permissions
   - Verify log directory exists
   - Review LogLevel settings

### Configuration debugging

1. Enable detailed logging:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Information"
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

## Complete example configuration

### Production appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  },
  "AllowedHosts": "api.company.com",
  "OpenApi": {
    "Enabled": true,
    "BaseProtocol": "https",
    "Title": "Company API Gateway",
    "Version": "v1",
    "Description": "Production API Gateway",
    "Contact": {
      "Name": "API Support",
      "Email": "api-support@company.com"
    },
    "SecurityDefinition": {
      "Name": "Bearer",
      "Description": "Enter 'Bearer' [space] and then your token",
      "In": "Header",
      "Type": "ApiKey",
      "Scheme": "Bearer"
    }
  },
  "RateLimiting": {
    "Enabled": true,
    "IpLimit": 200,
    "IpWindow": 60,
    "TokenLimit": 2000,
    "TokenWindow": 60
  },
  "RequestTrafficLogging": {
    "Enabled": false,
    "StorageType": "sqlite",
    "SqlitePath": "log/traffic.db",
    "CaptureHeaders": true,
    "IncludeRequestBodies": false,
    "IncludeResponseBodies": false
  },
  "SqlConnectionPooling": {
    "ApplicationName": "Company API Gateway",
    "MinPoolSize": 10,
    "MaxPoolSize": 150,
    "ConnectionTimeout": 30,
    "CommandTimeout": 30,
    "Enabled": true
  }
}
```

## Related topics

- [Environment Settings](/reference/environment-settings) - Environment-specific configuration
- [Security Guide](/guide/security) - Security configuration
- [Deployment Guide](/guide/deployment) - Production deployment
- [Logging](/reference/logging) - Logging configuration
