---
title: Deploying on Windows Server
description: "Deploy Portway as an IIS website on Windows Server with HTTPS and a dedicated Application Pool"
---

# Deploying on Windows Server

Portway on Windows Server behind IIS. Container deployment: [Deploying with Docker](/guide/deployment-docker).

## Prerequisites

- Windows Server with IIS installed and running
- Administrator access
- [.NET 11 ASP.NET Core Hosting Bundle](https://get.dot.net/11)
- A TLS/SSL certificate (self-signed is acceptable for internal deployments)

:::warning
Install the Hosting Bundle, not the x64 runtime; only the Hosting Bundle includes the IIS integration module. Run `iisreset` after installation.
:::

## Installation

### 1. Generate the encryption key

Machine-level environment variable, set before the first start:

```powershell
$bytes = New-Object byte[] 48
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
[Environment]::SetEnvironmentVariable("PORTWAY_ENCRYPTION_KEY", [Convert]::ToBase64String($bytes), "Machine")
```

### 2. Deploy application files

Extract the release to the target directory (e.g. `C:\Apps\Portway`).

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
For NTLM pass-through on proxy endpoints (e.g. Exact Globe+, AFAS Profit), run the Application Pool as a domain user with the required network access instead of ApplicationPoolIdentity.
:::

### 4. Start and verify

The first start creates `tokens/`, `log/` and `auth.db`. Checks:

- `https://localhost/health/live` returns `Alive`
- `https://localhost/docs` serves the API reference

## Initial configuration

Access token and environments: [Getting Started](/guide/getting-started). The Application Pool identity needs read access to `tokens/` and `environments/` under the site root.

## Troubleshooting

| Error | Likely cause | Resolution |
|---|---|---|
| HTTP 500.19 | ASP.NET Core Module not installed | Reinstall the Hosting Bundle and run `iisreset` |
| HTTP 500 | Application startup error | Check `log/portwayapi-*.log` and Windows Event Viewer |
| HTTP 403 | Directory permissions | Run `icacls` to grant the Application Pool identity access |
| Blank screen | No HTTPS binding or missing certificate | Bind a certificate to the site in IIS Manager |
| Database errors | Invalid connection string | Verify the connection string and SQL Server network access |

Startup errors outside the application log are captured with stdout logging in `web.config`:

```xml
<aspNetCore stdoutLogEnabled="true" stdoutLogFile=".\log\stdout" />
```

| Log | Location |
|---|---|
| Application | `log/portwayapi-*.log` |
| IIS | `C:\inetpub\logs\LogFiles\W3SVC[ID]\` |
| Startup errors | Windows Event Viewer → Application |

## Security configuration

- Enforce HTTPS using URL Rewrite rules ([IIS Rewrite Module](https://www.iis.net/downloads/microsoft/url-rewrite))
- Restrict `tokens/` read access to the Application Pool identity
- Restrict client addresses in IIS Manager (IP Address and Domain Restrictions)
- Use a dedicated domain service account with minimum SQL permissions

Security configuration: [Security](/guide/security).

## Backup

Back up `auth.db`, `mcp.db`, `portway.key`, `.core/`, `appsettings.overrides.json`, `environments/`, `endpoints/` and the file storage directory. Upgrades: [Upgrading Portway](/guide/upgrading).

## Next steps

- [Configure Environments](/guide/environments)
- [Configure Endpoints](/guide/endpoints-sql)
- [Security](/guide/security)
- [Health and Logs](/guide/monitoring)
