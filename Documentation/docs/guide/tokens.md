---
title: Access Tokens
description: "Create, scope, rotate, and archive the Bearer tokens that control API access"
---

# Access Tokens

API requests authenticate with a Bearer token ([RFC 6750](https://datatracker.ietf.org/doc/html/rfc6750)). Tokens are managed in the console under **Access Tokens**.

:::warning
The first start writes a full-access token (`*` scopes, `*` environments) to `tokens/{SERVER_NAME}.txt`. Delete the file after recording the value.
:::

## Creating a token

The create drawer opens from **Access Tokens → New Token**:

| Field | Required | Description |
|---|---|---|
| Name | Yes | Service or user the token belongs to; recorded in audit and traffic logs |
| Description | No | Purpose, e.g. "ERP sync service" |
| Expiration (days) | No | Empty for no expiry |
| Rate limit | No | Requests per window for this token; empty uses the global limit |
| Scopes | No | Endpoint restriction (default `*`) |
| Environments | No | Environment restriction (default `*`) |
| Tenants | No | Allowed values per [tenant header](/guide/security#tenant-headers) |

The token value is displayed once, after **Create**.

## Scoping tokens

A token reaches an endpoint when the endpoint matches its scopes and the environment matches both its environments and the endpoint's `AllowedEnvironments`.

### Endpoint scopes

| Pattern | Access |
|---|---|
| `*` | All endpoints |
| `Products` | Endpoint `Products` |
| `Products,Orders` | Both endpoints |
| `Product*` | Endpoints starting with `Product` |
| `Company/Employees` | Namespaced endpoint |
| `Company/*` | All endpoints in the `Company` namespace |
| `GET:Products` | `Products`, GET only |

### Environment scopes

| Pattern | Access |
|---|---|
| `*` | All environments |
| `prod` | Environment `prod` |
| `dev,test` | Both environments |
| `dev*` | Environments starting with `dev` |

### Tenant scopes

The `AllowedTenants` field maps each tenant header to its permitted values, e.g. `{"X-Company-Id": ["ACME", "GLOBEX"]}`. The token drawer lists one row per header with comma-separated values. With one value per header the request header is optional; with several values it is required. Endpoint configuration: [Tenant headers](/guide/security#tenant-headers).

### Common configurations

| Scenario | Scopes | Environments |
|---|---|---|
| Full access | `*` | `*` |
| Read-only reporting | `GET:*` | `prod` |
| Single integration (Globe+) | `Company/*` | `500,700` |
| Development | `*` | `dev,test` |
| Webhook ingestion | `webhooks/*` | `*` |

## Rotating a token

Rotation issues a new token value with the same name, scopes, environments, tenants and rate limit, and revokes the old value. Expiration keeps the remaining days. Rotate on a schedule and after any exposure.

## Archiving a token

Archiving revokes a token; requests with it return `401`, and its plaintext token file is deleted. Archived tokens are listed with **Show archived** and can be restored. The last token with full access cannot be archived or narrowed.

## Token audit log

Each token records its operations (created, scopes, environments and tenants changed, rotated, archived). The log opens from the token's edit drawer under **Audit Log**. API: `GET /ui/api/tokens/{id}/audit` ([Web UI API Reference](/reference/webui)).

## Related

- [Authentication reference](/reference/api-auth)
- [Security](/guide/security)
- [Web UI](/guide/webui)
