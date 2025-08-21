# Docker Installation

This guide explains how to deploy Portway API using Docker Compose for quick development, testing and/or Home Lab environments.

## Prerequisites

Before you begin, ensure you have:

- [Docker](https://www.docker.com/get-started) installed and running.

We'll primarily be using [Docker Compose](https://docs.docker.com/compose/install/).

## Quick Start

1. **Create a docker-compose.yml file:**
   ```yaml
   services:
     portway:
       image: ghcr.io/melosso/portway:latest
       ports:
         - "8080:8080"
       volumes:
         - ./environments:/app/environments
         - ./endpoints:/app/endpoints
         - ./tokens:/app/tokens
         - ./log:/app/log
         - ./data:/app/data
       environment:
         - ASPNETCORE_ENVIRONMENT=Production
         - ASPNETCORE_URLS=http://+:8080
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
      - ./environments:/app/environments
      - ./endpoints:/app/endpoints
      - ./tokens:/app/tokens
      - ./log:/app/log
      - ./data:/app/data
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
      - USE_HTTPS=false
      - PROXY_USERNAME=serviceaccount
      - PROXY_PASSWORD=password
      - PROXY_DOMAIN=YOURDOMAIN
      # - KEYVAULT_URI=https://your-keyvault-name.vault.azure.net/
      # - AZURE_CLIENT_ID=your-client-id
      # - AZURE_TENANT_ID=your-tenant-id
      # - AZURE_CLIENT_SECRET=your-client-secret
```

### Core Settings

| Variable | Description | Default Value |
|----------|-------------|---------------|
| `ASPNETCORE_ENVIRONMENT` | Application environment | `Production` |
| `ASPNETCORE_URLS` | URL binding configuration | `http://+:8080` |
| `USE_HTTPS` | Enable/disable HTTPS | `false` |

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
