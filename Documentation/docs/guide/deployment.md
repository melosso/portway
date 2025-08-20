# Deploying

This guide explains how to deploy the Portway API on a Windows Server using Internet Information Services (IIS), which is currently the only officially documented deployment method. 

**Note:** If you're running the API using Docker (Compose) and would like to contribute, you're welcome to start a discussion on GitHub.

## Prerequisites

Before you begin, ensure you have:
- Windows Server with IIS installed
- Administrator access
- [ASP.NET Core 9.0 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/9.0)

> [!WARNING]
> There's a slight difference between the x64-installer and the Hosting Bundle that ASP.NET Core 9.0 provides. Make sure to select the latter.

> [!IMPORTANT]
> This guide assumes you have basic knowledge of IIS configuration and data source connectivity. While we cover the essential steps, some details may require your existing expertise.

## Pre-deployment Checklist

### 1. Verify System Requirements
- [ ] IIS is installed and running
- [ ] Administrator access is available
- [ ] SQL Server is accessible (if using SQL endpoints)
- [ ] Firewall rules allow necessary ports
- [ ] Valid TLS/SSL (self-signed) certificate

### 2. Deployment Package
Ensure your deployment package contains:
```
/Deployment/PortwayApi/
├── web.config
├── appsettings.json
├── endpoints/
│   ├── SQL/
│   ├── Proxy/
│   └── Webhooks/
├── environments/
│   └── settings.json
├── wwwroot/
│   └── index.html
└── [various other files]
```

## Installation

### Step 1: Install ASP.NET Core Runtime
1. Download and install the ASP.NET Core 9.0 Hosting Bundle
2. Restart IIS to activate the changes:
   ```cmd
   iisreset
   ```

### Step 2: Deploy Application Files
1. Create the application directory:
   ```cmd
   mkdir C:\path\to\your\PortwayApi
   ```

2. Copy all files from `/Deployment/PortwayApi/` to the new directory

### Step 3: Configure IIS Website
1. Open IIS Manager
2. Right-click Sites → Add Website
3. Configure with these settings:
   - **Site name**: PortwayApi
   - **Physical path**: C:\path\to\your\PortwayApi
   - **Port**: 443 (or your preferred port)
4. Click OK

### Step 4: Configure Application Pool
1. Navigate to Application Pools in IIS Manager
2. Select the "PortwayApi" pool
3. Configure Basic Settings:
   - **.NET CLR version**: No Managed Code
   - **Pipeline mode**: Integrated
4. Configure Advanced Settings:
   - **Start Mode**: AlwaysRunning
   - **Idle Time-out**: 0
   - **Identity**: ApplicationPoolIdentity (or service account)

::: tip
If you're planning to use the **Proxy** to connect to any internal webservice, you may have to rely on NTLM-authentication (e.g. for [Exact Globe+](https://www.exact.com/nl/software/exact-globe)). You'll have to bind the Identity of the Application Pool to an internal (domain) user instead. This user has to have the necessary permissions to connect to the internal services.
:::

### Step 5: Set Directory Permissions
Grant the application pool identity required permissions:
```cmd
icacls "C:\path\to\your\PortwayApi" /grant "IIS AppPool\PortwayApi:(OI)(CI)M" /T
```

## Configuration

### 1. Initial Startup
Start the website in IIS. On first run, Portway will automatically:
- Create required directories (`tokens`, `log`)
- Generate an `auth.db` database
- Create the first access token

### 2. Retrieve Access Token
1. Navigate to `C:\path\to\your\PortwayApi\tokens`
2. Open `[SERVERNAME].txt`
3. Note the authentication token for API access. 

::: warning
After the first run, the token will be stored as a text file. Make sure to store this token savely and **remove** this file permanently. 
:::

### 3. Configure Environments
1. Navigate to `C:\path\to\your\PortwayApi\environments`
2. Edit `settings.json` to define allowed environments:
   ```json
   {
     "Environment": {
       "ServerName": "YOUR_SERVER",
       "AllowedEnvironments": ["prod", "dev","test"]
     }
   }
   ```

