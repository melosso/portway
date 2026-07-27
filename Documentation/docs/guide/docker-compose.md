---
title: Deploying with Docker
description: "Deploy Portway with Docker Compose, from a first container through to a production setup"
---

# Deploying with Docker

This guide takes you from a first container through to the settings worth having in place before you put Portway in front of real traffic, whether that is a laptop, a Home Lab, or a production host. Before you begin, make sure [Docker](https://www.docker.com/get-started) is installed and running.

If you would rather host on Windows Server behind IIS, [Deploying on Windows Server](/guide/deployment-windows) covers that path.

## Quick start

If you have not started a container yet, [Getting Started](/guide/getting-started) has a minimal `docker-compose.yml` you can copy and run in a couple of minutes. Once it is up, the API is available at `http://localhost:8080`.

The rest of this page picks up from there, covering the settings you are most likely to reach for next.

## Configuration

### Environment variables

The Docker Compose configuration can be extended with additional environment variables for advanced functionality:

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
      # Set your environment variables here
      - PORTWAY_ENCRYPTION_KEY=YourEncryptionKeyHere
      - AllowedHosts=*
      - PathBase=

      # Web UI settings
      - WebUi__AdminApiKey=INSECURE-CHANGE-ME-admin-api-key
      - WebUi__PublicOrigins__0=https://example.com
      - WebUi__PublicOrigins__1=https://api.example.com
      - WebUi__SecureCookies=false  
      - WebUi__Customization__PromoText=
      - WebUi__Customization__LoginFooter=If you don't have an account, please contact your [administrator](mailto:support@democompany.local).
    
      # Proxy settings for Kerberos/NTLM
      # - PROXY_USERNAME=serviceaccount
      # - PROXY_PASSWORD=password
      # - PROXY_DOMAIN=YOURDOMAIN

      # Azure credentials
      # - KEYVAULT_URI=https://your-keyvault-name.vault.azure.net/
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

| Variable | Description | Default Value |
|----------|-------------|---------------|
| `PORTWAY_ENCRYPTION_KEY` | Encryption secret | (Hardcoded) |
| `Use_HTTPS` | Whether Kestrel serves HTTPS directly. See note below. | `false` |
| `AllowedHosts` | Allowed host names | `*` |
| `PathBase` | Base path for the application | (empty) |

:::warning
The flag `Use_HTTPS` **requires a TLS certificate to be available to Kestrel.** If you set this to `true` without mounting a valid certificate, the container will fail to start immediately with `BackgroundService failed / Hosting failed to start`.

<br>

In most Docker deployments, SSL termination is handled by an external reverse proxy (nginx, Caddy, Cloudflare Tunnel, etc.) and Portway runs plain HTTP internally, keep `Use_HTTPS=false` in that case. 

<br>

Only set `Use_HTTPS=true` if Portway is directly internet-facing **and** you have configured a certificate (e.g. via `Kestrel__Certificates__Default__Path`).
:::

### Web UI settings

| Variable | Description | Default Value |
|----------|-------------|---------------|
| `WebUi__AdminApiKey` | Admin API key for web UI access | (none) |
| `WebUi__PublicOrigins` | Allowed origins for CORS (array) | (empty) |
| `WebUi__SecureCookies` | Use secure cookies | `false` |
| `WebUi__Customization__PromoText` | Banner text at the top | (none) |
| `WebUi__Customization__LoginFooter` | Footer text below login area | (none) |

For `WebUi__PublicOrigins`, use index notation for multiple origins:
```yaml
- WebUi__PublicOrigins__0=https://example.com
- WebUi__PublicOrigins__1=https://api.example.com
```

### Proxy configuration

Configure these settings if your environment requires proxy authentication. Portway supports NTLM authentication for corporate proxy environments:

| Variable | Description | Example |
|----------|-------------|---------|
| `PROXY_USERNAME` | Proxy username | `serviceaccount` |
| `PROXY_PASSWORD` | Proxy password | `password` |
| `PROXY_DOMAIN` | Domain for proxy authentication (NTLM) | `YOURDOMAIN` |

:::note
When using NTLM authentication, ensure all three proxy variables are configured. The `PROXY_DOMAIN` is required for proper NTLM handshake with corporate proxy servers.
:::

### Azure Key Vault (optional)

For production environments, you can integrate with Azure Key Vault by uncommenting and configuring:

| Variable | Description |
|----------|-------------|
| `KEYVAULT_URI` | Azure Key Vault URI |
| `AZURE_CLIENT_ID` | Azure application client ID |
| `AZURE_TENANT_ID` | Azure tenant ID |
| `AZURE_CLIENT_SECRET` | Azure client secret |

## Data persistence

The Docker Compose setup includes volume mounts for data persistence:

```yaml
volumes:
  - ./environments:/app/environments
  - ./endpoints:/app/endpoints
  - ./tokens:/app/tokens
  - ./log:/app/log
  - ./data:/app/data
```

- **Configuration files**: `environments/`, `endpoints/`, and `tokens/` are bind-mounted so you can edit them from the host
- **Logs**: written to `./log`, including the traffic log database at `log/traffic_logs.db`
- **Databases**: `auth.db`, `metrics.db`, and `mcp.db` are created at the application root, so they live in the `portway_app` named volume rather than a bind mount

## Customizing the setup

### Custom configuration

1. Create your configuration files in the mounted directories:
   - `./endpoints/` - API endpoint definitions
   - `./environments/` - Environment configurations
   - `./tokens/` - Authentication tokens

2. Restart the container to apply changes:
   ```bash
   docker compose restart
   ```

## Managing tokens

Token management is handled through the [Web UI](/guide/webui). Set `WebUi__AdminApiKey` in your environment configuration to enable it, then navigate to `http://localhost:8080/ui` and open **Tokens** to create, revoke, rotate, and audit tokens.

```yaml
environment:
  - WebUi__AdminApiKey=your-secure-password
```

## Going to production

The compose file above is deliberately minimal. A few additions are worth making before real traffic arrives.

### Set the encryption key

`PORTWAY_ENCRYPTION_KEY` protects the connection strings in your environment settings. Generate one and keep it out of the compose file itself, for example in a `.env` file or your secrets manager:

```bash
openssl rand -base64 48
```

### Terminate TLS in front of the container

Portway serves plain HTTP inside the container. Put a reverse proxy (nginx, Traefik, Caddy) in front of it to terminate TLS, or publish it behind an ingress that does.

### Back up your state

Your configuration lives in the bind mounts, and the databases live in the `portway_app` volume. [Data Persistence](#data-persistence) above lists exactly what sits where, and a backup wants both.

### Watch it

Health endpoints and Prometheus metrics are described in [Monitoring](/guide/monitoring), and [Security](/guide/security) covers token scoping, rate limiting, and network restrictions.

For upgrades, see [Upgrading Portway](/guide/upgrading).

## Next steps

After successful installation:

1. Review the [Getting Started Guide](/guide/getting-started) for basic usage
2. Configure your [Endpoints](/guide/endpoints-static) 
3. Set up [Security](/guide/security) and authentication
4. Monitor your deployment with [Health Checks](/guide/monitoring)
