---
title: Upgrading Portway
description: "Replace application files and restore configuration to move to a new release"
---

# Upgrading Portway

An upgrade replaces the application files. Configuration and databases are not changed.

::: warning Breaking changes
Before `v1.0.0`, releases can include breaking changes. Read the release notes before every upgrade.
:::

## Find your current version

Windows and Linux releases include `.version.txt` with the release version. Bug reports reference it.

## Steps

### 1. Read the release notes

The [GitHub release notes](https://github.com/melosso/portway/releases/) describe breaking changes, migration steps and new configuration.

### 2. Back up the installation

- `appsettings.json` and `appsettings.overrides.json`
- `auth.db`, `mcp.db` and `portway.key`
- `.env` on Linux, which contains `PORTWAY_ENCRYPTION_KEY`
- `environments/`
- `endpoints/`
- `.core/`
- The file storage directory (`FileStorage:StorageDirectory`, default `storage/files`)

### 3. Stop the application

::: code-group

```bash [Linux]
sudo systemctl stop portway
```

```powershell [Windows]
Stop-WebAppPool -Name "PortwayAppPool"
```

```sh [Docker]
docker compose down
```

:::

Stopping resets the in-memory cache and rate limit state.

### 4. Replace the application files

Linux package: install the newer `.deb` or `.rpm` package. Configuration file updates: [Package upgrades](/guide/deployment-linux#package-upgrades).

Linux archive and IIS: extract the release over the existing directory without overwriting `appsettings.json`, `environments/` and `endpoints/`.

Docker:

```sh
docker compose pull && docker compose up -d
```

### 5. Apply configuration changes

Apply the configuration changes from the release notes to `appsettings.json` and the environment files.

### To v0.13.0

- Portway v0.13.0 requires the .NET 11 runtime (GA). Install it before the upgrade on Windows and Linux archive installs. Docker images and Linux packages include it.
- Configuration names deprecated before v0.13.0 are not read. Startup logs one error per former name with its current name: [Removed keys](/reference/app-settings#removed-keys). `WebUi:Enabled: false` does not turn the console off. Use `Console:Enabled` or `PORTWAY_WEBUI_ENABLED`.
- A webhook endpoint with an empty or absent `AllowedColumns` does not load and returns `404` for every id.
- An environment authentication method without `AllowedEndpoints` has access to every endpoint of its environment and logs a startup warning. See [Environment authentication](/reference/environment-auth).
- If an Azure Key Vault lookup fails: Portway uses the last known value for at most 1 hour. Values in `settings.json` are not a fallback. See [Azure Key Vault](/reference/secrets#azure-key-vault).
- A v0.12.0 install removes `Secrets` when it rewrites `settings.json`. Back up `settings.json` before a downgrade.

### 6. Start and verify

- `GET /health/live` returns `Alive`
- Endpoints respond in a test environment

