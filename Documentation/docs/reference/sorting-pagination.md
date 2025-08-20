# Sorting & Pagination

Efficiently sort and paginate large datasets using OData query parameters. This reference covers ordering results and implementing pagination patterns for optimal performance.

## Sorting with $orderby

### Basic Syntax

```
$orderby=field [asc|desc]
```

### Single Field Sorting

```http
# Ascending (default)
GET /api/600/Products?$orderby=Name

# Explicit ascending
GET /api/600/Products?$orderby=Name asc

# Descending
GET /api/600/Products?$orderby=Price desc
```

### Multiple Field Sorting

```http
# Primary and secondary sort
GET /api/600/Products?$orderby=Category,Price desc

# Multiple fields with mixed directions
GET /api/600/Products?$orderby=Category asc,Price desc,Name asc

# Complex sorting
GET /api/600/Orders?$orderby=CustomerCode,OrderDate desc,Priority asc
```

### Sorting Rules

1. Default order is ascending if not specified
2. Fields are sorted in the order listed
3. Field names are case-sensitive
4. Sorting applies before pagination

## Pagination with $top and $skip

### $top - Limit Results

Controls the maximum number of items returned:

```http
# Return first 10 items
GET /api/600/Products?$top=10

# Return first 50 items
GET /api/600/Products?$top=50
```

### $skip - Skip Results

Skips a specified number of items:

```http
# Skip first 20 items
GET /api/600/Products?$skip=20

# Skip first 100 items
GET /api/600/Products?$skip=100
```

### Combined Pagination

```http
# Page 1 (items 1-10)
GET /api/600/Products?$top=10&$skip=0

# Page 2 (items 11-20)
GET /api/600/Products?$top=10&$skip=10

# Page 3 (items 21-30)
GET /api/600/Products?$top=10&$skip=20
```

## Pagination Patterns

### Standard Pagination

Implement page-based navigation:

```http
# Page size: 25, Page number: 1
GET /api/600/Orders?$top=25&$skip=0&$orderby=OrderDate desc

# Page size: 25, Page number: 2
GET /api/600/Orders?$top=25&$skip=25&$orderby=OrderDate desc

# Page size: 25, Page number: 3
GET /api/600/Orders?$top=25&$skip=50&$orderby=OrderDate desc
```

Formula:
```
$skip = (pageNumber - 1) * pageSize
$top = pageSize
```

### Offset-Based Pagination

Direct control over starting position:

```http
# Get items 51-75
GET /api/600/Products?$top=25&$skip=50

# Get items 101-150
GET /api/600/Products?$top=50&$skip=100
```

### Cursor-Based Pagination

Use the NextLink provided in responses:

```json
{
  "Count": 10,
  "Value": [...],
  "NextLink": "/api/600/Products?$top=10&$skip=10&$orderby=ItemCode"
}
```

```http
# First page
GET /api/600/Products?$top=10&$orderby=ItemCode

# Next page (use NextLink)
GET /api/600/Products?$top=10&$skip=10&$orderby=ItemCode
```

## Combining Sort and Pagination

### Best Practices

Always include $orderby when using pagination:

```http
# Good - consistent ordering
GET /api/600/Products?$orderby=ItemCode&$top=20&$skip=0

# Problematic - inconsistent results
GET /api/600/Products?$top=20&$skip=0
```

### Common Patterns

1. **Recent Items First**
```http
GET /api/600/Orders?$orderby=OrderDate desc&$top=10
```

2. **Alphabetical with Pagination**
```http
GET /api/600/Customers?$orderby=LastName,FirstName&$top=50&$skip=0
```

3. **Priority Sorting**
```http
GET /api/600/Tasks?$orderby=Priority asc,DueDate asc&$top=20
```

4. **Multi-Level Sorting**
```http
GET /api/600/Products?$orderby=Category,SubCategory,Name&$top=100&$skip=0
```

## Performance Optimization

### Indexed Field Sorting

Sort on indexed fields for better performance:

```http
# Good - sorting on primary key
GET /api/600/Products?$orderby=ItemCode&$top=50

# Good - sorting on indexed field
GET /api/600/Orders?$orderby=OrderDate desc&$top=20

# Less efficient - non-indexed field
GET /api/600/Products?$orderby=Description&$top=50
```

### Efficient Pagination

1. **Use Reasonable Page Sizes**
```http
# Optimal
GET /api/600/Products?$top=50&$skip=0

# Too large (may timeout)
GET /api/600/Products?$top=10000&$skip=0

# Too small (many requests)
GET /api/600/Products?$top=5&$skip=0
```

2. **Avoid Deep Pagination**
```http
# Efficient
GET /api/600/Products?$top=50&$skip=100

# Inefficient (deep pagination)
GET /api/600/Products?$top=50&$skip=10000
```

### Sorting Performance Tips

1. **Single Field vs Multiple Fields**
```http
# Faster
GET /api/600/Products?$orderby=ItemCode

# Slower
GET /api/600/Products?$orderby=Category,SubCategory,Name
```

2. **Direction Matters**
```http
# Often faster (uses index efficiently)
GET /api/600/Orders?$orderby=OrderDate desc

# May be slower (depends on index)
GET /api/600/Orders?$orderby=OrderDate asc
```

## Complete Examples

### Product Catalog with Categories

