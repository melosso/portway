---
title: Deploying with Docker
description: "Deploy Portway with Docker Compose, from a first container through to a production setup"
---

# Deploying with Docker

Portway with Docker Compose, from a first container to production settings. Requires [Docker](https://www.docker.com/get-started). Windows Server: [Deploying on Windows Server](/guide/deployment-windows).

## Quick start

A minimal `docker-compose.yml` is in [Getting Started](/guide/getting-started). The API listens on `http://localhost:8080`.

## Configuration

### Environment variables

Compose file with the common environment variables:

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
      - PORTWAY_ALLOWED_HOSTS=*
      - PORTWAY_PATH_BASE=

      # Web UI settings
      - WebUi__Enabled=true
      - WebUi__PublicOrigins__0=https://example.com
      - WebUi__PublicOrigins__1=https://api.example.com
      - PORTWAY_SECURE_COOKIES=false
      - WebUi__Customization__PromoText=
      - WebUi__Customization__LoginFooter=If you don't have an account, please contact your [administrator](mailto:support@democompany.local).
    
      # Proxy settings for Kerberos/NTLM
      # - PORTWAY_PROXY_USERNAME=serviceaccount
      # - PORTWAY_PROXY_PASSWORD=password
      # - PORTWAY_PROXY_DOMAIN=YOURDOMAIN

      # Azure credentials
      # - PORTWAY_KEYVAULT_URI=https://your-keyvault-name.vault.azure.net/
      # - AZURE_CLIENT_ID=your-client-id
      # - AZURE_TENANT_ID=your-tenant-id
      # - AZURE_CLIENT_SECRET=your-client-secret
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
| `WebUi__Enabled` | Enables the console | (none) | — |
| `PORTWAY_ADMIN_KEY` | Legacy; enables the console when `WebUi__Enabled` is unset, not used for sign-in | (none) | `WebUi__AdminApiKey` |
| `WebUi__SeedPassword` | Fixed password for the first account; demo instances only | (none) | — |
| `WebUi__PublicOrigins` | Origins allowed to reach the console from outside the local network (array) | (empty) | — |
| `PORTWAY_SECURE_COOKIES` | HTTPS-only console cookies | `true` | `WebUi__SecureCookies` |
| `WebUi__Customization__PromoText` | Banner text | (none) | — |
| `WebUi__Customization__LoginFooter` | Text below the sign-in form | (none) | — |

Multiple origins use index notation:
```yaml
- WebUi__PublicOrigins__0=https://example.com
- WebUi__PublicOrigins__1=https://api.example.com
```

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

### Azure Key Vault (optional)

Azure Key Vault variables:

| Variable | Description | Legacy name |
|----------|-------------|-------------|
| `PORTWAY_KEYVAULT_URI` | Azure Key Vault URI | `KEYVAULT_URI` |
| `AZURE_CLIENT_ID` | Azure application client ID | — |
| `AZURE_TENANT_ID` | Azure tenant ID | — |
| `AZURE_CLIENT_SECRET` | Azure client secret | — |

The `AZURE_*` variables are read by the Azure SDK's `DefaultAzureCredential`.

## Data persistence

Volume mounts:

```yaml
volumes:
  - ./environments:/app/environments
  - ./endpoints:/app/endpoints
  - ./tokens:/app/tokens
  - ./log:/app/log
  - ./data:/app/data
```

| Data | Location |
|---|---|
| `environments/`, `endpoints/`, `tokens/` | Bind mounts, editable on the host |
| Application and traffic logs (`log/traffic_logs.db`) | `./log` |
| `auth.db`, `metrics.db`, `mcp.db`, `portway.key` | Application root, in the `portway_app` volume |

## Customizing the setup

Endpoint and environment files in the mounted directories are reloaded on change. Other configuration changes apply after a restart:

```bash
docker compose restart
```

## Managing tokens

Tokens are managed in the [console](/guide/webui) at `http://localhost:8080/ui` under **Access Tokens**, with `WebUi__Enabled=true`.

## Going to production

### Set the encryption key

The `PORTWAY_ENCRYPTION_KEY` value encrypts secrets in environment settings. Keep it out of the compose file, e.g. in a `.env` file or a secrets manager:

```bash
openssl rand -base64 48
```

### Terminate TLS in front of the container

The container serves plain HTTP. TLS terminates at a reverse proxy (nginx, Traefik, Caddy) or ingress.

### Back up your state

Back up the bind mounts and the `portway_app` volume ([Data persistence](#data-persistence)).

### Watch it

Health endpoints and metrics: [Monitoring](/guide/monitoring). Upgrades: [Upgrading Portway](/guide/upgrading).

## Next steps

- [Getting Started](/guide/getting-started)
- [SQL Endpoints](/guide/endpoints-sql)
- [Security](/guide/security)
- [Monitoring](/guide/monitoring)
