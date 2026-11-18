---
title: Accounts
description: "Console account roles, shell recovery and session keys"
---

# Accounts

Accounts sign in to the [console](/guide/webui) at `/_`.

## First account

Console accounts are stored in `auth.db` with PBKDF2-SHA256 password hashes. The first start without accounts creates an administrator with a random one-time password, logged once and changed at first sign-in. The account name is `admin-` plus eight random characters, or `admin` when `Console:SeedPassword` or the legacy `Console:AdminApiKey` (`PORTWAY_ADMIN_KEY`) is set. `Console:SeedPassword` sets a fixed password without a forced change. Use it for demo instances.

The legacy `Console:AdminApiKey` is not used for sign-in. It enables the console like `Console:Enabled` and can be removed, also from **Settings → Security → Deployment & Access**.

## Users page

**Users** displays console accounts. The table sorts by name, role, provider, status and last sign-in. The account menu displays the e-mail address of the signed-in account.

## Account roles

| Role | Access |
|---|---|
| `administrator` | All console settings, endpoints, environments, tokens and accounts |
| `viewer` | Read access; own password and own [single sign-on](/guide/sso) link. Other writes return `403` |

The role is read from `auth.db` on every request, not from the session cookie; a demotion applies to the next request.

::: warning Upgrades
Earlier builds did not enforce the `viewer` role. Review the accounts under **Users** after upgrading.
:::

## Sessions

Session cookies are HMAC-SHA256 signed with `portway.key`, created next to `auth.db`, and expire after 12 hours. Deleting `portway.key` ends all sessions.

## Recovering an account

Account recovery runs from the shell, in the directory that contains `auth.db`:

::: code-group

```bash [Linux]
./PortwayApi accounts list
./PortwayApi accounts password <username> <new-password>
./PortwayApi accounts create <username> <password> [administrator|viewer]
```

```powershell [Windows]
.\PortwayApi.exe accounts list
.\PortwayApi.exe accounts password <username> <new-password>
.\PortwayApi.exe accounts create <username> <password> [administrator|viewer]
```

```bash [Docker]
docker exec <container> dotnet /app/PortwayApi.dll accounts list
docker exec <container> dotnet /app/PortwayApi.dll accounts password <username> <new-password>
docker exec <container> dotnet /app/PortwayApi.dll accounts create <username> <password> [administrator|viewer]
```

:::

Further subcommands: `promote`, `demote`, `enable`, `disable`, `delete`. A command that would leave no active administrator is refused.
