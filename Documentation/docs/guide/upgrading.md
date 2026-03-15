# Upgrading Portway

> Replace application files and restore configuration to move to a new release.

:::warning
Portway may include application and database changes between versions. Read the [release notes](https://github.com/melosso/portway/releases/) before upgrading, particularly for major version changes. Verify no breaking changes apply to your configuration before proceeding.
:::

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

Your installed version is recorded in `.version.txt` in the deployment directory. Update this file after upgrading to keep version information current — this is useful when submitting bug reports.
