# Security

Portway implements multiple layers of security to protect your APIs, data, and internal services. This guide covers authentication, authorization, network security, and best practices for securing your deployment.


::: warning Security Notice
Always follow your organization's security policies and compliance requirements when configuring Portway.
:::

## Authentication

### Token-Based Authentication

Portway uses Bearer token authentication for all API requests:

```http
Authorization: Bearer your-token-here
```

Tokens are:
- Generated using cryptographically secure random values
- Saved using modern encryption
- Bound to specific usernames for auditing

### Token Generation

Initial token generation happens automatically on first run:

```
🔑 Generated token for SERVER-NAME
💾 Token saved to: /tokens/SERVER-NAME.txt
```

Token file format:
```json
{
  "Username": "SERVER-NAME",
  "Token": "base64-encoded-secure-token",
  "AllowedScopes": "*",
  "AllowedEnvironments": "*",
  "ExpiresAt": "Never",
  "CreatedAt": "2024-01-01 00:00:00",
  "Usage": "Use this token in the Authorization header as: Bearer your-token-here"
}
```

### Managing Tokens

Use the Token Generator tool to manage access tokens.

> [!CAUTION]
> Any changes made through the program or command-line interface (CLI) take effect immediately.

#### Running the program

After navigating to the `tools/TokenGenerator/` folder, you can start the `TokenGenerator.exe`. The program will walk you through various options that'll allow you to manage the access to the API.

#### Using the CLI

```bash
# Generate token with full access
TokenGenerator.exe admin

# Generate token with specific scopes
TokenGenerator.exe api-user -s "Products,Orders"

# Generate token for specific environments
TokenGenerator.exe deploy-bot -e "dev,test"

# Generate token that expires
TokenGenerator.exe temp-user --expires 30
```

## Authorization

### Scoped Access Control

Tokens can be restricted to specific endpoints:

| Scope Pattern | Description | Example |
|--------------|-------------|----------|
| `*` | Full access to all endpoints | Default for admin tokens |
| `Products,Orders` | Access to specific endpoints | API integration tokens |
| `Product*` | Wildcard access to endpoints | All product-related endpoints |
| `GET:Products` | Method-specific access | Read-only access |

### Environment Access Control

Restrict tokens to specific environments:

| Environment Pattern | Description | Example |
|--------------------|-------------|----------|
| `*` | Access to all environments | Admin tokens |
| `prod` | Single environment access | Production-only tokens |
| `dev,test` | Multiple environments | Development team tokens |
| `dev*` | Wildcard environment access | All dev environments |

### Endpoint-Level Security

Configure security at the endpoint level:

```json
{
  "DatabaseObjectName": "SensitiveData",
  "AllowedEnvironments": ["prod"],
  "IsPrivate": true,
  "AllowedMethods": ["GET"]
}
```

## Network Security

### Request Validation

All requests are validated for:
- Valid authorization headers
- Allowed HTTP methods
- Permitted environments
- Token scopes and permissions
- Request size limits (10MB default)

### IP Restrictions

Configure allowed hosts in `environments/network-access-policy.json`:

```json
{
  "allowedHosts": [
    "localhost",
    "127.0.0.1",
    "your-internal-server.local"
  ],
  "blockedIpRanges": [
    "10.0.0.0/8",
    "172.16.0.0/12",
    "192.168.0.0/16",
    "169.254.0.0/16"
  ]
}
```

## Secure Configuration

### Azure Key Vault Integration

Store sensitive data securely in Azure Key Vault:

1. Set up Key Vault access:
```powershell
$env:KEYVAULT_URI = "https://your-keyvault.vault.azure.net/"
```

2. Create secrets in Azure Key Vault:
```
{environment}-ConnectionString
{environment}-ServerName
{environment}-Headers
```

3. Portway automatically retrieves secrets:
```
🔐 Azure Key Vault: Successfully connected
✅ Retrieved secrets for environment: prod
```

### Connection String Security

Never store passwords in configuration files:

❌ **Insecure:**
```json
{
  "ConnectionString": "Server=sql;User Id=admin;Password=MyPassword123!;"
}
```

✅ **Secure:**
```json
{
  "ConnectionString": "Server=sql;Integrated Security=True;"
}
```

Or use Azure Key Vault for credentials.

## Security Headers

### Automatic Security Headers

Portway adds these security headers to all responses:

| Header | Value | Purpose |
|--------|-------|---------|
| X-Content-Type-Options | nosniff | Prevent MIME type sniffing |
| X-Frame-Options | DENY | Prevent clickjacking |
| Strict-Transport-Security | max-age=31536000 | Enforce HTTPS |
| Referrer-Policy | strict-origin-when-cross-origin | Control referrer information |
| Permissions-Policy | geolocation=(), camera=(), microphone=() | Restrict browser features |

### Content Security Policy

Strict CSP is automatically applied:

```
Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; font-src 'self'; object-src 'none'; base-uri 'self'; form-action 'none'
```

