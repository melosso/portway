---
title: Single sign-on
description: "Sign in to the Portway console through an OpenID Connect provider such as Authelia, Authentik, Pocket ID, or Keycloak"
---

# Single sign-on

Console accounts can sign in through an OpenID Connect provider with a discovery document (e.g. Authelia, Authentik, Pocket ID, Keycloak), in addition to password sign-in.

## Adding a provider

Providers are added under **Users → Sign-in providers → Add provider**.

| Field | Notes |
|---|---|
| Key | Lowercase letters, numbers and hyphens, up to 32 characters; part of the redirect URI |
| Name | Sign-in button label; can change without affecting the redirect URI |
| Issuer URL | Absolute `https` URL; discovery is read from `{issuer}/.well-known/openid-configuration`. `http` only on loopback |
| Client ID and secret | The secret is write-only; empty for a public client |
| Scopes | Default `openid profile email` |
| Username claim | Default `preferred_username` |
| Email claim | Default `email` |
| Enabled | Off hides the button and refuses the callback |
| Create accounts | Creates an account for an unmatched identity, with the configured role |

## Redirect URI

```
https://your-host/ui/api/auth/oidc/{key}/callback
```

The console provider list shows the exact URI per key, including the path base.

## How an identity finds its account

Matching order at sign-in:

1. An account bound to this provider by subject.
2. An account whose email equals the email claim, if the provider marks the address verified. Password accounts and accounts of this provider are eligible.

The username claim never matches an existing account. A match in step 2 is bound to the subject.

Without a match and with **Create accounts** off, the sign-in is refused and the subject is logged with the reason. The subject can be linked under **Users**.

Created accounts receive the provider's role; use `viewer` when the whole directory can reach the provider. Roles: [Account roles](/guide/accounts#account-roles).

## Linking your own account

Password accounts, viewers included, can bind a provider identity to their own account under **Users**.

::: warning Removing a provider
Deleting a provider unbinds its accounts; accounts without a password can no longer sign in. Set passwords first; the console reports the affected count.
:::

## Turning it off

Setting `Oidc:Enabled: false` (**Settings → Security → Deployment & Access**) disables all providers: no provider buttons, `404` on the start route, and callbacks redirect to the sign-in page with an error. The setting is read per request; provider records are kept.