```http
# Products by category, then price
GET /api/600/Products?$orderby=Category,Price desc&$top=20&$skip=0

# Products in "Electronics" category, sorted by price
GET /api/600/Products?$filter=Category eq 'Electronics'&$orderby=Price desc&$top=10

# Most expensive products per category
GET /api/600/Products?$select=Category,Name,Price&$orderby=Category,Price desc&$top=5
```

### Customer Order History

```http
# Recent orders for a customer
GET /api/600/Orders?$filter=CustomerCode eq 'CUST001'&$orderby=OrderDate desc&$top=10

# Customer's largest orders
GET /api/600/Orders?$filter=CustomerCode eq 'CUST001'&$orderby=TotalAmount desc&$top=5

# Orders with status tracking
GET /api/600/Orders?$filter=CustomerCode eq 'CUST001'&$orderby=OrderDate desc&$select=OrderNumber,OrderDate,Status,TotalAmount&$top=20
```

### Inventory Management

```http
# Low stock items
GET /api/600/Products?$filter=StockLevel le 10&$orderby=StockLevel asc&$top=20

# Items by warehouse location
GET /api/600/Products?$orderby=WarehouseLocation,Bin&$select=ItemCode,Name,WarehouseLocation,Bin,StockLevel&$top=50

# Recent stock movements
GET /api/600/StockMovements?$orderby=MovementDate desc&$top=100
```

## Error Handling

### Invalid $orderby Fields

```http
# Error: Invalid field name
GET /api/600/Products?$orderby=InvalidField
Response: 400 Bad Request
{
  "error": "Invalid orderby field: InvalidField"
}

# Error: Missing field name
GET /api/600/Products?$orderby=
Response: 400 Bad Request
{
  "error": "Empty orderby clause"
}
```

### Pagination Errors

```http
# Error: Negative $skip value
GET /api/600/Products?$skip=-10
Response: 400 Bad Request
{
  "error": "$skip cannot be negative"
}

# Error: $top exceeds maximum
GET /api/600/Products?$top=10000
Response: 400 Bad Request
{
  "error": "$top cannot exceed 1000"
}
```

## Response Format

### Standard Pagination Response

```json
{
  "Count": 10,
  "Value": [
    {
      "ItemCode": "PROD001",
      "Name": "Widget A",
      "Price": 29.99,
      "Category": "Widgets"
    },
    {
      "ItemCode": "PROD002",
      "Name": "Widget B",
      "Price": 39.99,
      "Category": "Widgets"
    }
    // ... 8 more items
  ],
  "NextLink": "/api/600/Products?$orderby=Category,Price desc&$top=10&$skip=10"
}
```

### Last Page Response

```json
{
  "Count": 5,
  "Value": [
    // ... 5 items
  ],
  "NextLink": null
}
```

## Advanced Techniques

### Stable Sorting

Ensure consistent pagination by including a unique identifier:

```http
# Good - stable sorting
GET /api/600/Products?$orderby=Price desc,ItemCode&$top=20&$skip=0

# Problematic - may have inconsistent ordering
GET /api/600/Products?$orderby=Price desc&$top=20&$skip=0
```

### Keyset Pagination

More efficient for large datasets:

```http
# Traditional offset pagination (slower for large offsets)
GET /api/600/Products?$orderby=ItemCode&$top=20&$skip=10000

# Keyset pagination (faster)
GET /api/600/Products?$filter=ItemCode gt 'PROD10000'&$orderby=ItemCode&$top=20
```

### Dynamic Sorting

Build orderby clauses programmatically:

```javascript
// JavaScript example
function buildOrderBy(sortFields) {
  return sortFields
    .map(field => `${field.name}${field.desc ? ' desc' : ''}`)
    .join(',');
}

const sortFields = [
  { name: 'Category', desc: false },
  { name: 'Price', desc: true },
  { name: 'Name', desc: false }
];

const orderBy = buildOrderBy(sortFields);
// Result: "Category,Price desc,Name"
```

### Sorting with Nulls

Handle null values in sorting:

```http
# Nulls typically sort first in ascending order
GET /api/600/Products?$orderby=DiscountPrice asc

# Nulls typically sort last in descending order
GET /api/600/Products?$orderby=DiscountPrice desc
```

## Troubleshooting

### Common Issues

1. **Inconsistent Pagination Results**
   - Always include $orderby for consistent pagination
   - Use a unique identifier as a tiebreaker

2. **Slow Performance**
   - Sort on indexed columns when possible
   - Limit page size to reasonable numbers
   - Avoid deep pagination (high $skip values)

3. **Unexpected Sort Order**
   - Check field names for case sensitivity
   - Verify sort direction (asc/desc)
   - Consider data type effects on sorting

### Performance Monitoring

```http
# Add timing headers to monitor performance
GET /api/600/Products?$orderby=Name&$top=100
Response Headers:
X-Query-Time: 45ms
X-Total-Records: 5000
```

## Best Practices Summary

1. **Always use $orderby with pagination**
2. **Include unique identifier for stable sorting**
3. **Keep page sizes reasonable (20-100 items)**
4. **Sort on indexed fields for better performance**
5. **Avoid deep pagination when possible**
6. **Consider keyset pagination for large datasets**
7. **Test sort performance with production data**
8. **Monitor query execution times**
9. **Handle edge cases (nulls, empty results)**
10. **Validate user input for sort fields**