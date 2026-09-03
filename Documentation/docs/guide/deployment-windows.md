---
title: Deploying on Windows Server
description: "Deploy Portway as an IIS website on Windows Server with HTTPS and a dedicated Application Pool"
---

# Deploying on Windows Server

Deploying Portway on Windows Server behind IIS. If containers suit you better, [Deploying with Docker](/guide/docker-compose) covers that path instead.

The steps assume working knowledge of IIS and your network and data sources; the essentials are all here, though some details will depend on your existing environment.

## Prerequisites

- Windows Server with IIS installed and running
- Administrator access
- [.NET 11 ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/en-us/download/dotnet/11.0)
- A TLS/SSL certificate (self-signed is acceptable for internal deployments)

:::warning
Download the **Hosting Bundle**, not the x64 runtime installer. The Hosting Bundle includes the IIS integration module the runtime package omits. Restart IIS after installation (`iisreset`).
:::

## Installation

### 1. Generate the encryption key

Set the encryption key as a Machine-level environment variable before deploying the application files:

```powershell
$bytes = New-Object byte[] 48
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
[Environment]::SetEnvironmentVariable("PORTWAY_ENCRYPTION_KEY", [Convert]::ToBase64String($bytes), "Machine")
```

### 2. Deploy application files

Extract the Portway release to your target directory (e.g. `C:\Apps\Portway`).

### 3. Configure IIS

1. Open IIS Manager
2. Create an Application Pool:
   - Name: `PortwayAppPool`
   - .NET CLR version: `No Managed Code`
   - Pipeline mode: `Integrated`
   - Start Mode: `AlwaysRunning`
   - Idle Time-out: `0`
3. Create a new Website:
   - Application pool: `PortwayAppPool`
   - Physical path: `C:\Apps\Portway`
   - HTTPS binding with your certificate
4. Set directory permissions:
   ```cmd
   icacls "C:\Apps\Portway" /grant "IIS AppPool\PortwayAppPool:(OI)(CI)M" /T
   ```

:::info
If any proxy endpoint needs NTLM pass-through (e.g. for Exact Globe+ or AFAS Profit), bind the Application Pool identity to a domain user with the required network access instead of using ApplicationPoolIdentity.
:::

### 4. Start and verify

Start the website. On first run, Portway creates `tokens/`, `log/`, and `auth.db` automatically.

Verify the application is running:
- `https://localhost/health/live`, returns `Alive`
- `https://localhost/docs`, OpenAPI documentation interface

## Initial configuration

From here the setup is the same whichever way you host Portway. [Getting Started](/guide/getting-started) walks you through retrieving your access token and configuring your environments.

One detail is worth keeping in mind on IIS. The `tokens/` and `environments/` directories are created on first run under the site root, so the Application Pool identity needs read access to both.

## Troubleshooting

| Error | Likely cause | Resolution |
|---|---|---|
| HTTP 500.19 | ASP.NET Core Module not installed | Reinstall the Hosting Bundle and run `iisreset` |
| HTTP 500 | Application startup error | Check `log/portwayapi-*.log` and Windows Event Viewer |
| HTTP 403 | Directory permissions | Run `icacls` to grant the Application Pool identity access |
| Blank screen | No HTTPS binding or missing certificate | Bind a certificate to the site in IIS Manager |
| Database errors | Invalid connection string | Verify the connection string and SQL Server network access |

Enable stdout logging in `web.config` for startup errors that do not reach the application log:

```xml
<aspNetCore stdoutLogEnabled="true" stdoutLogFile=".\log\stdout" />
```

**Log locations:**
- Application log: `log/portwayapi-*.log`
- IIS log: `C:\inetpub\logs\LogFiles\W3SVC[ID]\`
- Startup errors: Windows Event Viewer → Application

## Security configuration

- Enforce HTTPS using URL Rewrite rules ([IIS Rewrite Module](https://www.iis.net/downloads/microsoft/url-rewrite))
- Restrict `tokens/` directory read access to the application pool identity only
- Configure IP whitelisting in IIS Manager (IP Address and Domain Restrictions)
- Use a dedicated domain service account with minimum SQL permissions for proxy endpoints

See [Security](/guide/security) for the full security configuration reference.

## Backup

Include these in your backup plan:

- `auth.db` for authentication database
- `tokens/` for token files
- `environments/` for connection strings and settings
- `endpoints/` for endpoint definitions

For upgrades, see [Upgrading Portway](/guide/upgrading).

## Next steps

- [Configure Environments](/guide/environments)
- [Configure Endpoints](/guide/endpoints-sql)
- [Security](/guide/security)
- [Monitoring](/guide/monitoring)
