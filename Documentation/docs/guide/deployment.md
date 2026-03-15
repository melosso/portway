# Deploying to Windows Server (IIS)

> Deploy Portway as an IIS website on Windows Server with HTTPS and a dedicated Application Pool.

For Docker-based deployment, see [Docker Installation](docker-compose.md).

:::info
This guide assumes working knowledge of IIS and your network/data sources. The steps cover the required configuration — some details depend on your existing environment.
:::

## Prerequisites

- Windows Server with IIS installed and running
- Administrator access
- [ASP.NET Core 10 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/10.0)
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
- `https://localhost/health/live` — returns `Alive`
- `https://localhost/docs` — OpenAPI documentation interface

## Initial configuration

### Retrieve the access token

Find the generated token at `tokens/[SERVERNAME].txt`:

```json
{
  "Username": "SERVER-NAME",
  "Token": "your-bearer-token-here",
  "AllowedScopes": "*",
  "AllowedEnvironments": "*"
}
```

:::warning
Remove this file from disk immediately after recording the token. The token is a plaintext secret. Unauthorized access to this file compromises your gateway.
:::

### Configure environments

Edit `environments/settings.json`:

```json
{
  "Environment": {
    "ServerName": "YOUR_SERVER",
    "AllowedEnvironments": ["prod", "dev", "test"]
  }
}
```

Create `environments/{name}/settings.json` for each environment:

::: code-group

```json [Production]
{
  "ServerName": "YOUR_SQL_SERVER",
  "ConnectionString": "Server=YOUR_SQL_SERVER;Database=prod;Trusted_Connection=True;TrustServerCertificate=true;"
}
```

```json [Development]
{
  "ServerName": "YOUR_SQL_SERVER",
  "ConnectionString": "Server=YOUR_SQL_SERVER;Database=dev;Trusted_Connection=True;TrustServerCertificate=true;"
}
```

:::

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

See [Security](./security) for the full security configuration reference.

## Backup

Include these in your backup plan:

- `auth.db` — authentication database
- `tokens/` — token files
- `environments/` — connection strings and settings
- `endpoints/` — endpoint definitions

For upgrades, see [Upgrading Portway](./upgrading).

## Next steps

- [Configure Environments](./environments)
- [Configure Endpoints](./endpoints-sql)
- [Security](./security)
- [Monitoring](./monitoring)
