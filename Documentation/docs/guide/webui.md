---
title: Web UI
description: "Browser console for endpoints, environments, tokens, users, settings and logs"
---

# Web UI

The console at `/ui` manages endpoints, environments, tokens, users and settings, and displays the dashboard and application logs. It writes the same configuration files the gateway reads. The gateway API runs without it.

## Access

```yaml
environment:
  - WebUi__Enabled=true
  - WebUi__PublicOrigins__0=https://example.com
  - PORTWAY_SECURE_COOKIES=true
```

Setting `WebUi__Enabled=true` enables the console. It is reachable from the local network only; `WebUi__PublicOrigins` allows external origins; `PORTWAY_SECURE_COOKIES` restricts cookies to HTTPS. All `WebUi` settings: [Application settings](/reference/app-settings#webui).

## Accounts

The first start creates an administrator account with a random one-time password, logged once and changed at first sign-in; its name is logged with it. `WebUi__SeedPassword` sets a fixed password for demo instances. Further accounts are created under **Users** or from the shell:

```bash
portway accounts create <username> <password>
portway accounts password <username> <new-password>
```

Shell recovery for bare-metal and Docker installs: [Recovering an account](/guide/accounts#recovering-an-account). Roles: [Account roles](/guide/accounts#account-roles). OpenID Connect sign-in: [Single sign-on](/guide/sso).

## Sessions

Session cookies are HMAC-SHA256 signed with `portway.key` (next to `auth.db`) and expire after 12 hours. Deleting `portway.key` ends all sessions.

## Settings changes

Settings saved in the console are written to `appsettings.overrides.json`, layered over `appsettings.json`. A save applies all submitted keys or none, and the response states whether a restart is required.

The section **Settings → Security → Deployment & Access** sets trusted proxies, trusted proxy networks and public console origins. A change that would exclude the current request returns `400`.

Every console change to environments, endpoints and MCP settings is recorded in the audit trail, and the previous file version is backed up. Both are listed under **Settings → Security → Recent Configuration Changes**, with restore.

## API

The console API under `/ui/api` uses the `portway_auth` session cookie and a CSRF header on writes: [Console API](/reference/console-api).
