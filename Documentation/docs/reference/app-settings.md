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
  "Swagger": { ... },
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

### Configuration Options

- **MinimumLevel.Default**: Controls the overall logging level
- **MinimumLevel.Override**: Set specific levels for different namespaces
- **WriteTo**: Configure output destinations (Console, File, etc.)
- **File Settings**: Control log file rotation, size limits, and buffering

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

Portway automatically generates beautiful, interactive API documentation using the OpenAPI specification. The documentation is powered by Scalar, providing a modern and user-friendly interface for exploring your API endpoints.

**What you can customize:**
- **Basic Information**: Title, description, version, and contact details
- **UI Appearance**: Theme, sidebar visibility, and component display options  
- **Security**: Authentication methods and schemes
- **Behavior**: Default expansions, filtering, and validation settings

The documentation will be available at `/docs` (or your custom route) once configured.

### Full Configuration

```json
{
  "Swagger": {
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
    "RoutePrefix": "docs",
    "DocExpansion": "List",
    "DefaultModelsExpandDepth": -1,
    "DisplayRequestDuration": true,
    "EnableFilter": false,
    "EnableDeepLinking": false,
    "EnableValidator": true,
    "EnableScalar": true,
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
| `RoutePrefix` | string | `"docs"` | URL path for documentation (e.g., `/docs`) |
| `DocExpansion` | string | `"List"` | Default expansion: `"List"`, `"Full"`, `"None"` |
| `DefaultModelsExpandDepth` | integer | `-1` | Schema model expansion depth (-1 = collapsed) |
| `DisplayRequestDuration` | boolean | `true` | Show request execution time |
| `EnableFilter` | boolean | `false` | Enable endpoint search/filtering |
| `EnableDeepLinking` | boolean | `false` | Enable direct links to operations |
| `EnableValidator` | boolean | `true` | Enable request/response validation |
| `EnableScalar` | boolean | `true` | Use Scalar UI instead of default Swagger UI |
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
| `ASPNETCORE_URLS` | Listening URLs | `http://+:8080` |
| `KEYVAULT_URI` | Azure Key Vault URI | `https://vault.azure.net` |
| `PROXY_USERNAME` | Proxy authentication user | `domain\user` |
| `PROXY_PASSWORD` | Proxy authentication password | `password` |
| `PROXY_DOMAIN` | Proxy domain | `CONTOSO` |

### Configuration Priority

1. Environment variables
2. `appsettings.{Environment}.json`
3. `appsettings.json`
4. Default values

## Monitoring and Diagnostics

### Diagnostic Logging

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information",
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  }
}
```

### Request Tracing

```json
{
  "RequestTrafficLogging": {
    "Enabled": true,
    "CaptureHeaders": true,
    "EnableInfoLogging": true
  }
}
```

### Performance Monitoring

```json
{
  "Swagger": {
    "DisplayRequestDuration": true
  },
  "RequestTrafficLogging": {
    "EnableInfoLogging": true
  }
}
```

## Production Best Practices

### 1. Security Settings

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "RequestTrafficLogging": {
    "Enabled": false,
    "IncludeRequestBodies": false,
    "IncludeResponseBodies": false
  },
  "Swagger": {
    "Enabled": false
  }
}
```

### 2. Performance Settings

```json
{
  "SqlConnectionPooling": {
    "MinPoolSize": 20,
    "MaxPoolSize": 200,
    "ConnectionTimeout": 30,
    "CommandTimeout": 30,
    "Enabled": true
  },
  "RateLimiting": {
    "Enabled": true,
    "IpLimit": 300,
    "TokenLimit": 3000
  }
}
```

### 3. Error Handling

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Error",
      "Microsoft": "Error",
      "PortwayApi": "Warning"
    }
  }
}
```

## Configuration Validation

### Startup Validation

The application validates critical settings on startup:

1. Database connectivity
2. Required environment variables
3. SSL certificate availability
4. Directory permissions

### Health Checks

```http
GET /health
GET /health/details
GET /health/live
```

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
  "Swagger": {
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

- [Environment Settings](/reference/configuration/environment-settings) - Environment-specific configuration
- [Security Guide](/guide/security) - Security configuration
- [Deployment Guide](/guide/deployment/production) - Production deployment
- [Logging](/reference/tools/logging) - Logging configuration