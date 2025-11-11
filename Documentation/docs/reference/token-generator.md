# Portway Management Console

The Portway Management Console is a command-line utility for creating and managing authentication tokens for the Portway API. It provides fine-grained control over token permissions, including endpoint access and environment restrictions.

## Usage Modes

### Interactive Mode

Run without parameters to enter interactive mode:

```bash
PortwayMgt.exe
```

Interactive menu options:
1. List all existing tokens
2. Generate new token  
3. Revoke token
4. Update token scopes
5. Update token environments
6. Update token expiration
7. Rotate token
8. Update passphrase
0. Exit

### Command Line Mode

Generate tokens directly with parameters:

```bash
# Basic token generation
PortwayMgt.exe username

# Token with specific endpoint scopes
PortwayMgt.exe username -s "Products,Orders,Customers"

# Token with specific namespace scopes
PortwayMgt.exe hr-system -s "Company/*"

# Token with environment restrictions
PortwayMgt.exe username -e "prod,dev"

# Token with expiration (in days)
PortwayMgt.exe username --expires 90

# Combined parameters
PortwayMgt.exe username -s "Products,Orders" -e "prod" --expires 30 --description "Frontend API access"
```

**Note**: Token rotation is only available through interactive mode (menu option 7).

## Command Line Parameters

| Parameter | Description | Example |
|-----------|-------------|---------|
| `-h, --help` | Show help message | `--help` |
| `-d, --database` | Path to auth.db file | `-d "C:\path\to\auth.db"` |
| `-t, --tokens` | Token files directory | `-t "C:\path\to\tokens"` |
| `-s, --scopes` | Allowed endpoints (comma-separated) | `-s "Products,Orders"` |
| `-e, --environments` | Allowed environments | `-e "prod,dev,Synergy"` |
| `--description` | Token description | `--description "API access"` |
| `--expires` | Expiration in days | `--expires 90` |

## Token Scopes

### Scope Formats

| Format | Description | Example |
|--------|-------------|---------|
| `*` | Full access to all endpoints | `*` |
| `Endpoint1,Endpoint2` | Specific endpoints only | `Products,Orders` |
| `Prefix*` | All endpoints with prefix | `Product*` |

### Examples

```bash
# Full access
PortwayMgt.exe admin -s "*"

# Specific endpoints
PortwayMgt.exe frontend -s "Products,Orders,Customer/*"

# Pattern matching
PortwayMgt.exe reporting -s "Report*,Export*"

# Specific service access
PortwayMgt.exe frontend -s "Products,Orders" --description "Frontend service access"
```

## Environment Restrictions

### Environment Formats

| Format | Description | Example |
|--------|-------------|---------|
| `*` | All environments | `*` |
| `Env1,Env2` | Specific environments | `prod,dev` |
| `Prefix*` | Environments with prefix | `6*` |

### Examples

```bash
# All environments
PortwayMgt.exe admin -e "*"

# Production only
PortwayMgt.exe prod-api -e "prod"

# Test environments
PortwayMgt.exe test-api -e "dev,Synergy"

# Pattern matching
PortwayMgt.exe staging -e "7*"
```

## Token Management

### Listing Tokens

View all active tokens with their permissions:

```
=== Active Tokens ===
ID    Username             Created              Expires              Scopes          Environments
----  ------------------   ------------------   ------------------   --------------  --------------
1     admin               2024-01-15 10:30     Never                *               *
2     frontend            2024-01-16 14:22     2024-04-16           Products,Or...  prod,dev
3     reporting           2024-01-17 09:15     2024-02-17           Report*         *
```

### Revoking Tokens

1. Enter interactive mode
2. Select option 3 (Revoke token)
3. Enter the token ID
4. Confirm revocation

Revoked tokens are moved to `username.revoked.txt`

### Updating Token Permissions

#### Update Scopes
```
Current scopes: Products,Orders
Enter new scopes: Products,Orders,Customers,Reports
```

#### Update Environments
```
Current environments: prod
Enter new environments: prod,dev,Synergy
```

#### Update Expiration
```
Current expiration: Never
Enter days until expiration (0 for no expiration): 90
```

### Token Rotation

Securely replace tokens while maintaining permissions.

1. Select option 7 (Rotate token)
2. Enter token ID and confirm
3. Old token becomes invalid, new token generated with same permissions
4. Token file automatically updated, all operations logged for audit

## Token Files

### File Location

Tokens are stored in JSON files:
```
tokens/
  ├── admin.txt
  ├── frontend.txt
  ├── reporting.txt
  └── api-user.revoked.txt
```

### File Format

```json
{
  "Username": "frontend",
  "Token": "eyJhbGci...truncated...",
  "AllowedScopes": "Products,Orders",
  "AllowedEnvironments": "prod,dev",
  "ExpiresAt": "2024-04-16 14:22:00",
  "CreatedAt": "2024-01-16 14:22:00",
  "Description": "Frontend API access",
  "Usage": "Use this token in the Authorization header as: Bearer eyJhbGci..."
}
```

## Security Best Practices

### Token Generation

1. **Use descriptive usernames**
   ```bash
   # Good
   PortwayMgt.exe frontend-app
   PortwayMgt.exe reporting-service
   
   # Avoid
   PortwayMgt.exe user1
   PortwayMgt.exe test
   ```

2. **Apply principle of least privilege**
   ```bash
   # Good - specific access
   PortwayMgt.exe orders-service -s "Orders,Customers" -e "prod"
   
   # Avoid - excessive access
   PortwayMgt.exe orders-service -s "*" -e "*"
   ```

