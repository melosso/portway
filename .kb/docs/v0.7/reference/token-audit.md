---
title: Token Audit Log
description: "TokenAudits table in auth.db and the first-run token file"
---

# Token Audit Log

Token operations are recorded in the `TokenAudits` table of `auth.db`. The console shows them per token under **Audit Log** in the token's edit drawer.

## Schema

| Column | Type | Description |
|---|---|---|
| `Id` | integer | Entry id |
| `TokenId` | integer | Token id |
| `Username` | string | Token name |
| `Operation` | string | See below |
| `OldTokenHash` | string | Previous hash, on revoke |
| `NewTokenHash` | string | New hash, on create |
| `Timestamp` | datetime | UTC |
| `Details` | string | JSON with the changed values |
| `Source` | string | `PortwayApi` |
| `IpAddress` | string | Not recorded |
| `UserAgent` | string | Machine and process user |

| Operation | Recorded on |
|---|---|
| `Created` | Create, and the new token of a rotation |
| `Revoked` | Archive, and the old token of a rotation |
| `Unarchived` | Restore |
| `ScopesUpdated` | Endpoint scope change |
| `EnvironmentsUpdated` | Environment change |
| `TenantsUpdated` | Tenant change |
| `RateLimitUpdated` | Rate limit change |
| `DescriptionUpdated` | Description change |

## Queries

```sql
SELECT * FROM TokenAudits WHERE Username = 'api-service' ORDER BY Timestamp DESC;

SELECT Username, Operation, Timestamp FROM TokenAudits
WHERE Timestamp > datetime('now', '-1 day')
ORDER BY Timestamp DESC;
```

## First-run token file

The first start creates a token named after the machine and saves it to `tokens/{MACHINE_NAME}.txt`:

```json
{
  "Username": "SERVER-NAME",
  "Token": "...",
  "AllowedScopes": "*",
  "AllowedEnvironments": "*",
  "AllowedTenants": {},
  "ExpiresAt": "Never",
  "CreatedAt": "2026-01-01 00:00:00"
}
```

Copy the token value and delete the file.

## Related topics

- [Tokens](/guide/tokens)
- [Authentication](/reference/api-auth)
- [Security](/guide/security)
