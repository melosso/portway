---
title: Deploying with Docker
description: "Deploy Portway with Docker Compose, from a first container through to a production setup"
---

# Deploying with Docker

Requires [Docker](https://www.docker.com/get-started). Linux: [Deploying on Linux](/guide/deployment-linux). Windows Server: [Deploying on Windows Server](/guide/deployment-windows). The image supports `linux/amd64` and `linux/arm64`.

## Quick start

A minimal `docker-compose.yml` is in [Getting Started](/guide/getting-started). The API listens on `http://localhost:8080`.

## Configuration

### Environment variables

Compose file with the environment variables:

```yaml
services:
  portway:
    image: ghcr.io/melosso/portway:latest
    ports:
      - "8080:8080"
    volumes:
      - portway_app:/app
      - ./environments:/app/environments
      - ./endpoints:/app/endpoints
      - ./tokens:/app/tokens
      - ./log:/app/log
      - ./data:/app/data
    environment:
      - PORTWAY_ENCRYPTION_KEY=YourEncryptionKeyHere
      - PORTWAY_PUBLIC_ORIGINS=https://example.com,https://api.example.com
      - PORTWAY_SECURE_COOKIES=false # true behind TLS
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health/live"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 10s
      
volumes:
  portway_app:
```

### Core settings

| Variable | Description | Default Value | Legacy name |
|----------|-------------|---------------|-------------|
| `PORTWAY_ENCRYPTION_KEY` | Encryption key for secrets at rest; required outside Development | (none) | — |
| `PORTWAY_USE_HTTPS` | Kestrel serves HTTPS directly | `false` | `Use_HTTPS` |
| `PORTWAY_ALLOWED_HOSTS` | Allowed host names | `*` | `AllowedHosts` |
| `PORTWAY_PATH_BASE` | Base path for the application | (empty) | `PathBase` |

Legacy names remain supported with a deprecation warning at startup; the `PORTWAY_*` name takes precedence.

:::warning
The setting `PORTWAY_USE_HTTPS=true` requires a certificate for Kestrel (e.g. `Kestrel__Certificates__Default__Path`); without one the container fails to start with `BackgroundService failed / Hosting failed to start`. Behind a TLS-terminating reverse proxy (nginx, Caddy, Cloudflare Tunnel), keep `false`.
:::

### Web UI settings

| Variable | Description | Default Value | Legacy name |
|----------|-------------|---------------|-------------|
| `PORTWAY_WEBUI_ENABLED` | Serves the console; `false` turns it off | `true` | `WebUi__Enabled` |
| `PORTWAY_SEED_PASSWORD` | Fixed password for the first account; demo instances only | (none) | `WebUi__SeedPassword` |
| `PORTWAY_PUBLIC_ORIGINS` | Origins allowed to reach the console from outside the local network, comma-separated | (empty) | `WebUi__PublicOrigins__0`, `__1`, ... |
| `PORTWAY_SECURE_COOKIES` | HTTPS-only console cookies; set `true` when TLS terminates in front of the container | `false` | `WebUi__SecureCookies` |

Banner and sign-in footer settings are set under **Settings** in the console. [Environment variables](/reference/app-settings#environment-variables) lists every `PORTWAY_*` name. A setting with a set environment variable is read-only in the console.

### Proxy configuration

Credentials for outbound requests through an authenticating (NTLM) corporate proxy:

| Variable | Description | Example | Legacy name |
|----------|-------------|---------|-------------|
| `PORTWAY_PROXY_USERNAME` | Proxy username | `serviceaccount` | `PROXY_USERNAME` |
| `PORTWAY_PROXY_PASSWORD` | Proxy password | `password` | `PROXY_PASSWORD` |
| `PORTWAY_PROXY_DOMAIN` | Domain for proxy authentication (NTLM) | `YOURDOMAIN` | `PROXY_DOMAIN` |

:::note
NTLM requires all three variables, including `PORTWAY_PROXY_DOMAIN`.
:::

```yaml
    environment:
      - PORTWAY_PROXY_USERNAME=serviceaccount
      - PORTWAY_PROXY_PASSWORD=password
      - PORTWAY_PROXY_DOMAIN=YOURDOMAIN
```

### Azure Key Vault (optional)

| Variable | Description | Legacy name |
|----------|-------------|-------------|
| `PORTWAY_KEYVAULT_URI` | Azure Key Vault URI | `KEYVAULT_URI` |
| `AZURE_CLIENT_ID` | Azure application client ID | — |
| `AZURE_TENANT_ID` | Azure tenant ID | — |
| `AZURE_CLIENT_SECRET` | Azure client secret | — |

The `AZURE_*` variables are read by the Azure SDK's `DefaultAzureCredential`.

```yaml
    environment:
      - PORTWAY_KEYVAULT_URI=https://your-keyvault-name.vault.azure.net/
      - AZURE_CLIENT_ID=your-client-id
      - AZURE_TENANT_ID=your-tenant-id
      - AZURE_CLIENT_SECRET=your-client-secret
```

## Data persistence

| Data | Location |
|---|---|
| `environments/`, `endpoints/`, `tokens/` | Bind mounts, editable on the host |
| Application and traffic logs (`log/traffic_logs.db`) | `./log` |
| `auth.db`, `metrics.db`, `mcp.db`, `portway.key` | Application root, in the `portway_app` volume |
| `appsettings.overrides.json` (settings saved in the console) | Application root, in the `portway_app` volume |

An environment variable overrides the same setting in `appsettings.overrides.json`. The console displays that setting as read-only. Leave a variable unset to manage the setting from the console.

`appsettings.overrides.json` can also be bind-mounted as a single file. Create the file with the content `{}` before the first start. Docker creates a directory for a missing bind-mount source.

## Customizing the setup

Endpoint and environment files in the mounted directories are reloaded on change. Other configuration changes apply after a restart:

```bash
docker compose restart
```

## Managing tokens

Tokens are managed in the [console](/guide/webui) at `http://localhost:8080/ui` under **Access Tokens**.

## Going to production

### Set the encryption key

The `PORTWAY_ENCRYPTION_KEY` value encrypts secrets in environment settings. Keep it out of the compose file, e.g. in a `.env` file or a secrets manager:

::: code-group

```bash [Linux]
openssl rand -base64 48
```

```powershell [Windows]
$bytes = New-Object byte[] 48
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
[Convert]::ToBase64String($bytes)
```

:::

### Terminate TLS in front of the container

The container serves plain HTTP. TLS terminates at a reverse proxy (nginx, Traefik, Caddy) or ingress.

### Backups

Back up the bind mounts and the `portway_app` volume ([Data persistence](#data-persistence)). Portway also writes a daily snapshot of its databases and configuration to `.backups/snapshots/` ([DatabaseMaintenance](/reference/app-settings#databasemaintenance)).

### Monitoring

Health endpoints: [Health and Logs](/guide/monitoring). Metrics: [Telemetry](/guide/telemetry). Upgrades: [Upgrading Portway](/guide/upgrading).

## Next steps

- [Getting Started](/guide/getting-started)
- [SQL Endpoints](/guide/endpoints-sql)
- [Security](/guide/security)
- [Health and Logs](/guide/monitoring)