## Logging and Auditing

### Security Event Logging

All security events are logged:

```
🔒 Token authentication successful for user: api-service
❌ Invalid token attempted for endpoint: Products
🚫 Rate limit exceeded for IP: 192.168.1.100
⚠️ Unauthorized environment access: prod
```

### Request Tracing

Enable detailed request tracing for security analysis:

```json
{
  "RequestTrafficLogging": {
    "Enabled": true,
    "IncludeRequestBodies": true,
    "IncludeResponseBodies": false,
    "CaptureHeaders": true
  }
}
```

## Best Practices

### Token Management

1. **Principle of Least Privilege**
   - Grant minimum required permissions
   - Use specific scopes instead of wildcards
   - Limit environment access

2. **Token Rotation**
   - Set expiration dates on tokens
   - Regularly rotate long-lived tokens
   - Revoke compromised tokens immediately

3. **Secure Storage**
   - Keep token files in secure locations
   - Use file system permissions
   - Never commit tokens to source control

### Deployment Security

1. **IIS Configuration**
   - Use a dedicated application pool
   - Configure appropriate identity
   - Enable HTTPS only
   - Remove server headers

2. **Network Security**
   - Deploy behind a firewall
   - Use internal network addresses
   - Implement IP whitelisting
   - Enable DDoS protection

3. **Database Security**
   - Use Windows Authentication
   - Implement least-privilege database users
   - Enable SQL Server auditing
   - Use encrypted connections

### Monitoring and Alerts

1. **Security Monitoring**
   - Monitor failed authentication attempts
   - Track rate limit violations
   - Alert on suspicious patterns
   - Review access logs regularly

2. **Automated Responses**
   - Block IPs after repeated failures
   - Revoke tokens with suspicious activity
   - Alert administrators on breaches
   - Implement automatic backups

## Security Checklist

### Pre-Deployment

- [ ] Configure HTTPS in IIS
- [ ] Set up Azure Key Vault
- [ ] Create restricted tokens
- [ ] Configure rate limiting
- [ ] Review firewall rules
- [ ] Test security headers

### Post-Deployment

- [ ] Verify security headers
- [ ] Test rate limiting
- [ ] Check access logs
- [ ] Validate token permissions
- [ ] Monitor for unauthorized access
- [ ] Review security alerts

## Incident Response

### Compromised Token

If a token is compromised, follow these steps immediately:

1. **Revoke the token using Token Generator**
   
   Run the Token Generator interactively:
   ```powershell
   cd C:\path\to\portway\tools
   TokenGenerator.exe
   ```
   
   Then follow these steps:
   ```
   ===============================================
         Portway Token Generator        
   ===============================================
   1. List all existing tokens
   2. Generate new token
   3. Revoke token
   4. Update token scopes
   5. Update token environments
   6. Update token expiration
   0. Exit
   -----------------------------------------------
   Select an option: 3
   ```
   
   When prompted, enter the ID of the compromised token:
   ```
   Enter token ID to revoke (or 0 to cancel): DemoUser
   Token with ID DemoUser has been revoked successfully.
   ```

2. **Effects of token revocation**
   
   - The token is marked with a `RevokedAt` timestamp in the database
   - The token file is deleted from the `tokens/` directory
   - All future API requests using this token will be rejected with 401 Unauthorized
   - Access logs will show "Invalid or expired token" for attempts to use the revoked token

3. **Review security logs**
   
   Check the logs for unauthorized activity:
   ```
   log/portwayapi-20240503.log
   ```
   
   Look for entries with the compromised token:
   ```
   [2024-05-03 14:23:15] ❌ Invalid or expired token used for /api/prod/Customers
   ```

4. **Generate a replacement token**
   
   Create a new token with restricted permissions:
   ```powershell
   # Option 1: Interactive mode
   TokenGenerator.exe
   # Then select option 2 and follow prompts
   
   # Option 2: Command line with specific restrictions
   TokenGenerator.exe api-user -s "Products,Orders" -e "dev,test" --expires 30
   ```

5. **Update affected systems**
   
   - Update all applications using the compromised token
   - Rotate any related credentials
   - Document the incident for security audit

6. **Monitor for continued unauthorized access**
   
   Enable detailed logging if not already active:
   ```json
   {
     "RequestTrafficLogging": {
       "Enabled": true,
       "IncludeRequestBodies": true,
       "CaptureHeaders": true
     }
   }
   ```

::: tip Quick Reference
To quickly check active tokens, run TokenGenerator and select option 1. This shows all tokens that haven't been revoked or expired.
:::

::: warning Important
Token revocation is permanent. Once revoked, a token cannot be reactivated. Always create a new token for the affected user or system.
:::

## Next Steps

- [Configure Rate Limiting](./rate-limiting) for API protection
- [Set up Monitoring](./monitoring) for security events
- [Deploy to Production](./deployment) with security best practices
- [Review API Reference](../reference/authentication) for detailed security configuration
