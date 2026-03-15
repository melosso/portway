# Environment Authentication

> Per-environment authentication methods that augment or replace the global Portway token system.

Environment-specific authentication is configured in each environment's `settings.json`. It supports ApiKey, Basic, Bearer, JWT, and HMAC methods. Sensitive fields are automatically encrypted to `PWENC:` format on next startup.

If multiple methods are defined, a request is authorised if it satisfies **any** of them.

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

| Property | Description |
|----------|-------------|
| `Type`| `Basic` |
| `Name`| The expected username. |
| `Value`| The expected password (auto-encrypted). |

### 3. Bearer
Matches a static token in the `Authorization: Bearer <token>` header.

| Property | Description |
|----------|-------------|
| `Type`| `Bearer` |
| `Value` | The expected static token (auto. encrypted). |

### 4. JWT (JSON Web Token)
Performs full JWT validation including signature, issuer, and audience.

| Property | Description |
|----------|-------------|
| `Type`| `JWT` |
| `Issuer` | Optional: Validates the `iss` claim. |
| `Audience` | Optional: Validates the `aud` claim. |
| `Secret` | Symmetric key for HMAC algorithms (e.g., HS256) (auto-encrypted). |
| `PublicKey`| RSA Public Key in PEM format for asymmetric algorithms (e.g., RS256). |
| `Algorithm`| The expected signature algorithm (e.g., `"HS256"`). |

### 5. HMAC
Validates a request signature generated using a shared secret.

| Property | Description |
|----------|-------------|
| `Type`| `HMAC` |
| `Name` | The header name for the signature (default `"X-Signature"`). |
| `Secret`| The shared secret used for hashing (auto-encrypted). |

:::info HMAC Implementation
Portway's HMAC Implementation expects `X-Signature`and `X-Timestamp` headers. The signature is calculated as `HMACSHA256(Secret, Method + Path + Timestamp + Body)`.
:::

## Automatic Encryption

When you save a `settings.json` file with plaintext secrets, Portway detects them on next startup and encrypts them using RSA/AES hybrid encryption.

The following fields are automatically encrypted:
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

## Security Notes

Use cryptographically strong keys for ApiKey and HMAC methods. Rotate credentials periodically. For OAuth2 provider integrations, prefer JWT for full signature validation. Authentication credentials sent via headers are only secure over HTTPS.

## Related Topics

- [Environment Settings](/reference/environment-settings) - General environment configuration
- [API Authentication](/reference/api-auth) - Standard Portway token system
- [Security Guide](/guide/security) - General security practices
