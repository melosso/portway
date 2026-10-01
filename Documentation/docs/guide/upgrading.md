---
title: Upgrading Portway
description: "Replace application files and restore configuration to move to a new release"
---

# Upgrading Portway

An upgrade replaces the application files and keeps configuration and databases. Releases can change the application, configuration and database schema.

::: important Breaking changes
Before `v1.0.0`, releases can include breaking changes. Read the release notes before every upgrade.
:::

## Find your current version

The installed version is recorded in `.version.txt` in the deployment directory. Update it after an upgrade; bug reports reference it.

## Steps

### 1. Read the release notes

The [GitHub release notes](https://github.com/melosso/portway/releases/) list breaking changes, migration steps and new configuration.

### 2. Back up the installation

- `appsettings.json` and `appsettings.overrides.json`
- `auth.db`, `mcp.db` and `portway.key`
- `environments/`
- `endpoints/`
- `.core/`
- The file storage directory (`FileStorage:StorageDirectory`, default `storage/files`)

### 3. Stop the application

::: code-group

```powershell [IIS]
Stop-WebAppPool -Name "PortwayAppPool"
```

```sh [Docker]
docker compose down
```

:::

Stopping resets the in-memory cache and rate limit state.

### 4. Replace the application files

IIS: extract the release over the existing directory without overwriting `appsettings.json`, `environments/` and `endpoints/`.

Docker:

```sh
docker compose pull && docker compose up -d
```

### 5. Apply configuration changes

Apply the configuration changes from the release notes to `appsettings.json` and the environment files.

### 6. Start and verify

- `GET /health/live` returns `Alive`
- Endpoints respond in a test environment

:::tip
Validate major upgrades in a non-production environment first.
:::
