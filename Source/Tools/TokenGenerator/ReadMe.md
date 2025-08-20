# Enhanced TokenGenerator Usage Guide

## Overview

The enhanced TokenGenerator allows you to create and manage authentication tokens with fine-grained scope control. This lets you implement the principle of least privilege by giving tokens access only to the specific endpoints they need.

## Command-Line Usage

### Basic Token Generation

```bash
# Generate token with default settings (full access, no expiration)
TokenGenerator.exe username

# Generate token with specific scopes
TokenGenerator.exe username -s "Products,Orders,Customers"

# Generate token with expiration
TokenGenerator.exe username --expires 90

# Generate token with description
TokenGenerator.exe username --description "API access for frontend"

# Combine multiple options
TokenGenerator.exe username -s "Products,Orders" --expires 30 --description "Limited access token"
```

### Available Command-Line Arguments

| Argument | Description |
|----------|-------------|
| `-s, --scopes` | Comma-separated list of endpoints or patterns |
| `--description` | Description of what the token is used for |
| `--expires` | Number of days until token expires |
| `-d, --database` | Custom path to auth.db file |
| `-t, --tokens` | Custom path to tokens folder |
| `-h, --help` | Show help information |

## Interactive Menu

The TokenGenerator provides a full-featured menu for managing tokens:

1. **List all existing tokens** - Shows all active tokens with their scopes
2. **Generate new token** - Create a new token with custom settings
3. **Revoke token** - Invalidate an existing token
4. **Update token scopes** - Change which endpoints a token can access
5. **Update token expiration** - Change when a token expires

## Scope Configuration

Scopes determine which endpoints a token can access:

- `*` - Full access to all endpoints
- `Products,Orders` - Access to specific endpoints only
- `Product*` - Access to all endpoints starting with "Product"

## Token Files

When you generate a token, a JSON file is created in the tokens directory with:

- The token string (for authentication)
- Scope information
- Expiration date (if set)
- Usage examples

## Best Practices

1. **Use specific scopes** rather than full access when possible
2. **Set reasonable expiration dates** for tokens in production environments
3. **Add descriptive comments** to document what each token is used for
4. **Periodically audit tokens** to ensure access is still appropriate