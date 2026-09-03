---
title: Single sign-on
description: "Sign in to the Portway console through an OpenID Connect provider such as Authelia, Authentik, Pocket ID, or Keycloak"
---

# Single sign-on

Console accounts can sign in through any OpenID Connect provider that publishes a discovery document, such as Authelia, Authentik, Pocket ID, or Keycloak. Password sign-in keeps working alongside it.

## Adding a provider

Open the **Users** page and choose **Add provider** under **Sign-in providers**.

| Field | Notes |
|---|---|
| Key | Lowercase letters, numbers and hyphens, up to 32 characters. It appears in the redirect URI. The name below can change without breaking the registration at your provider |
| Name | The label on the sign-in button |
| Issuer URL | An absolute `https` URL. Discovery is read from `{issuer}/.well-known/openid-configuration`. Plain `http` is accepted only for a provider on loopback |
| Client ID and secret | The secret is stored write-only: the console reports only whether one is set. Leave it empty to register a public client |
| Scopes | `openid profile email` by default |
| Username claim | `preferred_username` by default. Authelia, Authentik and Pocket ID all send it |
| Email claim | `email` by default |
| Enabled | Off keeps the provider configured but hides its button and refuses its callback |
| Create accounts | Whether an identity with no matching account gets one, and which role it receives |

## Redirect URI

Register this address at your provider:

```
https://your-host/ui/api/auth/oidc/{key}/callback
```

The provider list in the console shows the exact value for each key, including any path base you configured.

## How an identity finds its account

On each sign-in Portway looks for a match in this order:

1. An account already bound to this provider by subject.
2. An account whose username equals the username claim.
3. An account whose email equals the email claim, and only when the provider marked that address verified. An unverified address is skipped, because anyone who can set the claim could otherwise take over the account it names.

Steps 2 and 3 only consider accounts that use password sign-in, or that already belong to this provider. An account bound to a different provider is never a candidate. The first match is bound to the subject, and later sign-ins match at step 1.

When nothing matches and **Create accounts** is off, Portway refuses the sign-in and logs the subject that tried, with the reason the username or address did not match. Link that subject from the **Users** page, or turn account creation on for the provider.

A created account holds the role set on the provider. Pick `viewer` when everyone in your directory can reach that provider. See [Account roles](/guide/security#account-roles).

## Linking your own account

If you already sign in with a password, open the **Users** page and bind a provider identity to your own account. A viewer can do this for its own account.

::: warning Removing a provider
Deleting a provider unbinds every account that used it, so any account with no password loses its only way in. The console counts those accounts and tells you after the deletion, so set a password on them first.
:::

## Turning it off

`Oidc:Enabled` turns off every provider at once, under **Settings → Security → Feature Toggles**. With it off, the sign-in page shows no providers, the start route returns `404`, and a callback redirects back to the sign-in page with an error. This applies to every provider, including one whose own **Enabled** flag is set. Portway reads it on each request, so a change applies without a restart, and the provider records stay as they are.

