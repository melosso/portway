# OData Syntax

Portway's SQL endpoints support OData query syntax for flexible data retrieval. This reference covers the supported OData query options and their syntax.

## Query Options Overview

| Option | Purpose | Example |
|--------|---------|---------|
| `$select` | Choose specific fields | `$select=Name,Price` |
| `$filter` | Filter results | `$filter=Price gt 100` |
| `$orderby` | Sort results | `$orderby=Name desc` |
| `$top` | Limit results | `$top=10` |
| `$skip` | Skip results | `$skip=20` |

## Basic Query Structure

```http
GET /api/{environment}/{endpoint}?{query_options}
```

Example:
```http
GET /api/600/Products?$select=ItemCode,Description&$filter=Price gt 50&$orderby=Price desc&$top=10
```

## $select - Field Selection

Select specific fields from the entity:

### Syntax
```
$select=field1,field2,field3
```

### Examples
```http
# Single field
GET /api/600/Products?$select=ItemCode

# Multiple fields
GET /api/600/Products?$select=ItemCode,Description,Price

# All allowed fields (based on entity configuration)
GET /api/600/Products
```

### Field Selection Rules

- Field names are case-sensitive
- Only fields listed in `AllowedColumns` can be selected
- Invalid field names return an error
- If no `$select` is specified, all allowed fields are returned

## $filter - Filtering Data

Filter results based on conditions:

### Basic Syntax
```
$filter=field operator value
```

### Comparison Operators

| Operator | Description | Example |
|----------|-------------|---------|
| `eq` | Equals | `$filter=Status eq 'Active'` |
| `ne` | Not equals | `$filter=Status ne 'Closed'` |
| `gt` | Greater than | `$filter=Price gt 100` |
| `ge` | Greater than or equal | `$filter=Price ge 100` |
| `lt` | Less than | `$filter=Price lt 100` |
| `le` | Less than or equal | `$filter=Price le 100` |

### String Functions

| Function | Description | Example |
|----------|-------------|---------|
| `contains` | Contains substring | `$filter=contains(Description,'book')` |
| `startswith` | Starts with | `$filter=startswith(Name,'A')` |
| `endswith` | Ends with | `$filter=endswith(Email,'.com')` |

### Logical Operators

| Operator | Description | Example |
|----------|-------------|---------|
| `and` | Logical AND | `$filter=Price gt 100 and Status eq 'Active'` |
| `or` | Logical OR | `$filter=Status eq 'New' or Status eq 'Pending'` |
| `not` | Logical NOT | `$filter=not contains(Description,'test')` |

### Filter Examples

```http
# Exact match
GET /api/600/Products?$filter=ItemCode eq 'PROD001'

# Numeric comparison
GET /api/600/Products?$filter=Price gt 50.00

# String contains
GET /api/600/Products?$filter=contains(Description,'Widget')

# Multiple conditions
GET /api/600/Products?$filter=Price gt 100 and Assortment eq 'Electronics'

# OR condition
GET /api/600/Products?$filter=Status eq 'Active' or Status eq 'Pending'

# Complex filter
GET /api/600/Products?$filter=(Price gt 100 and Price lt 500) or contains(Description,'Special')
```

## $orderby - Sorting Results

Sort results by one or more fields:

### Syntax
```
$orderby=field [asc|desc]
```

### Examples
```http
# Single field ascending (default)
GET /api/600/Products?$orderby=Name

# Single field descending
GET /api/600/Products?$orderby=Price desc

# Multiple fields
GET /api/600/Products?$orderby=Category,Price desc

# Complex sorting
GET /api/600/Products?$orderby=Category asc,Price desc,Name asc
```

### Sorting Rules

- Default sort order is ascending
- Use `desc` for descending order
- Multiple fields are sorted in order listed
- Field names are case-sensitive

## $top and $skip - Pagination

Control result set size and implement pagination:

### $top Syntax
```
$top=number
```

### $skip Syntax
```
$skip=number
```

### Pagination Examples
```http
# First 10 items
GET /api/600/Products?$top=10

# Skip first 20 items
GET /api/600/Products?$skip=20

# Page 2 with 10 items per page
GET /api/600/Products?$top=10&$skip=10

# Page 3 with 25 items per page
GET /api/600/Products?$top=25&$skip=50
```

### Pagination Best Practices

1. Always use `$orderby` with pagination for consistent results
2. Keep page sizes reasonable (10-100 items)
3. Use the `NextLink` in responses for easy navigation
4. Implement server-side pagination for large datasets

## Combining Query Options

Multiple query options can be combined in a single request:

```http
# Complete query example
GET /api/600/Products
  ?$select=ItemCode,Description,Price,Category
  &$filter=Price gt 50 and Category eq 'Electronics'
  &$orderby=Price desc
  &$top=20
  &$skip=0
```

## Data Types in Queries

### String Values
- Enclose in single quotes: `'value'`
- Escape single quotes with double quotes: `'O''Brien'`

```http
$filter=Name eq 'Product Name'
$filter=Description eq 'It''s a product'
```

