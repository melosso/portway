---
title: Access Tokens
description: "Create, scope, rotate, and revoke the Bearer tokens that control API access"
---

# Access Tokens

Portway is our API gateway that uses Bearer tokens for authentication, implementing the standard defined in [RFC 6750](https://datatracker.ietf.org/doc/html/rfc6750).

Every request to the server must carry a Bearer token, so token management is a place you'll visit regularly. Tokens live in the Web UI under **Access Tokens**.

:::warning
On first startup, Portway writes an initial token to `tokens/{SERVER_NAME}.txt`. This token has full access (`*` scopes, `*` environments). Delete the file from disk immediately after recording the value, then use the Web UI for all subsequent token management.
:::

## Creating a token

1. Open the Web UI and navigate to **Access Tokens**
2. Click **New Token**
3. Fill in the fields:

| Field | Required | Description |
|-------|----------|-------------|
| `Username` | Yes | Identifies the service or user this token belongs to. Used for audit trail and logging. |
| `Description` | No | Human-readable purpose note (e.g., "ERP sync service") |
| `Allowed Scopes` | No | Endpoint access restriction. Default `*` = all endpoints. |
| `Allowed Environments` | No | Environment access restriction. Default `*` = all environments. |
| `Expires In (days)` | No | Leave blank for a non-expiring token. |

4. Click **Create** and copy the token value immediately.

:::warning
Save the token value shown when you first launch Portway or when creating a new token. It is only displayed once and cannot be retrieved later.
:::


## Scoping tokens

Narrow a token's access to reduce exposure. A token can only access endpoints and environments that match both its scope and the endpoint's own `AllowedEnvironments` / `Hidden` settings.

### Endpoint scopes

| Pattern | Grants access to |
|---------|-----------------|
| `*` | All endpoints |
| `Products` | Single endpoint named `Products` |
| `Products,Orders` | Two named endpoints |
| `Product*` | All endpoints with a `Product` prefix |
| `Company/Employees` | Specific namespaced endpoint |
| `Company/*` | All endpoints under the `Company` namespace |
| `GET:Products` | `Products` endpoint, GET method only |

### Environment scopes

| Pattern | Grants access to |
|---------|-----------------|
| `*` | All environments |
| `prod` | Single environment |
| `dev,test` | Two named environments |
| `dev*` | All environments with a `dev` prefix |

### Common configurations

| Scenario | Scopes | Environments |
|----------|--------|--------------|
| Full-access admin token | `*` | `*` |
| Read-only reporting service | `GET:*` | `prod` |
| Single integration (Globe+) | `Company/*` | `500,700` |
| Development testing | `*` | `dev,test` |
| Webhook ingestion only | `webhooks/*` | `*` |

## Rotating a token

Rotation replaces an existing token with a new one while keeping the username, scopes, and environments intact. The old token is revoked immediately.

1. Open **Access Tokens** and find the token to rotate
2. Click **Rotate**
3. Copy the new token value
4. Update every application or service using the old token

Use rotation on a schedule (e.g., quarterly) or immediately when a token may have been exposed.

## Revoking a token

Revocation permanently invalidates a token. Any request using it will immediately receive `401 Unauthorized`.

1. Open **Access Tokens** and find the token
2. Click **Revoke**
3. Confirm the action

Revocation is irreversible. Create a new token for any user or service that was using the revoked one.

## Token audit log

Every token records a history of operations (created, used, rotated, revoked), accessible from the **Access Tokens** page by clicking **Audit** on a token row. This log is also queryable directly:

```http
GET /ui/api/tokens/{id}/audit
```

See [Token Audit Log](/reference/token-generator) for the schema.

## Related

- [Authentication reference](/reference/api-auth): validation flow, error responses, scope syntax
- [Security guide](/guide/security): incident response, network restrictions, encryption
- [Web UI](/guide/webui): enabling and configuring the management interface
