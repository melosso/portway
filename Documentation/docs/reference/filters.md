# Filter Operations

Filter operations allow you to query specific data from SQL endpoints using OData syntax. This reference covers all supported filter operations with examples and best practices.

## Filter Syntax

Basic filter structure:
```
$filter=expression
```

## Comparison Operators

### Equality Operators

| Operator | Description | Example |
|----------|-------------|---------|
| `eq` | Equal to | `$filter=Status eq 'Active'` |
| `ne` | Not equal to | `$filter=Status ne 'Closed'` |

#### Examples
```http
# String equality
GET /api/600/Products?$filter=ItemCode eq 'PROD001'

# Numeric equality
GET /api/600/Products?$filter=Price eq 99.99

# Boolean equality
GET /api/600/Products?$filter=IsActive eq true

# Date equality
GET /api/600/Orders?$filter=OrderDate eq 2024-01-15
```

### Relational Operators

| Operator | Description | Example |
|----------|-------------|---------|
| `gt` | Greater than | `$filter=Price gt 100` |
| `ge` | Greater than or equal to | `$filter=Price ge 100` |
| `lt` | Less than | `$filter=Price lt 100` |
| `le` | Less than or equal to | `$filter=Price le 100` |

#### Examples
```http
# Numeric comparison
GET /api/600/Products?$filter=Price gt 50.00

# Date comparison
GET /api/600/Orders?$filter=OrderDate gt 2024-01-01

# String comparison (alphabetical)
GET /api/600/Customers?$filter=Name gt 'M'
```

## Logical Operators

### Combining Conditions

| Operator | Description | Example |
|----------|-------------|---------|
| `and` | Logical AND | `$filter=Price gt 100 and Status eq 'Active'` |
| `or` | Logical OR | `$filter=Status eq 'New' or Status eq 'Pending'` |
| `not` | Logical NOT | `$filter=not contains(Description,'test')` |

#### Examples
```http
# AND condition
GET /api/600/Products?$filter=Price gt 100 and Category eq 'Electronics'

# OR condition
GET /api/600/Orders?$filter=Status eq 'Pending' or Status eq 'Processing'

# NOT condition
GET /api/600/Products?$filter=not IsDeleted

# Complex combination
GET /api/600/Products?$filter=(Price gt 100 and Price lt 500) or Category eq 'Special'
```

### Grouping with Parentheses

Use parentheses to control operator precedence:

```http
# Without parentheses (AND has precedence over OR)
GET /api/600/Products?$filter=Price gt 100 and Category eq 'A' or Category eq 'B'
# Evaluates as: (Price gt 100 AND Category eq 'A') OR Category eq 'B'

# With parentheses
GET /api/600/Products?$filter=Price gt 100 and (Category eq 'A' or Category eq 'B')
# Evaluates as: Price gt 100 AND (Category eq 'A' OR Category eq 'B')
```

## String Functions

### Text Search Functions

| Function | Description | Example |
|----------|-------------|---------|
| `contains(field,value)` | Contains substring | `$filter=contains(Description,'widget')` |
| `startswith(field,value)` | Starts with string | `$filter=startswith(Name,'A')` |
| `endswith(field,value)` | Ends with string | `$filter=endswith(Email,'.com')` |

#### Examples
```http
# Contains search (case-sensitive)
GET /api/600/Products?$filter=contains(Description,'book')

# Starts with
GET /api/600/Customers?$filter=startswith(Name,'John')

# Ends with
GET /api/600/Documents?$filter=endswith(FileName,'.pdf')

# Combining string functions
GET /api/600/Products?$filter=contains(Description,'premium') and startswith(ItemCode,'P')
```

### String Function Best Practices

1. **Case Sensitivity**: All string comparisons are case-sensitive
2. **Performance**: `startswith` is typically faster than `contains`
3. **Wildcards**: Use string functions instead of SQL wildcards

## Working with Data Types

### String Values

Strings must be enclosed in single quotes:

```http
# Correct
$filter=Name eq 'John Smith'

# Escape single quotes with another single quote
$filter=Description eq 'It''s a product'

# Multiple words
$filter=Category eq 'Home & Garden'
```

### Numeric Values

Numbers don't require quotes:

```http
# Integer
$filter=Quantity eq 10

# Decimal
$filter=Price eq 99.99

# Negative numbers
$filter=Balance gt -100.50

# Scientific notation
$filter=Value lt 1.5e6
```

### Date and DateTime Values

Use ISO 8601 format:

```http
# Date only
$filter=OrderDate eq 2024-01-15

# DateTime
$filter=CreatedAt gt 2024-01-15T14:30:00Z

# Date range
$filter=OrderDate ge 2024-01-01 and OrderDate lt 2024-02-01
```

### Boolean Values

Use lowercase `true` or `false`:

```http
# Boolean true
$filter=IsActive eq true

# Boolean false
$filter=IsDeleted eq false

# Negation
$filter=not IsActive
```

### Null Values

Use `null` keyword:

```http
# Check for null
$filter=AssignedTo eq null

# Check for not null
$filter=CompletedDate ne null

# Combine with other conditions
$filter=Status eq 'Open' and AssignedTo eq null
```

## Advanced Filter Patterns

### Range Queries

```http
# Numeric range
GET /api/600/Products?$filter=Price ge 100 and Price le 500

# Date range
GET /api/600/Orders?$filter=OrderDate ge 2024-01-01 and OrderDate lt 2024-02-01

# Excluding boundaries
GET /api/600/Products?$filter=Price gt 100 and Price lt 500
```

### Multiple Value Matching

```http
# Using OR for multiple values
GET /api/600/Orders?$filter=Status eq 'New' or Status eq 'Pending' or Status eq 'Processing'

# Alternative approach with grouping
GET /api/600/Products?$filter=(Category eq 'A' or Category eq 'B' or Category eq 'C') and Price gt 50
```