3. **Set expiration dates**
   ```bash
   # Good - time-limited tokens
   PortwayMgt.exe api-client --expires 90
   
   # Avoid - permanent tokens for temporary use
   PortwayMgt.exe temp-access
   ```

### Token Storage

1. **Secure token files**
   - Store tokens directory outside web root
   - Set appropriate file permissions
   - Encrypt sensitive token files

2. **Never commit tokens to version control**
   ```gitignore
   # .gitignore
   tokens/
   *.txt
   auth.db
   ```

3. **Rotate tokens regularly**
   - Set expiration dates
   - Revoke unused tokens
   - Monitor token usage

## Advanced Usage

### Bulk Token Generation

Create multiple tokens using scripts:

```powershell
# PowerShell script
$services = @(
    @{name="orders-service"; scopes="Orders,Customers"; env="prod"},
    @{name="products-service"; scopes="Products,Categories"; env="prod"},
    @{name="reporting-service"; scopes="*"; env="dev"}
)

foreach ($service in $services) {
    .\PortwayMgt.exe $service.name -s $service.scopes -e $service.env --expires 90
}
```

### Custom Token Patterns

Examples of advanced scope patterns:

```bash
# Read/Write separation
PortwayMgt.exe read-api -s "Products,Orders,Customers"
PortwayMgt.exe write-api -s "Products*,Orders*,Customers*"

# Service-specific tokens
PortwayMgt.exe inventory -s "Products,StockLevels,Warehouses"
PortwayMgt.exe sales -s "Orders,Invoices,Customers"

# Environment-specific access
PortwayMgt.exe dev-tools -s "*" -e "test*,dev*"
PortwayMgt.exe prod-monitoring -s "health,metrics" -e "prod*"
```

## Troubleshooting

### Common Issues

1. **Database not found**
   ```
   Error: Database not found at auth.db
   Solution: Use -d parameter to specify path
   ```

2. **Permission denied**
   ```
   Error: Cannot write to tokens directory
   Solution: Run as administrator or fix directory permissions
   ```

3. **Token already exists**
   ```
   Warning: Token file already exists
   Solution: Confirm overwrite or use different username
   ```

### Error Messages

| Error | Cause | Solution |
|-------|-------|----------|
| "Invalid database structure" | Corrupted auth.db | Restore from backup |
| "Token generation failed" | Database write error | Check permissions |
| "Invalid scope format" | Syntax error in scopes | Check comma separation |
| "Environment not allowed" | Invalid environment name | Verify environment exists |

### Automation Scripts

```powershell
# Automated token rotation
$token = .\PortwayMgt.exe api-service --expires 30
$tokenInfo = Get-Content "tokens/api-service.txt" | ConvertFrom-Json

# Store in secure location
Set-AzKeyVaultSecret -VaultName "MyVault" -Name "api-token" -SecretValue $tokenInfo.Token
```

## Audit Trail & Security Monitoring

The Management Console includes comprehensive audit logging for all token operations, providing complete visibility into token lifecycle management.

### Audit Trail Features

All token management operations are automatically logged with detailed information:

- **Token Creation**: User, permissions, expiration details
- **Token Rotation**: Old/new token hashes, timestamp, reason
- **Token Revocation**: User, timestamp, reason
- **Permission Updates**: Before/after values, modification details

### Viewing Audit Logs

#### Database Queries

Connect to the `auth.db` SQLite database to query audit logs:

```sql
-- View all operations for a specific user
SELECT * FROM TokenAudits 
WHERE Username = 'api-service' 
ORDER BY Timestamp DESC;

-- View all rotation operations
SELECT Username, Operation, Timestamp, Details 
FROM TokenAudits 
WHERE Operation LIKE '%Rotat%' 
ORDER BY Timestamp DESC;

-- View recent activity (last 24 hours)
SELECT * FROM TokenAudits 
WHERE Timestamp > datetime('now', '-1 day') 
ORDER BY Timestamp DESC;
```

#### Audit Log Schema

| Field | Description | Example |
|-------|-------------|---------|
| `Id` | Unique audit entry ID | `1547` |
| `TokenId` | Associated token ID (if applicable) | `23` |
| `Username` | Token owner username | `api-service` |
| `Operation` | Type of operation | `Created`, `Rotated`, `Revoked`, `Updated` |
| `OldTokenHash` | Previous token hash (for rotations) | `abc123...` |
| `NewTokenHash` | New token hash (for rotations) | `xyz789...` |
| `Timestamp` | When operation occurred | `2024-01-15 14:30:22` |
| `Details` | JSON metadata with operation details | `{"reason": "security_review"}` |
| `Source` | Application that performed operation | `TokenGenerator`, `PortwayApi` |
| `IpAddress` | Client IP address (when available) | `192.168.1.100` |
| `UserAgent` | Client user agent (when available) | `DESKTOP-ABC/user1` |

### Monitoring Token Activity

If you need to monitor token management activity, you can query the audit database directly:

```powershell
# Check for frequent token rotations (possible security issue)
$query = @"
SELECT COUNT(*) as RotationCount, Username 
FROM TokenAudits 
WHERE Operation = 'Rotated' 
AND Timestamp > datetime('now', '-1 day')
GROUP BY Username
HAVING COUNT(*) > 5
"@

$results = Invoke-SqliteQuery -Query $query -DataSource "auth.db"
foreach ($result in $results) {
    Write-Warning "Unusual activity: $($result.RotationCount) token rotations for user: $($result.Username)"
}
```