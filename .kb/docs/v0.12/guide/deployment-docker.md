---
title: Deploying with Docker
description: "Deploy Portway with Docker Compose, from a first container through to a production setup"
---

# Deploying with Docker

## Prerequisites

- [Docker Engine](https://www.docker.com/get-started) with Compose support
- *Alternative:* If you use Podman, make sure to use the `podman-docker` plugin.

## Compose file

```yaml [docker-compose.yml]
services:
  portway:
    image: ghcr.io/melosso/portway:latest
    ports:
      - "8080:8080"
    volumes:
      - portway_app:/app:z
      - ./environments:/app/environments:z
      - ./endpoints:/app/endpoints:z
      - ./log:/app/log:z
      - ./data:/app/data:z
    environment:
      - PORTWAY_ENCRYPTION_KEY=${PORTWAY_ENCRYPTION_KEY}
      - PORTWAY_PUBLIC_ORIGINS=https://api.example.com
      - PORTWAY_SECURE_COOKIES=true
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

`PORTWAY_PUBLIC_ORIGINS` allows console access from external origins. `PORTWAY_SECURE_COOKIES=true` requires TLS in front of the container. Every `PORTWAY_*` variable: [Environment variables](/reference/app-settings#environment-variables).

## Encryption key

`PORTWAY_ENCRYPTION_KEY` encrypts secrets in environment settings and is required. The compose file reads it from `.env` next to `docker-compose.yml`:

::: code-group

```bash [Linux]
echo "PORTWAY_ENCRYPTION_KEY=$(openssl rand -base64 48)" > .env
```

```powershell [Windows]
$bytes = New-Object byte[] 48
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
"PORTWAY_ENCRYPTION_KEY=$([Convert]::ToBase64String($bytes))" | Out-File .env -Encoding ascii
```

:::

## Start

```sh
docker compose pull && docker compose up -d
```

The API listens on `http://localhost:8080`. Checks:

- `curl http://localhost:8080/health/live` returns `Alive`
- `http://localhost:8080/docs` serves the API reference

## Data persistence

| Data | Location |
|---|---|
| `environments/`, `endpoints/` | Bind mounts, editable on the host |
| `tokens/` (written only with the console disabled) | `portway_app` volume |
| Application and traffic logs | `./log` |
| `auth.db`, `metrics.db`, `mcp.db`, `portway.key` | `portway_app` volume |
| `appsettings.overrides.json` (settings saved in the console) | `portway_app` volume |

Endpoint and environment files are reloaded on change. Other configuration changes require `docker compose restart`.

The image contains the release files in `/opt/portway`. Each start copies them into the `portway_app` volume, so `docker compose pull` followed by `docker compose up -d` runs the new release. An existing `appsettings.json`, `endpoints/` and `environments/` in the volume are not changed.

An environment variable overrides the same setting in `appsettings.overrides.json`, and the console displays that setting as read-only. `appsettings.overrides.json` can be bind-mounted as a single file: create it with the content `{}` before the first start, because Docker creates a directory for a missing bind-mount source.

## TLS

The container serves plain HTTP. TLS terminates at a reverse proxy (nginx, Traefik, Caddy) or ingress. `PORTWAY_KNOWN_PROXIES` or `PORTWAY_KNOWN_NETWORKS` lists the proxy ([ForwardedHeaders](/reference/app-settings#forwardedheaders)).

`PORTWAY_USE_HTTPS=true` requires a Kestrel certificate (`Kestrel__Certificates__Default__Path`). Without a certificate, the container fails to start with `Hosting failed to start`.

## Backups

Back up the bind mounts and the `portway_app` volume. Portway writes a daily snapshot of its databases and configuration to `.backups/snapshots/` ([DatabaseMaintenance](/reference/app-settings#databasemaintenance)).

Next: [Record the access token](/guide/getting-started#2-record-the-access-token).
