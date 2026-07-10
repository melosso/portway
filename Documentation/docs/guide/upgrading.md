---
title: Upgrading Portway
description: "Replace application files and restore configuration to move to a new release"
---

# Upgrading Portway

Upgrading Portway is usually a matter of replacing the binaries and letting it start back up, but a little preparation goes a long way. Releases may include application and database changes, so it is recommended to read the [release notes](https://github.com/melosso/portway/releases/) first, particularly for major versions, and to check that no breaking changes apply to your configuration.

## Moving to `v2.0.0`

Version `2.0.0` is a major release, so it is worth a moment of planning before you upgrade. Two things in particular are good to know:

- **It runs on .NET 11.** The [.NET 11 Hosting Bundle](/guide/getting-started#prerequisites) is needed on the server. Since .NET 11 is currently a preview of the framework, you may reasonably prefer to keep a .NET 10 LTS deployment for production until .NET 11 reaches general availability.
- **Webhook routes are now namespaced.** The older flat route `POST /api/{env}/webhook/{id}` has been retired in favour of `POST /api/{env}/{namespace}/{name}/{id}`. If you have callers on the old path, please point them at the new shape; the previous route now answers with `410 Gone` so nothing fails silently.

Everything else, including your existing endpoints and environments, carries over unchanged. The generated API reference also moves to OpenAPI 3.2, which you can read more about in [OpenAPI Documentation Settings](/reference/openapi-settings).

## Steps

**1. Read the release notes**

Review the [GitHub release notes](https://github.com/melosso/portway/releases/) for migration steps, breaking changes, and new configuration requirements.

**2. Back up your installation**

Copy these files and directories to a safe location before making any changes:

- `appsettings.json`
- `auth.db`
- `environments/`
- `endpoints/`
- `.core/`

**3. Stop the application**

*IIS:*
```powershell
Stop-WebAppPool -Name "PortwayAppPool"
```

*Docker:*
```sh
docker compose down
```

:::info
Stopping the IIS Application Pool resets in-memory cache and rate limit state. This is expected behaviour.
:::

**4. Replace application files**

Extract the new release over your existing directory, replacing application files. Do not overwrite your configuration files (`appsettings.json`, `environments/`, `endpoints/`).

*Docker:*
```sh
docker compose pull && docker compose up -d
```

**5. Restore configuration**

If the release notes require configuration changes (new fields, renamed settings), apply them to your `appsettings.json` and environment files now.

**6. Start and verify**

Start the application pool or container and confirm:
- `GET /health/live` returns `Alive`
- Endpoints respond as expected in your test environment

:::tip
For major version upgrades, validate in a non-production environment before upgrading production.
:::

## Find your current version

Your installed version is recorded in `.version.txt` in the deployment directory. Update this file after upgrading to keep version information current, this is useful when submitting bug reports.
