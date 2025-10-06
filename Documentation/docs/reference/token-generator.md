# Token Generator

The Token Generator is a command-line utility for creating and managing authentication tokens for the Portway API. It provides fine-grained control over token permissions, including endpoint access and environment restrictions.

## Installation

### Prerequisites

- .NET 9.0 Runtime (which is only available in the Hosting Bundle)
- Access to the auth.db file
- Write permissions to the tokens directory

### Setup

1. Navigate to the tools directory:
```bash
cd Deployment/PortwayApi/tools/TokenGenerator
```

2. Run the executable:
```bash
TokenGenerator.exe
```

## Usage Modes

### Interactive Mode

Run without parameters to enter interactive mode:

```bash
TokenGenerator.exe
```

Interactive menu options:
1. List all existing tokens
2. Generate new token  
3. Revoke token
4. Update token scopes
5. Update token environments
6. Update token expiration
7. Rotate token
0. Exit

### Command Line Mode

Generate tokens directly with parameters:

```bash
# Basic token generation
TokenGenerator.exe username

# Token with specific endpoint scopes
TokenGenerator.exe username -s "Products,Orders,Customers"

# Token with specific namespace scopes
TokenGenerator.exe hr-system -s "Company/*"

# Token with environment restrictions
TokenGenerator.exe username -e "prod,dev"

# Token with expiration (in days)
TokenGenerator.exe username --expires 90

# Combined parameters
TokenGenerator.exe username -s "Products,Orders" -e "prod" --expires 30 --description "Frontend API access"
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
TokenGenerator.exe admin -s "*"

# Specific endpoints
TokenGenerator.exe frontend -s "Products,Orders,Customer/*"

# Pattern matching
TokenGenerator.exe reporting -s "Report*,Export*"

# Specific service access
TokenGenerator.exe frontend -s "Products,Orders" --description "Frontend service access"
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
TokenGenerator.exe admin -e "*"

# Production only
TokenGenerator.exe prod-api -e "prod"

# Test environments
TokenGenerator.exe test-api -e "dev,Synergy"

# Pattern matching
TokenGenerator.exe staging -e "7*"
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
   TokenGenerator.exe frontend-app
   TokenGenerator.exe reporting-service
   
   # Avoid
   TokenGenerator.exe user1
   TokenGenerator.exe test
   ```

2. **Apply principle of least privilege**
   ```bash
   # Good - specific access
   TokenGenerator.exe orders-service -s "Orders,Customers" -e "prod"
   
   # Avoid - excessive access
   TokenGenerator.exe orders-service -s "*" -e "*"
   ```

3. **Set expiration dates**
   ```bash
   # Good - time-limited tokens
   TokenGenerator.exe api-client --expires 90
   
   # Avoid - permanent tokens for temporary use
   TokenGenerator.exe temp-access
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
    .\TokenGenerator.exe $service.name -s $service.scopes -e $service.env --expires 90
}
```

### Token Validation

Verify token permissions programmatically:

```csharp
// Check if token has access to endpoint
bool hasAccess = token.HasAccessToEndpoint("Products");

// Check environment access
bool canAccessEnv = token.HasAccessToEnvironment("prod");

// Validate token is active
bool isActive = token.IsActive;
```

### Custom Token Patterns

Examples of advanced scope patterns:

```bash
# Read/Write separation
TokenGenerator.exe read-api -s "Products,Orders,Customers"
TokenGenerator.exe write-api -s "Products*,Orders*,Customers*"

# Service-specific tokens
TokenGenerator.exe inventory -s "Products,StockLevels,Warehouses"
TokenGenerator.exe sales -s "Orders,Invoices,Customers"

# Environment-specific access
TokenGenerator.exe dev-tools -s "*" -e "test*,dev*"
TokenGenerator.exe prod-monitoring -s "health,metrics" -e "prod*"
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

## Integration Examples

### Using Generated Tokens

```http
# HTTP request with token
GET /api/prod/Products
Authorization: Bearer eyJhbGci...truncated...
```

```javascript
// JavaScript example
const response = await fetch('https://api.example.com/api/prod/Products', {
  headers: {
    'Authorization': `Bearer ${token}`,
    'Content-Type': 'application/json'
  }
});
```

```csharp
// C# example
var client = new HttpClient();
client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
var response = await client.GetAsync("https://api.example.com/api/prod/Products");
```

### Automation Scripts

```powershell
# Automated token rotation
$token = .\TokenGenerator.exe api-service --expires 30
$tokenInfo = Get-Content "tokens/api-service.txt" | ConvertFrom-Json

