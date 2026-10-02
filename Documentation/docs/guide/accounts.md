---
title: Console Accounts
description: "Console account roles, shell recovery and session keys"
---

# Console Accounts

Console accounts sign in to the [Web UI](/guide/webui).

## First account

Console accounts are stored in `auth.db` with PBKDF2-SHA256 password hashes. The first start without accounts creates an administrator with a random one-time password, logged once and changed at first sign-in. The account name is `admin-` plus eight random characters, or `admin` when `WebUi:SeedPassword` or the legacy `WebUi:AdminApiKey` (`PORTWAY_ADMIN_KEY`) is set. `WebUi:SeedPassword` sets a fixed password without a forced change, for demo instances only.

The legacy `WebUi:AdminApiKey` is not used for sign-in. It enables the console like `WebUi:Enabled` and can be removed, also from **Settings → Security → Deployment & Access**.

## Account roles

| Role | Access |
|---|---|
| `administrator` | All console settings, endpoints, environments, tokens and accounts |
| `viewer` | Read access; own password and own [single sign-on](/guide/sso) link. Other writes return `403` |

The role is read from `auth.db` on every request, not from the session cookie; a demotion applies to the next request.

::: warning Upgrades
Earlier builds did not enforce the `viewer` role. Review the accounts under **Users** after upgrading.
:::

Sessions are signed with `portway.key`, created next to `auth.db`. Deleting it ends all sessions.

## Recovering an account

Account recovery runs from the shell, in the directory that contains `auth.db`:

```bash
portway accounts list
portway accounts password <username> <new-password>
portway accounts create <username> <password> [administrator|viewer]
```

Docker (the image has no `portway` binary):

```bash
docker exec <container> dotnet /app/PortwayApi.dll accounts list
docker exec <container> dotnet /app/PortwayApi.dll accounts password <username> <new-password>
docker exec <container> dotnet /app/PortwayApi.dll accounts create <username> <password> [administrator|viewer]
```

Further subcommands: `promote`, `demote`, `enable`, `disable`, `delete`. A command that would leave no active administrator is refused.

## Related topics

- [Web UI](/guide/webui)
- [Single Sign-On](/guide/sso)
- [Security](/guide/security)
