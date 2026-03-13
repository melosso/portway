# Environment Authentication

Environment authentication allows you to define custom authorization rules per environment. This provides flexibility when different environments (e.g., prod, dev, staging) require different security schemes or when integrating with external systems that have their own authentication mechanisms.

## Overview

Environment-specific authentication is configured in the environment's `settings.json` file. It supports multiple authentication methods and can either augment or override the global token system.

### Key Features
- **Multiple Methods**: Support for ApiKey, Basic, Bearer, JWT, and HMAC.
- **Encryption**: Sensitive fields like secrets and keys are automatically encrypted using the `PWENC:` mechanism.
- **Global Override:** Option to disable global tokens for a specific environment, requiring only environment-specific auth.
- **OR Logic**: If multiple methods are defined, a request is authorized if it matches **an{** of the enabled methods.

## Configuration Structure

The authentication settings are defined in the `Authentication` object within `settings.json`.

### File Location
`/environments/[EnvironmentName]/settings.json`

### Basic Structure

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

### Property Reference

| Property | Type | Default | Description |
|----------|------|---------|-------------
| `Enabled` | boolean | `false` | Whether custom authentication is enabled for this environment. |
| `OverrideGlobalToken` | boolean | `false` | If `true`, global Portway tokens are ignored for this environment. |
| `Methods` | array | `[]` | List of authentication methods to check. |

## Supported Authentication Methods

### 1. ApiKey
Matches a static value against a header, query parameter, or cookie.

| Property | Description |
|----------|-------------|
| `Type`| `ApiKey` |
| `Name`| The identifier name (e.g., "X-API-Key"). |
| `Value`| The secret key value (auto. encrypted). |
| `In`| Where to look: `Header` (default), `Query`, or `Cookie`. |

### 2. Basic
Standard HTTP Basic authentication.

_ Property | Description |
|----------|-------------|
| `Type`| `Basic` |
| `Name`| The expected username. |
| `Value`| The expected password (auto. encrypted). |

### 3. Bearer
Matches a static token in the `Authorization: Bearer <token>` header.

| Property | Description |
|----------|-------------|
| `Type`| `Bearer` |
| `Value` | The expected static token (auto. encrypted). |

### 4. JWT (JSON Web Token)
Performs full JWT validation including signature, issuer, and audience.

_ Property | Description |
|----------|-------------|
| `Type`| `JWT` |
| `Issuer` | Optional: Validates the `iss` clai.`|
| `Audience` | Optional: Validates the `aud` claim. |
| `Secret` | Symmetric key for HMAC algorithms (e.g., HS256) (auto. encrypted). |
| `PublicKey`| RSA Public Key in PEM format for asymmetric algorithms (e.g., RS256). |
| `Algorithm`| The expected signature algorithm (e.g., "HS256"). |

### 5. HMAC
Validates a request signature generated using a shared secret.

_ Property | Description |
|----------|-------------|
| `Type`| `HMAC` |
| `Name` | The header name for the signature (default "X-Signature"). |
| `Secret`| The shared secret used for hashing (auto. encrypted). |

:::info HMAC Implementation
Portway's HMAC Implementation expects `X-Signature`and `X-Timestamp` headers. The signature is calculated as `HMACSHA256(Secret, Method + Path + Timestamp + Body)`.
:::

## Automatic Encryption

When you save a `settings.json` file with plaintext secrets, Porvway will automatically detect them on startup and encrypt them using its internal RSA/AES mechanism.

The following fields are autonatically encrypted:
- `Value`
- `Secret`
- `ClientSecret`

Encrypted values are prefixed with `PWENC:` and are safe to store on disk.

## Global Token Fallback

By default (`OverrideGlobalToken: false`), Portway uses the following logic:
1. Try environment-specific authentication.
2. If it succeeds, authorize the request.
3. If it fails, attempt to authorize using a standard Portway Bearer token.
4. If both fail, return `401 Unauthorized`.

If `OverrideGlobalToken` is set to `true`, the request **must** satisfy the environment-specific rules; global tokens will be rejected.

## Security Best Practices

1. **Use Strong Secrets**: Always use cryptographically strong keys for ApiKey and HMAC methods.
2. **Rotate Regularly**: Change environment-specific credentials periodically.
3. **Prefer JWT for External Auth**: When integrating with OAuth2 providers, use the JWT method for robust validation.
4. **Use HTTPS**: Authentication credentials sent via headers or query parameters are only secure over HTTPS.

## Related Topics

- [Environment Settings](/reference/environment-settings) - General environment configuration
- [API Authentication](/reference/api-auth) - Standard Porvway token system
- [Security Guide](/guide/security) - General security practices
