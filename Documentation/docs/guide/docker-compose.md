# Docker Installation

This guide explains how to deploy Portway using Docker Compose for quick development, testing and/or Home Lab environments. Before you begin, ensure you have [Docker](https://www.docker.com/get-started) installed and running.

## Quick Start

1. **Create a docker-compose.yml file:**

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

volumes:
  portway_app:
```

2. **Start the application:**

  ```bash
  docker compose up -d
  ```

3. **Verify the installation:**
   The API will be available at `http://localhost:8080`

## Configuration

### Environment Variables

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

### Core Settings

| Variable | Description | Default Value |
|----------|-------------|---------------|
| `PORTWAY_ENCRYPTION_KEY` | Encryption secret | (Hardcoded) |
| `Use_HTTPS` | Whether Kestrel serves HTTPS directly. See note below. | `false` |
| `AllowedHosts` | Allowed host names | `*` |
| `PathBase` | Base path for the application | (empty) |

> [!WARNING]
> **`Use_HTTPS` requires a TLS certificate to be available to Kestrel.** If you set this to `true` without mounting a valid certificate, the container will fail to start immediately with `BackgroundService failed / Hosting failed to start`.
>
> In most Docker deployments, SSL termination is handled by an external reverse proxy (nginx, Caddy, Cloudflare Tunnel, etc.) and Portway runs plain HTTP internally, keep `Use_HTTPS=false` in that case. Only set `Use_HTTPS=true` if Portway is directly internet-facing **and** you have configured a certificate (e.g. via `Kestrel__Certificates__Default__Path`).

### Web UI Settings

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

### Proxy Configuration

Configure these settings if your environment requires proxy authentication. Portway supports NTLM authentication for corporate proxy environments:

| Variable | Description | Example |
|----------|-------------|---------|
| `PROXY_USERNAME` | Proxy username | `serviceaccount` |
| `PROXY_PASSWORD` | Proxy password | `password` |
| `PROXY_DOMAIN` | Domain for proxy authentication (NTLM) | `YOURDOMAIN` |

> [!NOTE]
> When using NTLM authentication, ensure all three proxy variables are configured. The `PROXY_DOMAIN` is required for proper NTLM handshake with corporate proxy servers.

### Azure Key Vault (Optional)

For production environments, you can integrate with Azure Key Vault by uncommenting and configuring:

| Variable | Description |
|----------|-------------|
| `KEYVAULT_URI` | Azure Key Vault URI |
| `AZURE_CLIENT_ID` | Azure application client ID |
| `AZURE_TENANT_ID` | Azure tenant ID |
| `AZURE_CLIENT_SECRET` | Azure client secret |

## Data Persistence

The Docker Compose setup includes volume mounts for data persistence:

```yaml
volumes:
  - ./environments:/app/environments
  - ./endpoints:/app/endpoints
  - ./tokens:/app/tokens
  - ./log:/app/log
  - ./data:/app/data
```

- **Configuration files**: Mounted from local directories for easy editing
- **Authentication data**: Stored in the `./data` directory
- **Logs**: Available in the `./log` directory

## Customizing the Setup

### Custom Configuration

1. Create your configuration files in the mounted directories:
   - `./endpoints/` - API endpoint definitions
   - `./environments/` - Environment configurations
   - `./tokens/` - Authentication tokens

2. Restart the container to apply changes:
   ```bash
   docker compose restart
   ```

## Health Check

The container can be monitored to verify the API is responding:

```bash
# Check container health
docker compose ps

# View container logs
docker compose logs portway
```

## Troubleshooting

### Container Won't Start

1. Check Docker logs:
   ```bash
   docker compose logs portway
   ```

### Configuration Issues

1. Verify environment variables are set correctly
2. Check mounted volume permissions
3. Review application logs in the `./log` directory

### Proxy Authentication

If you're behind a corporate proxy:

1. Update the proxy settings in the environment variables
2. Ensure your proxy credentials are correct
3. Contact your network administrator for proxy details



## Managing tokens

Token management is handled through the [Web UI](./webui). Set `WebUi__AdminApiKey` in your environment configuration to enable it, then navigate to `http://localhost:8080/ui` and open **Tokens** to create, revoke, rotate, and audit tokens.

```yaml
environment:
  - WebUi__AdminApiKey=your-secure-password
```

## Next Steps

After successful installation:

1. Review the [Getting Started Guide](getting-started.md) for basic usage
2. Configure your [Endpoints](endpoints-static.md) 
3. Set up [Security](security.md) and authentication
4. Monitor your deployment with [Health Checks](monitoring.md)

## Production Considerations

> [!WARNING]
> This Docker setup is intended for development and testing. For production deployments, consider:
> - Using proper secrets management
> - Implementing reverse proxy with SSL/TLS
> - Setting up proper logging and monitoring
> - Following security best practices

For production deployments, see the [Deployment Guide](deployment.md).