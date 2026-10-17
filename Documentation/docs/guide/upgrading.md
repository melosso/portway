---
title: Upgrading Portway
description: "Replace application files and restore configuration to move to a new release"
---

# Upgrading Portway

An upgrade replaces the application files and keeps configuration and databases.

::: important Breaking changes
Before `v1.0.0`, releases can include breaking changes. Read the release notes before every upgrade.
:::

## Find your current version

Windows and Linux releases include `.version.txt` with the release version. Bug reports reference it.

## Steps

### 1. Read the release notes

The [GitHub release notes](https://github.com/melosso/portway/releases/) list breaking changes, migration steps and new configuration.

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

Linux package: install the newer `.deb` or `.rpm` package. Configuration file handling: [Package upgrades](/guide/deployment-linux#package-upgrades).

Linux archive and IIS: extract the release over the existing directory without overwriting `appsettings.json`, `environments/` and `endpoints/`.

Docker:

```sh
docker compose pull && docker compose up -d
```

### 5. Apply configuration changes

Apply the configuration changes from the release notes to `appsettings.json` and the environment files.

### 6. Start and verify

- `GET /health/live` returns `Alive`
- Endpoints respond in a test environment

## Version notes

### 0.10.0 to 0.10.1

Search the `entity.json` files in `endpoints/` for `ResponseTransforms`, `FallbackUrls` and `Retry`. If no file contains them, no action is needed.

1. **Endpoints with `ResponseTransforms`.** Check the clients of these endpoints. 0.10.0 ignored the rules. 0.10.1 removes, masks and renames the configured fields ([Proxy](/reference/entity-config#proxy)).
2. **Endpoints with `FallbackUrls` or `Retry`.** Add each fallback host to `environments/network-access-policy.json`. 0.10.0 ignored both keys. 0.10.1 makes `Retry.Attempts` attempts per URL (default `1`), then calls the fallback URLs in order. A fallback host outside the policy is skipped.

### 0.7 to 0.10

Versions 0.8 and 0.9 do not exist. Check each item:

1. **Generated API clients.** Regenerate them from `/docs/openapi.json`. Operation ids are route-based, e.g. `get_WMS_Bins`. Each endpoint has its own tag, e.g. `CRM/Accounts`. Previous document URLs redirect to `/docs/openapi.json` ([OpenAPI](/reference/openapi-settings)).
2. **Clients that read `401` or `403` bodies.** The body contains `success` and `error`. `availableEnvironments`, `requestedEnvironment`, `availableScopes` and `requestedEndpoint` are removed ([Error responses](/reference/api-auth#error-responses)).
3. **Endpoint folders named `v1`, `v2` and so on.** A folder with such a name inside an endpoint folder defines a version of the parent endpoint. A `v1` folder replaces the parent `entity.json`. Rename folders that are not versions, e.g. to `orders-v1` ([Versions](/reference/namespaces#versions)).
4. **Settings set by environment variables.** Environment variables override values saved in the console. The console displays these settings as read-only. Remove the variable to manage a setting in the console.
5. **The console.** The console is enabled by default. `PORTWAY_WEBUI_ENABLED=false` turns it off. `PORTWAY_PUBLIC_ORIGINS` allows external origins. An instance without accounts logs a one-time password for `admin-` plus eight characters at first start ([Console Accounts](/guide/accounts)).
6. **Metrics dashboards and alerts.** The `portway.endpoint` label contains the full endpoint name, e.g. `CRM/Accounts` or `Inventory/Products@v2` ([Telemetry](/guide/telemetry)).
7. **Namespace descriptions.** Move namespace text from `Documentation.TagDescription` to `NamespaceDescription`. `TagDescription` describes the endpoint tag ([OpenAPI tags](/reference/namespaces#openapi-tags)).
8. **Reverse proxies.** Add the proxy to `ForwardedHeaders:KnownProxies` (`PORTWAY_KNOWN_PROXIES`) or `KnownNetworks` (`PORTWAY_KNOWN_NETWORKS`). The OpenAPI server URL reads `X-Forwarded-Proto` from trusted proxies only ([Application settings](/reference/app-settings#forwardedheaders)).

No action is needed for these changes:

- The `WebUi` section is renamed to `Console`. `WebUi` keys and `WebUi__*` variables log a deprecation warning.
- Unprefixed environment variables, e.g. `Telemetry__Provider`, log a deprecation warning next to their `PORTWAY_*` names ([Environment variables](/reference/app-settings#environment-variables)).
- `PORTWAY_ADMIN_KEY` is deprecated. It enables the console and is not used for sign-in.
- The `GET:{endpoint}` scope form is removed. Existing tokens are unchanged.
- The first start adds columns to `auth.db` and `metrics.db`.
