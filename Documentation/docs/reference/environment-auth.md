---
title: Environment Authentication
description: "Per-environment authentication methods that augment or replace the global Portway token system"
---

# Environment Authentication

An environment's `settings.json` can define ApiKey, Basic, Bearer, JWT and HMAC authentication in addition to, or instead of, Portway tokens. A request is authorized when it satisfies any defined method. Plaintext secrets are encrypted (`PWENC:`) at the next start.

## Configuration structure

The `Authentication` object in `/environments/[EnvironmentName]/settings.json`:

### Structure

```json
{
  "Authentication": {
    "Enabled": true,
    "OverrideGlobalToken": false,
    "Methods": [
      {
        "Type": "ApiKey",
        "Name": "X-API-Key",
        "Value": "your-secret-key",
        "In": "Header"
      }
    ]
  }
}
```

### Property reference

| Property | Type | Default | Description |
|----------|------|---------|-------------
| `Enabled` | boolean | `false` | Enables the methods for this environment |
| `OverrideGlobalToken` | boolean | `false` | `true` rejects Portway tokens in this environment |
| `Methods` | array | `[]` | Authentication methods |

## Supported authentication methods

### ApiKey

Static value in a header, query parameter or cookie.

| Property | Description |
|----------|-------------|
| `Type`| `ApiKey` |
| `Name`| Header, parameter or cookie name (e.g. `X-API-Key`) |
| `Value`| Key value (encrypted at rest) |
| `In`| `Header` (default), `Query` or `Cookie` |

### Basic

HTTP Basic authentication.

| Property | Description |
|----------|-------------|
| `Type`| `Basic` |
| `Name`| Username |
| `Value`| Password (encrypted at rest) |

### Bearer

Static token in `Authorization: Bearer <token>`.

| Property | Description |
|----------|-------------|
| `Type`| `Bearer` |
| `Value` | Token (encrypted at rest) |

### JWT

JWT validation of signature, issuer and audience.

| Property | Description |
|----------|-------------|
| `Type`| `JWT` |
| `Issuer` | Expected `iss` claim (optional) |
| `Audience` | Expected `aud` claim (optional) |
| `Secret` | Symmetric key for HMAC algorithms such as HS256 (encrypted at rest) |
| `PublicKey`| RSA public key in PEM format for asymmetric algorithms such as RS256 |
| `Algorithm`| Expected signature algorithm, e.g. `HS256` |

### HMAC

Request signature with a shared secret.

| Property | Description |
|----------|-------------|
| `Type`| `HMAC` |
| `Name` | Signature header (default `X-Signature`) |
| `Secret`| Shared secret (encrypted at rest) |

:::info HMAC Implementation
Requests send `X-Signature` and `X-Timestamp`. Signature: `HMACSHA256(Secret, Method + Path + Timestamp + Body)`.
:::

## Automatic encryption

Plaintext `Value`, `Secret` and `ClientSecret` fields are encrypted at the next start with RSA/AES hybrid encryption and stored with the `PWENC:` prefix.

## Global token fallback

With `OverrideGlobalToken: false`:

1. Environment methods are checked; a match authorizes the request.
2. Otherwise the Portway Bearer token is checked.
3. Without either, the response is `401`.

With `OverrideGlobalToken: true`, only the environment methods are accepted.

Requests authorized by environment methods have no Portway token and are refused on endpoints with [tenant headers](/guide/tenant-headers).

## Security notes

Use random keys for ApiKey and HMAC and rotate them. JWT validates signatures for OAuth2 providers. Header credentials require HTTPS.

## Related topics

- [Environment Settings](/reference/environment-settings)
- [API Authentication](/reference/api-auth)
- [Security Guide](/guide/security)