3. Create environment-specific configurations:
   ```
   environments/
   ├── prod/
   │   └── settings.json
   └── dev/
   │   └── settings.json
   └── test/
       └── settings.json
   ```

   Example environment configuration:
   ::: code-group

   ```json [Production]
   {
     "ServerName": "YOUR_SQL_SERVER",
     "ConnectionString": "Server=YOUR_SQL_SERVER;Database=prod;Trusted_Connection=True;TrustServerCertificate=true;",
     "Headers": {
       "DatabaseName": "prod",
       "ServerName": "YOUR_APP_SERVER"
     }
   }
   ```
    
   ```json [Development]
   {
     "ServerName": "YOUR_SQL_SERVER",
     "ConnectionString": "Server=YOUR_SQL_SERVER;Database=dev;Trusted_Connection=True;TrustServerCertificate=true;",
     "Headers": {
       "DatabaseName": "dev",
       "ServerName": "YOUR_APP_SERVER"
     }
   }
   ```

    ```json [Testing]
   {
     "ServerName": "YOUR_SQL_SERVER",
     "ConnectionString": "Server=YOUR_SQL_SERVER;Database=test;Trusted_Connection=True;TrustServerCertificate=true;",
     "Headers": {
       "DatabaseName": "test",
       "ServerName": "YOUR_APP_SERVER"
     }
   }

   :::
## Verification

### 1. Test Basic Functionality
Open a browser and navigate to:
- `https://localhost/docs` - API documentation interface
- `https://localhost/health/live` - Basic health check

### 2. Verify Authentication
Test API authentication using PowerShell:
```powershell
$token = Get-Content "C:\path\to\your\PortwayApi\tokens\[SERVERNAME].txt" | ConvertFrom-Json | Select-Object -ExpandProperty Token
Invoke-RestMethod -Uri "https://localhost/health" -Headers @{"Authorization"="Bearer $token"}
```

### 3. Check Logs
Verify application startup in:
- `C:\path\to\your\PortwayApi\log\portwayapi-[date].log`

## Troubleshooting

### Common Issues

| Error | Possible Cause | Solution |
|-------|---------------|----------|
| 500.19 | Missing ASP.NET Core Module | Reinstall ASP.NET Core Hosting Bundle |
| 500 | Application error | Check Event Viewer and application logs |
| 403 | Permission issues | Verify folder permissions for app pool identity |
| Database errors | Connection issues | Check connection strings and SQL access |
| Blank screen | Required TLS certificate | Bind a certificate and enforce HTTPS traffic |

### Enable Detailed Logging
For troubleshooting, enable stdout logging in web.config:
```xml
<aspNetCore stdoutLogEnabled="true" stdoutLogFile=".\logs\stdout" />
```

### Log Locations
- **Application logs**: `C:\path\to\your\PortwayApi\log\portwayapi-*.log`
- **IIS logs**: `C:\inetpub\logs\LogFiles\W3SVC[ID]\`
- **Windows Event Viewer**: Application and System logs

## Security Best Practices

### HTTPS Configuration
1. Install SSL certificate in IIS
2. Configure HTTPS binding
3. Enforce HTTPS with URL Rewrite rules using the [IIS Rewrite Module](https://www.iis.net/downloads/microsoft/url-rewrite)

### Access Token Security
- Restrict access to the tokens directory
- Implement token rotation policy
- Use token scoping for least-privilege access


### Firewall Whitelisting
- Restrict access to the API by enabling Firewall in IIS
- Set the default response to Deny
- Add only whitelisted IP address ranges to the whitelist

### Application Pool Hardening
- Use dedicated service account for production
- Implement principle of least privilege
- Regularly review and audit permissions

## Maintenance & Updates

### Updating Portway
1. Stop the application pool:
   ```powershell
   Stop-WebAppPool -Name "PortwayApi"
   ```

2. Backup current deployment:
   ```cmd
   xcopy C:\path\to\your\PortwayApi C:\Backup\PortwayApi_%date% /E /I
   ```

3. Deploy new files to the application directory

4. Start the application pool:
   ```powershell
   Start-WebAppPool -Name "PortwayApi"
   ```

### Backup Strategy
Include these critical components in your backup plan:
- `auth.db` - Authentication database
- `tokens/` directory - Access tokens
- `environments/` directory - Configuration
- `endpoints/` directory - Endpoint definitions
- Application logs for audit purposes

## Next Steps

Now that Portway is installed, continue with:

- [Configure Endpoints](./endpoints/overview) - Set up SQL, Proxy, and Webhook endpoints
- [Manage Authentication](./security/tokens) - Generate and manage access tokens  
- [Configure Environments](./environments) - Set up database connections
- [Monitor Your Installation](./monitoring) - Learn about logging and health checks
- [Production Security](./security/production) - Implement security best practices