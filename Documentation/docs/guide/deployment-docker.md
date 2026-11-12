---
title: Deploying with Docker
description: "Deploy Portway with Docker Compose, from a first container through to a production setup"
---

# Deploying with Docker

## Prerequisites

- [Docker Engine](https://www.docker.com/get-started) with Compose support, or Podman with the `podman-docker` package
- A reverse proxy for HTTPS, e.g. Nginx, Traefik or Caddy ([TLS](#tls))

## Installation

### 1. Set the encryption key

Docker Compose reads `PORTWAY_ENCRYPTION_KEY` from `.env` next to `docker-compose.yml`. Set the key before the first start:

::: code-group

```bash [Linux]
test -f .env || echo "PORTWAY_ENCRYPTION_KEY=$(openssl rand -base64 48)" > .env
chmod 600 .env
```

```powershell [Windows]
if (-not (Test-Path .env)) { $bytes = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes); "PORTWAY_ENCRYPTION_KEY=$([Convert]::ToBase64String($bytes))" | Out-File .env -Encoding ascii }
```

:::

The commands do not overwrite an existing `.env`. A new key makes existing encrypted settings unreadable.

### 2. Create the compose file

```yaml [docker-compose.yml]
services:
  portway:
    image: ghcr.io/melosso/portway:latest
    ports:
      - "8080:8080"
      - "127.0.0.1:8081:8081"
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

| Variable | Purpose |
|---|---|
| `PORTWAY_ENCRYPTION_KEY` | Encrypts secrets in environment settings. Required. |
| `PORTWAY_PUBLIC_ORIGINS` | Allows console access from these external origins. |
| `PORTWAY_SECURE_COOKIES` | Marks console cookies as secure. `true` requires TLS in front of the container. |

All `PORTWAY_*` variables: [Environment variables](/reference/app-settings#environment-variables).

### 3. Start the container

```bash
docker compose pull && docker compose up -d
```

### 4. Verify

The first start creates `tokens/`, `log/` and `auth.db`. Checks:

- `curl http://localhost:8080/health/live` returns `Alive`
- `http://localhost:8080/docs` serves the API reference
- `http://localhost:8081/_` serves the console sign-in

## Ports

| Port | Variable | Serves |
|---|---|---|
| `8080` | `PORTWAY_PORT_API` | API, `/docs`, `/mcp`, `/health`. `/_/*` returns `404` |
| `8081` | `PORTWAY_PORT_CONSOLE` | Console and every API path |

The compose file publishes the console port on `127.0.0.1`. A reverse proxy or a private network provides remote console access. If both variables are set to the same port: one listener serves the API and the console.

## Data persistence

| Data | Location |
|---|---|
| `environments/`, `endpoints/` | Bind mounts, editable on the host |
| `tokens/` (written when the console is disabled) | `portway_app` volume |
| Application and traffic logs | `./log` |
| `auth.db`, `metrics.db`, `mcp.db`, `portway.key` | `portway_app` volume |
| `appsettings.overrides.json` (settings saved in the console) | `portway_app` volume |

Portway reloads endpoint and environment files on change. Other configuration changes require `docker compose restart`.

An environment variable overrides the same setting in `appsettings.overrides.json`. The console displays that setting as read-only.

`appsettings.overrides.json` accepts a single-file bind mount. Create the file with the content `{}` before the first start: Docker creates a directory for a missing bind-mount source.

## Upgrades

```bash
docker compose pull && docker compose up -d
```

The image contains the release files in `/opt/portway`. Each start copies the release files into the `portway_app` volume. The copy does not change an existing `appsettings.json`, `endpoints/` or `environments/` in the volume.

Release notes and backup steps: [Upgrading Portway](/guide/upgrading).

## TLS

The container serves plain HTTP. TLS terminates at a reverse proxy (Nginx, Traefik, Caddy) or ingress. `PORTWAY_KNOWN_PROXIES` or `PORTWAY_KNOWN_NETWORKS` identifies the proxy ([ForwardedHeaders](/reference/app-settings#forwardedheaders)).

`PORTWAY_HTTPS_ENABLED=true` requires a Kestrel certificate (`Kestrel__Certificates__Default__Path`).

## Troubleshooting

Container log:

```bash
docker compose logs portway
```

| Error | Likely cause | Resolution |
|---|---|---|
| `PORTWAY_ENCRYPTION_KEY is not set` | Missing `.env` or missing key in `.env` | Set the key ([Installation](#1-set-the-encryption-key)) |
| `Hosting failed to start` | `PORTWAY_HTTPS_ENABLED=true` without a certificate | Set `Kestrel__Certificates__Default__Path` or remove `PORTWAY_HTTPS_ENABLED` ([TLS](#tls)) |
| `port is already allocated` | Host port 8080 or 8081 taken by another process | Change the host side of the `ports` mapping, e.g. `"9080:8080"` |

| Log | Location |
|---|---|
| Application | `./log/portwayapi-*.log` |
| Container | `docker compose logs portway` |

## Backup

Back up the bind mounts and the `portway_app` volume. Portway writes a daily snapshot of its databases and configuration to `.backups/snapshots/` ([DatabaseMaintenance](/reference/app-settings#databasemaintenance)). Files to back up: [Upgrading Portway](/guide/upgrading#2-back-up-the-installation).

Next: [Record the access token](/guide/getting-started#2-record-the-access-token).
