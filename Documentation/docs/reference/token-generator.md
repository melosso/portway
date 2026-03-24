# Token Audit Log

> Schema and queries for the token operations audit trail stored in `auth.db`.

All token management operations, e.g. create, rotate, revoke, update, are recorded automatically in the `TokenAudits` table of `auth.db`.

## Audit Schema

| Field | Type | Description |
|-------|------|-------------|
| `Id` | integer | Unique audit entry ID |
| `TokenId` | integer | Associated token ID |
| `Username` | string | Token owner |
| `Operation` | string | `Created`, `Rotated`, `Revoked`, `Updated` |
| `OldTokenHash` | string | Previous hash (rotation only) |
| `NewTokenHash` | string | New hash (rotation only) |
| `Timestamp` | datetime | UTC timestamp |
| `Details` | string | JSON metadata |
| `Source` | string | Origin: `PortwayApi` |
| `IpAddress` | string | Client IP (when available) |
| `UserAgent` | string | Client user agent (when available) |

The Web UI exposes per-token audit history at `GET /ui/api/tokens/{id}/audit`. See the [Web UI API reference](/reference/webui).

## Direct Queries

```sql
-- All operations for a token owner
SELECT * FROM TokenAudits WHERE Username = 'api-service' ORDER BY Timestamp DESC;

-- Recent rotations
SELECT Username, Operation, Timestamp FROM TokenAudits
WHERE Operation = 'Rotated' AND Timestamp > datetime('now', '-1 day')
ORDER BY Timestamp DESC;

-- Last 24 hours of activity
SELECT * FROM TokenAudits
WHERE Timestamp > datetime('now', '-1 day')
ORDER BY Timestamp DESC;
```

## First-run Token File

On first run, Portway writes the initial token to `tokens/{SERVER_NAME}.txt`:

```json
{
  "Username": "SERVER-NAME",
  "Token": "base64-encoded-secure-token",
  "AllowedScopes": "*",
  "AllowedEnvironments": "*",
  "ExpiresAt": "Never",
  "CreatedAt": "2024-01-01 00:00:00"
}
```

Record the token value, delete the file, and use the Web UI to create all subsequent tokens.

## Related Topics

- [Web UI guide](/guide/webui): browser-based token management
- [Authentication reference](/reference/api-auth): token properties and scope patterns
- [Security guide](/guide/security): incident response and token rotation
