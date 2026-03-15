# Application Settings

Application settings control the core behavior of Portway, including logging, security, rate limiting, and service configuration. These settings are defined in `appsettings.json` and can be overridden for different environments.

## Configuration Files

| File | Purpose | Priority |
|------|---------|----------|
| `appsettings.json` | Base configuration | Lowest |
| `appsettings.Development.json` | Development overrides | Medium |
| `appsettings.Production.json` | Production overrides | Highest |
| Environment variables | Runtime overrides | Highest |

## Core Configuration Structure

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "PathBase": "",
  "WebUi": {
    "AdminApiKey": "",
    "PublicOrigins": [],
    "SecureCookies": false
  },
  "OpenApi": { ... },
  "RateLimiting": { ... },
  "RequestTrafficLogging": { ... },
  "SqlConnectionPooling": { ... }
}
```

## Logging Configuration

Portway uses Serilog for structured logging with configurable sinks and filtering.

### Basic Structure

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

### Log Levels

| Level | Description | Use Case |
|-------|-------------|----------|
| `Debug` | Debugging information | Development troubleshooting |
| `Information` | General flow of events | Normal operations |
| `Warning` | Abnormal or unexpected events | Potential issues |
| `Error` | Error events | Application errors |
| `Fatal` | Critical failures | System failures |

### Changing Log Levels

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

## OpenAPI Configuration

Portway generates OpenAPI documentation from your configured endpoints and exposes it through the Scalar UI at `/docs`.

### Full Configuration

```json
{
  "OpenApi": {
    "Enabled": true,
    "BaseProtocol": "https",
    "Title": "Portway: API Gateway",
    "Version": "v1",
    "Description": "This is Portway. A lightweight API gateway designed to integrate your platforms with your Windows environment. It provides a simple, fast and efficient way to connect various data sources and services.",
    "Contact": {
      "Name": "Jay Doe (Demo Company)",
      "Email": "support@democompany.local"
    },
    "Footer": {
      "Text": "Powered by Scalar",
      "Target": "_blank",
      "Url": "#"
    },
    "SecurityDefinition": {
      "Name": "Bearer",
      "Description": "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
      "In": "Header",
      "Type": "ApiKey",
      "Scheme": "Bearer"
    },
    "ScalarTheme": "default",
    "ScalarLayout": "modern",
    "ScalarShowSidebar": true,
    "ScalarHideDownloadButton": true,
    "ScalarHideModels": true,
    "ScalarHideClientButton": true,
    "ScalarHideTestRequestButton": false
  }
}
```

### Property Reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | boolean | `true` | Enable/disable API documentation |
| `BaseProtocol` | string | `"https"` | Protocol for API base URLs |
| `Title` | string | - | Main title displayed in documentation |
| `Version` | string | `"v1"` | API version identifier |
| `Description` | string | - | Main description (supports markdown) |
| `Contact` | object | - | Contact information for API support |
| `Footer` | object | - | Footer text shown in Scalar |
| `SecurityDefinition` | object | - | Authentication method configuration |
| `ScalarTheme` | string | `"default"` | Scalar theme: `"default"`, `"alternate"`, `"moon"`, `"purple"`, `"solarized"` |
| `ScalarLayout` | string | `"modern"` | Scalar layout: `"modern"`, `"classic"` |
| `ScalarShowSidebar` | boolean | `true` | Display navigation sidebar |
| `ScalarHideDownloadButton` | boolean | `true` | Hide OpenAPI spec download button |
| `ScalarHideModels` | boolean | `true` | Hide data models section |
| `ScalarHideClientButton` | boolean | `true` | Hide client generation button |
| `ScalarHideTestRequestButton` | boolean | `false` | Hide test request button |

## Rate Limiting Configuration

### Configuration Structure

```json
{
  "RateLimiting": {
    "Enabled": true,
    "IpLimit": 100,
    "IpWindow": 60,
    "TokenLimit": 100,
    "TokenWindow": 60
  }
}
```

### Property Reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | boolean | `true` | Enable rate limiting |
| `IpLimit` | integer | `100` | Requests per IP |
| `IpWindow` | integer | `60` | Time window in seconds |
| `TokenLimit` | integer | `100` | Requests per token |
| `TokenWindow` | integer | `60` | Time window in seconds |

### Rate Limiting Behavior

- IP-based limiting applies to all requests
- Token-based limiting applies per authentication token
- Exceeding limits results in 429 Too Many Requests
- Limits reset after the time window expires

## Request Traffic Logging

### Full Configuration

```json
{
  "RequestTrafficLogging": {
    "Enabled": false,
    "QueueCapacity": 10000,
    "StorageType": "file",
    "SqlitePath": "log/traffic_logs.db",
    "LogDirectory": "log/traffic",
    "MaxFileSizeMB": 50,
    "MaxFileCount": 5,
    "FilePrefix": "proxy_traffic_",
    "BatchSize": 100,
    "FlushIntervalMs": 1000,
    "IncludeRequestBodies": false,
    "IncludeResponseBodies": false,
    "MaxBodyCaptureSizeBytes": 4096,
    "CaptureHeaders": true,
    "EnableInfoLogging": true
  }
}
```

### Property Reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | boolean | `false` | Enable traffic logging |
| `QueueCapacity` | integer | `10000` | Log queue size |
| `StorageType` | string | `"file"` | Storage type: "file" or "sqlite" |
| `SqlitePath` | string | `"log/traffic_logs.db"` | SQLite database path |
| `LogDirectory` | string | `"log/traffic"` | Log file directory |
| `MaxFileSizeMB` | integer | `50` | Maximum log file size |
| `MaxFileCount` | integer | `5` | Maximum log files |
| `FilePrefix` | string | `"proxy_traffic_"` | Log file prefix |
| `BatchSize` | integer | `100` | Batch write size |
| `FlushIntervalMs` | integer | `1000` | Flush interval (ms) |
| `IncludeRequestBodies` | boolean | `false` | Log request bodies |
| `IncludeResponseBodies` | boolean | `false` | Log response bodies |
| `MaxBodyCaptureSizeBytes` | integer | `4096` | Max body size to log |
| `CaptureHeaders` | boolean | `true` | Log request headers |
| `EnableInfoLogging` | boolean | `true` | Enable info-level logs |

## SQL Connection Pooling

### Configuration Structure

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

### Property Reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ApplicationName` | string | - | Application identifier |
| `MinPoolSize` | integer | `5` | Minimum pool connections |
| `MaxPoolSize` | integer | `100` | Maximum pool connections |
| `ConnectionTimeout` | integer | `15` | Connection timeout (seconds) |
| `CommandTimeout` | integer | `30` | Command timeout (seconds) |
| `Enabled` | boolean | `true` | Enable connection pooling |