### Complex Text Searches

```http
# Multiple text conditions
GET /api/600/Products?$filter=contains(Description,'premium') and not contains(Description,'refurbished')

# Search in multiple fields
GET /api/600/Products?$filter=contains(Name,'widget') or contains(Description,'widget')
```

### Nested Conditions

```http
# Complex nested logic
GET /api/600/Orders?$filter=(Status eq 'Open' and Priority eq 1) or (Status eq 'Pending' and DueDate lt 2024-02-01)

# Multiple grouping levels
GET /api/600/Products?$filter=((Price gt 100 and Price lt 500) or Category eq 'Special') and IsActive eq true
```

## Filter Performance Tips

### 1. Use Indexed Fields

Always filter on indexed fields when possible:

```http
# Good - filtering on primary key
GET /api/600/Products?$filter=ItemCode eq 'PROD001'

# Less efficient - filtering on non-indexed field
GET /api/600/Products?$filter=contains(Description,'long text search')
```

### 2. Avoid Complex String Operations

```http
# Efficient - exact match
GET /api/600/Products?$filter=Category eq 'Electronics'

# Less efficient - contains operation
GET /api/600/Products?$filter=contains(Category,'Elec')

# Most efficient for prefix search
GET /api/600/Products?$filter=startswith(ItemCode,'PROD')
```

### 3. Limit Result Sets Early

```http
# Good - filter reduces dataset before sorting
GET /api/600/Orders?$filter=Status eq 'Open'&$orderby=OrderDate desc&$top=10

# Less efficient - sorting entire dataset
GET /api/600/Orders?$orderby=OrderDate desc&$top=10
```

### 4. Use Specific Conditions

```http
# Specific date
GET /api/600/Orders?$filter=OrderDate eq 2024-01-15

# Date range (less specific)
GET /api/600/Orders?$filter=OrderDate ge 2024-01-01 and OrderDate lt 2024-02-01

# Very broad (avoid)
GET /api/600/Orders?$filter=OrderDate ne null
```

## Common Filter Patterns

### Active Records

```http
# Active items
GET /api/600/Products?$filter=IsActive eq true and IsDeleted eq false

# Non-deleted items
GET /api/600/Customers?$filter=DeletedDate eq null
```

### Date-Based Filters

```http
# Today's records
GET /api/600/Orders?$filter=OrderDate eq 2024-01-15

# This month's records
GET /api/600/Orders?$filter=OrderDate ge 2024-01-01 and OrderDate lt 2024-02-01

# Last 30 days
GET /api/600/Orders?$filter=OrderDate gt 2023-12-16
```

### Status Filters

```http
# Single status
GET /api/600/Tasks?$filter=Status eq 'Open'

# Multiple statuses
GET /api/600/Tasks?$filter=Status eq 'Open' or Status eq 'InProgress'

# Exclude status
GET /api/600/Tasks?$filter=Status ne 'Closed'
```

### Search Patterns

```http
# Partial match
GET /api/600/Products?$filter=contains(Name,'widget')

# Prefix search
GET /api/600/Customers?$filter=startswith(LastName,'Sm')

# Multiple field search
GET /api/600/Products?$filter=contains(Name,'phone') or contains(Description,'phone')
```

## Error Handling

### Common Filter Errors

1. **Invalid Field Name**
```json
{
  "error": "Invalid field name",
  "details": "Field 'InvalidField' is not allowed",
  "success": false
}
```

2. **Syntax Error**
```json
{
  "error": "Invalid filter syntax",
  "details": "Expected operator at position 15",
  "success": false
}
```

3. **Type Mismatch**
```json
{
  "error": "Type mismatch",
  "details": "Cannot compare string field with numeric value",
  "success": false
}
```

4. **Invalid Date Format**
```json
{
  "error": "Invalid date format",
  "details": "Date must be in ISO 8601 format",
  "success": false
}
```

## Filter Limitations

| Limitation | Description | Workaround |
|------------|-------------|------------|
| Query length | Maximum 2048 characters | Split into multiple queries |
| Filter complexity | Maximum 10 conditions | Simplify or split filters |
| String functions | Case-sensitive only | Handle case in application |
| No regex support | No pattern matching | Use contains/startswith |
| No arithmetic | No calculations in filters | Pre-calculate values |

## Best Practices

1. **Use Appropriate Data Types**
   ```http
   # Good
   $filter=Price gt 100.00
   $filter=IsActive eq true
   
   # Avoid
   $filter=Price gt '100.00'
   $filter=IsActive eq 'true'
   ```

2. **Optimize Filter Order**
   ```http
   # Good - most restrictive first
   $filter=ItemCode eq 'PROD001' and IsActive eq true
   
   # Less optimal
   $filter=IsActive eq true and ItemCode eq 'PROD001'
   ```

3. **Use Parentheses for Clarity**
   ```http
   # Clear precedence
   $filter=(Status eq 'A' or Status eq 'B') and IsActive eq true
   
   # Ambiguous
   $filter=Status eq 'A' or Status eq 'B' and IsActive eq true
   ```

4. **Escape Special Characters**
   ```http
   # Escape single quotes
   $filter=Name eq 'O''Brien'
   
   # URL encode spaces
   $filter=Name%20eq%20'John%20Doe'
   ```

## Related Topics

- [OData Syntax](/reference/query-language/odata-syntax) - Complete OData syntax reference
- [Sorting & Pagination](/reference/query-language/sorting-pagination) - Sorting and pagination
- [SQL Endpoints](/guide/endpoints/sql) - SQL endpoint configuration
- [API Overview](/reference/api/overview) - General API reference