### Numeric Values
- No quotes needed
- Use decimal point for floating numbers

```http
$filter=Price eq 99.99
$filter=Quantity gt 10
```

### Date Values
- Use ISO 8601 format
- Can compare with standard operators

```http
$filter=CreatedDate gt 2024-01-01T00:00:00Z
$filter=ModifiedDate le 2024-12-31T23:59:59Z
```

### Boolean Values
- Use `true` or `false` (lowercase)

```http
$filter=IsActive eq true
$filter=IsDeleted eq false
```

### Null Values
- Use `null` keyword

```http
$filter=DeletedDate eq null
$filter=AssignedTo ne null
```

## Special Characters and Encoding

### URL Encoding
Special characters must be URL encoded:

| Character | Encoded | Example |
|-----------|---------|---------|
| Space | `%20` | `$filter=Name%20eq%20'Product'` |
| `'` | `%27` | `$filter=Name%20eq%20%27Product%27` |
| `&` | `%26` | In values only |
| `+` | `%2B` | `$filter=Code%20eq%20'A%2B'` |

### Reserved Characters
These characters have special meaning in OData:
- `$` - Query option prefix
- `(` `)` - Function and grouping
- `'` - String delimiter
- `,` - List separator

## Query Response Format

Successful queries return a JSON response:

```json
{
  "Count": 50,
  "Value": [
    {
      "ItemCode": "PROD001",
      "Description": "Widget A",
      "Price": 99.99
    },
    {
      "ItemCode": "PROD002",
      "Description": "Widget B",
      "Price": 149.99
    }
  ],
  "NextLink": "/api/600/Products?$top=10&$skip=20"
}
```

### Response Properties

| Property | Description |
|----------|-------------|
| `Count` | Number of items in this response |
| `Value` | Array of result objects |
| `NextLink` | URL for next page (if applicable) |

## Common Query Patterns

### Search by Text
```http
# Contains search
GET /api/600/Products?$filter=contains(Description,'widget')

# Starts with search
GET /api/600/Products?$filter=startswith(Name,'A')
```

### Date Range Queries
```http
# Records created this year
GET /api/600/Orders?$filter=CreatedDate ge 2024-01-01T00:00:00Z

# Records in date range
GET /api/600/Orders?$filter=OrderDate ge 2024-01-01 and OrderDate lt 2024-02-01
```

### Null Checking
```http
# Find unassigned items
GET /api/600/Tasks?$filter=AssignedTo eq null

# Find completed items
GET /api/600/Tasks?$filter=CompletedDate ne null
```

### Complex Filters
```http
# Multiple conditions with grouping
GET /api/600/Products
  ?$filter=(Price gt 100 and Price lt 500) and 
           (Category eq 'Electronics' or Category eq 'Computers')
```

## Query Limitations

### Maximum Values

| Limit | Default Value | Description |
|-------|--------------|-------------|
| `$top` | 1000 | Maximum items per request |
| `$skip` | No limit | Maximum items to skip |
| Query length | 2048 characters | Maximum URL length |
| Filter complexity | 10 conditions | Maximum filter conditions |

### Performance Considerations

1. Use indexed fields in filters and sorting
2. Limit result sets with `$top`
3. Avoid complex string operations on large datasets
4. Use specific filters rather than post-filtering

## Error Responses

### Query Syntax Errors

```json
{
  "error": "Invalid filter syntax",
  "details": "Unknown operator 'equals' at position 15",
  "success": false
}
```

### Invalid Field Names

```json
{
  "error": "Invalid field name",
  "details": "Field 'InvalidField' is not allowed",
  "success": false
}
```

### Type Mismatch

```json
{
  "error": "Type mismatch",
  "details": "Cannot compare string field 'Name' with numeric value",
  "success": false
}
```

## Best Practices

1. **Use Field Selection**
   ```http
   # Good - only needed fields
   GET /api/600/Products?$select=ItemCode,Name,Price
   
   # Avoid - retrieving all fields
   GET /api/600/Products
   ```

2. **Implement Efficient Pagination**
   ```http
   # Good - ordered pagination
   GET /api/600/Products?$orderby=ItemCode&$top=50&$skip=0
   
   # Avoid - pagination without ordering
   GET /api/600/Products?$top=50&$skip=0
   ```

3. **Use Appropriate Filters**
   ```http
   # Good - specific filter
   GET /api/600/Products?$filter=ItemCode eq 'PROD001'
   
   # Avoid - filtering in application
   GET /api/600/Products  # Then filter in code
   ```

4. **Optimize Complex Queries**
   ```http
   # Good - server-side filtering
   GET /api/600/Orders
     ?$filter=Status eq 'Open' and CustomerCode eq 'CUST001'
     &$orderby=OrderDate desc
     &$top=10
   ```

## Related Topics

- [Filter Operations](/reference/query-language/filter-operations) - Detailed filter operations
- [Sorting & Pagination](/reference/query-language/sorting-pagination) - Advanced sorting and pagination
- [SQL Endpoints](/guide/endpoints/sql) - SQL endpoint configuration
- [API Overview](/reference/api/overview) - General API reference