## Caching Configuration

Portway caches proxy and SQL responses in memory or Redis to reduce upstream load and improve response times.

### Configuration Structure

```json
{
  "Caching": {
    "Enabled": true,
    "DefaultCacheDurationSeconds": 300,
    "ProviderType": "Memory",
    "MemoryCacheMaxItems": 10000,
    "MemoryCacheSizeLimitMB": 100,
    "CacheableContentTypes": [
      "application/json",
      "text/json"
    ],
    "EndpointCacheDurations": {
      "Products": 600,
      "Customers": 300
    },
    "Redis": {
      "ConnectionString": "localhost:6379",
      "InstanceName": "Portway:",
      "Database": 0,
      "UseSsl": false,
      "ConnectTimeoutMs": 5000,
      "AbortOnConnectFail": false,
      "FallbackToMemoryCache": true,
      "MaxRetryAttempts": 3,
      "RetryDelayMs": 200
    }
  }
}
```

### Property Reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | boolean | `true` | Enable response caching |
| `DefaultCacheDurationSeconds` | integer | `300` | Default TTL for cached responses |
| `ProviderType` | string | `"Memory"` | Cache backend: `"Memory"` or `"Redis"` |
| `MemoryCacheMaxItems` | integer | `10000` | Maximum number of items in the memory cache |
| `MemoryCacheSizeLimitMB` | integer | `100` | Memory cap for the cache in MB |
| `CacheableContentTypes` | array | `["application/json", ...]` | Only responses with these content types are cached |
| `EndpointCacheDurations` | object | `{}` | Per-endpoint TTL overrides keyed by endpoint name |

### Redis Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ConnectionString` | string | `"localhost:6379"` | Redis connection string |
| `InstanceName` | string | `"Portway:"` | Key prefix to namespace cache entries |
| `Database` | integer | `0` | Redis logical database index |
| `UseSsl` | boolean | `false` | Use TLS for the Redis connection |
| `ConnectTimeoutMs` | integer | `5000` | Connection timeout in milliseconds |
| `AbortOnConnectFail` | boolean | `false` | Throw on connection failure instead of retrying |
| `FallbackToMemoryCache` | boolean | `true` | Fall back to in-process memory cache if Redis is unavailable |
| `MaxRetryAttempts` | integer | `3` | Retry attempts on transient Redis errors |
| `RetryDelayMs` | integer | `200` | Delay between retry attempts in milliseconds |

