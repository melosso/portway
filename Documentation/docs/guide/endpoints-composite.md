# Composite Endpoints

> Orchestrate multiple proxy endpoint calls into a single transaction, with data passing between steps.

Composite endpoints chain existing proxy endpoints into a sequence. Each step calls a named proxy endpoint, receives its response, and can pass extracted values to subsequent steps. The caller sends one request and receives a combined result. Steps execute in order — if any step fails, execution stops.

:::warning
Composite endpoints do not provide automatic rollback. Steps that complete before a failure remain committed. Design your operations accordingly — idempotent steps and a clean-up procedure for partial failures are both worth implementing.
:::

:::info
Each step references an existing proxy endpoint by name. Configure your proxy endpoints first before building composites on top of them.
:::

## Configuration

Create `endpoints/Proxy/{CompositeName}/entity.json`:

```json
{
  "Type": "Composite",
  "Url": "http://internal-service/api",
  "Methods": ["POST"],
  "CompositeConfig": {
    "Name": "CreateOrder",
    "Description": "Creates order lines then the order header",
    "Steps": [
      {
        "Name": "CreateOrderLines",
        "Endpoint": "OrderLine",
        "Method": "POST",
        "IsArray": true,
        "ArrayProperty": "Lines",
        "TemplateTransformations": {
          "TransactionKey": "$guid"
        }
      },
      {
        "Name": "CreateOrderHeader",
        "Endpoint": "OrderHeader",
        "Method": "POST",
        "SourceProperty": "Header",
        "TemplateTransformations": {
          "TransactionKey": "$prev.CreateOrderLines.0.d.TransactionKey"
        }
      }
    ]
  }
}
```

### Top-level properties

| Property | Required | Type | Description |
|---|---|---|---|
| `Type` | Yes | string | Must be `"Composite"` |
| `Url` | Yes | string | Base URL used for all steps |
| `Methods` | Yes | array | Supported HTTP methods. Composite endpoints typically accept `["POST"]` only |
| `CompositeConfig` | Yes | object | Step definitions and operation metadata |

### CompositeConfig properties

| Property | Required | Type | Description |
|---|---|---|---|
| `Name` | Yes | string | Identifier for this composite operation |
| `Description` | No | string | Human-readable description, included in OpenAPI docs |
| `Steps` | Yes | array | Ordered list of steps to execute |

### Step properties

| Property | Required | Type | Description |
|---|---|---|---|
| `Name` | Yes | string | Unique step name, used in `$prev` references |
| `Endpoint` | Yes | string | Name of the proxy endpoint to call |
| `Method` | Yes | string | HTTP method to use for this step |
| `IsArray` | No | boolean | When `true`, iterates over an array and calls the endpoint once per item |
| `ArrayProperty` | Yes if IsArray | string | Property in the request body containing the array |
| `SourceProperty` | No | string | Extract this property from the request body and send it as the step's request body |
| `TemplateTransformations` | No | object | Values to inject or override in the request body before the step executes |

## Template transformations

Use transformations to inject generated values or reference data from earlier steps:

| Template | Description |
|---|---|
| `$guid` | Generates a new GUID |
| `$requestid` | Uses the incoming request ID |
| `$prev.StepName.property` | References a property from a previous step's response |
| `$context.variable` | References a context variable |

Access nested and array results with dot notation:

```json
{
  "TemplateTransformations": {
    "TransactionKey": "$prev.CreateOrderLines.0.d.TransactionKey",
    "LineCount": "$prev.CreateOrderLines.length"
  }
}
```

## Request and response format

**Request:**

```http
POST /api/prod/composite/CreateOrder
Content-Type: application/json
Authorization: Bearer <token>

{
  "Header": {
    "CustomerCode": "CUST001",
    "OrderDate": "2024-03-15",
    "Reference": "PO-12345"
  },
  "Lines": [
    { "ProductCode": "PROD001", "Quantity": 5, "Price": 99.99 },
    { "ProductCode": "PROD002", "Quantity": 3, "Price": 149.99 }
  ]
}
```

**Success response:**

```json
{
  "Success": true,
  "StepResults": {
    "CreateOrderLines": [
      { "d": { "TransactionKey": "abc-123", "LineNumber": 1, "ProductCode": "PROD001" } },
      { "d": { "TransactionKey": "abc-123", "LineNumber": 2, "ProductCode": "PROD002" } }
    ],
    "CreateOrderHeader": {
      "d": { "OrderNumber": "SO-001234", "TransactionKey": "abc-123", "Status": "Created" }
    }
  }
}
```

**Failure response:**

```json
{
  "Success": false,
  "ErrorStep": "CreateOrderHeader",
  "ErrorMessage": "Insufficient credit limit",
  "ErrorDetail": "Customer credit limit exceeded",
  "StatusCode": 400,
  "StepResults": {
    "CreateOrderLines": [...]
  }
}
```

`StepResults` always includes results from steps that completed before the failure.

## Example — multi-service operation

```json
{
  "Type": "Composite",
  "Url": "http://erp-service/api",
  "Methods": ["POST"],
  "CompositeConfig": {
    "Name": "ProcessOrder",
    "Steps": [
      {
        "Name": "ValidateCustomer",
        "Endpoint": "CustomerService",
        "Method": "POST",
        "SourceProperty": "Customer"
      },
      {
        "Name": "CheckInventory",
        "Endpoint": "InventoryService",
        "Method": "POST",
        "SourceProperty": "Items"
      },
      {
        "Name": "CreateOrder",
        "Endpoint": "OrderService",
        "Method": "POST",
        "TemplateTransformations": {
          "CustomerId": "$prev.ValidateCustomer.id",
          "AvailableItems": "$prev.CheckInventory.available"
        }
      }
    ]
  }
}
```

## Troubleshooting

**"Endpoint not found"** — The step's `Endpoint` value must match an existing proxy endpoint name exactly (case-sensitive).

**Transformation resolves to null** — Verify the `$prev.StepName.property` path against the actual response structure of the referenced step. Check array indices when referencing array results.

**Timeouts** — Total execution time includes all steps in sequence. If individual steps are slow, the composite timeout can be reached. Test each step individually first.

To increase log verbosity:

```json
{
  "Logging": {
    "LogLevel": {
      "PortwayApi.Classes.CompositeEndpointHandler": "Debug"
    }
  }
}
```

## Next steps

- [Proxy Endpoints](./endpoints-proxy)
- [Environments](./environments)
- [Security](./security)