# Store in secure location
Set-AzKeyVaultSecret -VaultName "MyVault" -Name "api-token" -SecretValue $tokenInfo.Token
```

## Audit Trail & Security Monitoring (NEW)

The Token Generator now includes comprehensive audit logging for all token operations, providing complete visibility into token lifecycle management.

### Audit Trail Features

All token operations are automatically logged with detailed information:

- **Token Creation**: User, permissions, expiration details
- **Token Rotation**: Old/new token hashes, timestamp, reason
- **Token Revocation**: User, timestamp, reason
- **Permission Updates**: Before/after values, modification details
- **Failed Access Attempts**: Invalid tokens, authorization failures

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

-- View security events (failed auth, authorization failures)
SELECT Username, Operation, IpAddress, UserAgent, Timestamp 
FROM TokenAudits 
WHERE Operation IN ('FailedAuth', 'AuthorizationFailed') 
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
| `Operation` | Type of operation | `Rotated`, `Created`, `FailedAuth` |
| `OldTokenHash` | Previous token hash (for rotations) | `abc123...` |
| `NewTokenHash` | New token hash (for rotations) | `xyz789...` |
| `Timestamp` | When operation occurred | `2024-01-15 14:30:22` |
| `Details` | JSON metadata with operation details | `{"reason": "security_review"}` |
| `Source` | Application that performed operation | `TokenGenerator`, `PortwayApi` |
| `IpAddress` | Client IP address (when available) | `192.168.1.100` |
| `UserAgent` | Client user agent (when available) | `DESKTOP-ABC/user1` |

### Security Monitoring

#### Failed Authentication Tracking

The system automatically logs failed authentication attempts:

```json
{
  "RequestPath": "/api/prod/Products",
  "Method": "GET",
  "TokenPrefix": "invalidtok...",
  "UserAgent": "Mozilla/5.0...",
  "Timestamp": "2024-01-15 14:30:22"
}
```

#### Authorization Failure Tracking

Access denied events are logged with full context:

```json
{
  "ResourceType": "Environment",
  "ResourceName": "prod",
  "RequestPath": "/api/prod/Orders",
  "Method": "POST",
  "AvailableScopes": "Products,Customers",
  "AvailableEnvironments": "dev,test",
  "Timestamp": "2024-01-15 14:30:22"
}
```

### Automated Monitoring Examples

#### PowerShell Monitoring Script

```powershell
# Check for suspicious activity in last hour
$query = @"
SELECT COUNT(*) as FailedAttempts, IpAddress 
FROM TokenAudits 
WHERE Operation = 'FailedAuth' 
AND Timestamp > datetime('now', '-1 hour')
GROUP BY IpAddress
HAVING COUNT(*) > 10
"@

$results = Invoke-SqliteQuery -Query $query -DataSource "auth.db"
foreach ($result in $results) {
    Write-Warning "Suspicious activity: $($result.FailedAttempts) failed attempts from $($result.IpAddress)"
}
```

#### Security Alert Integration

```csharp
// C# example for monitoring service
public async Task CheckSecurityEvents()
{
    var recentFailures = await dbContext.TokenAudits
        .Where(a => a.Operation == "FailedAuth" && 
                   a.Timestamp > DateTime.UtcNow.AddMinutes(-15))
        .GroupBy(a => a.IpAddress)
        .Where(g => g.Count() > 5)
        .ToListAsync();
        
    foreach (var group in recentFailures)
    {
        await alertService.SendSecurityAlert(
            $"Multiple failed auth attempts from {group.Key}");
    }
}
```

## Security Considerations

1. **Token Lifecycle Management**
   - Set appropriate expiration times
   - Revoke compromised tokens immediately
   - **Use token rotation for regular security maintenance**
   - Audit token usage regularly

2. **Access Control**
   - Limit token generation to authorized personnel
   - Use separate tokens for different services
   - Apply environment restrictions appropriately
   - **Monitor audit logs for unauthorized access attempts**

3. **Secure Storage**
   - Encrypt token files at rest
   - Use secure key management systems
   - Never expose tokens in logs or error messages
   - **Protect the audit database with appropriate access controls**

4. **Monitoring and Auditing**
   - Track token creation and revocation
   - Monitor failed authentication attempts
   - Set up alerts for suspicious activity
   - **Review audit logs regularly for security compliance**
   - **Use token rotation logs to track security maintenance**

### Token Rotation Best Practices

1. **Regular Rotation Schedule**
   ```bash
   # Rotate high-privilege tokens monthly
   # Rotate service tokens quarterly
   # Rotate temporary tokens after use
   ```

2. **Emergency Rotation**
   ```bash
   # Immediately rotate tokens when:
   # - Security incident detected
   # - Employee access changes
   # - Suspected compromise
   ```

3. **Automated Rotation**
   ```powershell
   # Schedule regular rotations
   # Monitor for tokens nearing expiration
   # Alert on failed rotation attempts
   ```