## File Storage Configuration

Controls how Portway stores and serves files for `File`-type endpoints.

### Configuration Structure

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

### Property Reference

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

## Endpoint Reloading Configuration

Controls hot-reload behaviour when endpoint JSON files change on disk.

### Configuration Structure

```json
{
  "EndpointReloading": {
    "Enabled": true,
    "DebounceMs": 2000,
    "LogLevel": "Information"
  }
}
```

### Property Reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | boolean | `true` | Watch endpoint files and reload on change without restart |
| `DebounceMs` | integer | `2000` | Minimum milliseconds between reloads for the same file (prevents double-fires from editors) |
| `LogLevel` | string | `"Information"` | Log level for reload events (`"Debug"`, `"Information"`, `"Warning"`) |

## Log Settings

### Configuration Structure

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

## General Settings

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

## Web UI Configuration

The built-in admin interface settings.

```json
{
  "WebUi": {
    "AdminApiKey": "your-secure-password",
    "PublicOrigins": ["https://example.com"],
    "SecureCookies": true,
    "EnableLandingPage": true
  }
}
```

### Property Reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AdminApiKey` | string | `""` | Password for web UI login (empty = disabled) |
| `PublicOrigins` | array | `[]` | Allowed CORS origins for external access |
| `SecureCookies` | boolean | `false` | Require HTTPS for auth cookies |
| `EnableLandingPage` | boolean | `true` | Show the landing page at `/` for local/allowed clients. Set to `false` to redirect all root requests straight to `/docs` (useful for production systems where the UI should not be discoverable). |
| `Customization.PromoText` | string | `""` | Markdown banner shown at the top of the login page |
| `Customization.PromoLogin` | boolean | `false` | Allow the promo-bar to be shown at `/login` |
| `Customization.LoginFooter` | string | `""` | Markdown text shown below the login form |

### Security

- Without `AdminApiKey`, the web UI is disabled
- Without `PublicOrigins`, only local network IPs can access the UI
- Cookie auth uses HMAC-SHA256 signing
- Set `EnableLandingPage: false` on internet-facing or production deployments to prevent the admin UI from being surfaced at the root path

### Customization Example

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

## Environment-Specific Configuration

### Development Settings

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

### Production Settings

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

## Security Settings

### CORS Configuration

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

## Performance Tuning

### Connection Pool Optimization

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

### Rate Limiting for High Traffic

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

### Traffic Logging for Debugging

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

## Environment Variables

### Common Variables

| Variable | Description | Example |
|----------|-------------|---------|
| `PORTWAY_ENCRYPTION_KEY` | Encryption secret | (Hardcoded) |
| `Use_HTTPS` | Whether Kestrel serves HTTPS directly (see note) | `false` |
| `KEYVAULT_URI` | Azure Key Vault URI | `https://vault.azure.net` |
| `PROXY_USERNAME` | Proxy authentication user | `domain\user` |
| `PROXY_PASSWORD` | Proxy authentication password | `password` |
| `PROXY_DOMAIN` | Proxy domain | `CONTOSO` |
| `AllowedHosts` | Allowed host names | `*` |
| `PathBase` | Base path | `/api` |
| `WebUi__AdminApiKey` | Web UI password | `secret` |
| `WebUi__PublicOrigins__0` | CORS origin (array) | `https://example.com` |
| `WebUi__SecureCookies` | Secure cookies | `true` |
| `WebUi__EnableLandingPage` | Show landing page at root | `false` |

:::warning
**`Use_HTTPS=true` requires a TLS certificate reachable by Kestrel.** Without one the container fails immediately at startup with `BackgroundService failed / Hosting failed to start`. In Docker deployments where an external reverse proxy (nginx, Caddy, Cloudflare Tunnel, etc.) handles SSL termination, leave this unset or set it to `false`. Only enable it when Portway is directly internet-facing **and** a certificate is supplied (e.g. via `Kestrel__Certificates__Default__Path`).
:::

### Configuration Priority

1. Environment variables
2. `appsettings.{Environment}.json`
3. `appsettings.json`
4. Default values

## Troubleshooting Configuration

### Common Issues

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

### Configuration Debugging

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
```powershell
echo %ASPNETCORE_ENVIRONMENT%
```

3. Review startup logs for configuration issues

## Complete Example Configuration

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

## Related Topics

- [Environment Settings](/reference/environment-settings) - Environment-specific configuration
- [Security Guide](/guide/security) - Security configuration
- [Deployment Guide](/guide/deployment) - Production deployment
- [Logging](/reference/logging) - Logging configuration