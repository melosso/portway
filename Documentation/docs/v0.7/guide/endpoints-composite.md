---
title: Composite Endpoints
description: "Call several proxy endpoints in sequence with one request, passing values between steps"
---

# Composite Endpoints

Composite endpoints call existing proxy endpoints in sequence. Each step calls a named proxy endpoint and can pass values from its response to later steps. The caller sends one request and receives one combined result. Execution stops at the first failing step.

:::warning
Composite endpoints have no rollback. Steps completed before a failure remain committed; use idempotent steps and a clean-up procedure for partial failures.
:::

:::info
Each step references an existing proxy endpoint by name. A step cannot target an endpoint whose `entity.json` contains a `Tenancy` block, the property that maps [tenant headers](/guide/tenant-headers) to columns, parameters or upstream headers. Such a step returns `403`.
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

All properties, including `DependsOn`: [Entity configuration](/reference/entity-config#composite).

## Template transformations

Transformations insert generated values or values from earlier steps:

| Template | Description |
|---|---|
| `$guid` | A GUID generated once per request; every step receives the same value |
| `$requestid` | The composite request id (GUID) |
| `$prev.StepName.property` | A property from an earlier step's response |

Nested and array values use dot notation:

```json
{
  "TemplateTransformations": {
    "TransactionKey": "$prev.CreateOrderLines.0.d.TransactionKey",
    "LineCount": "$prev.CreateOrderLines.length"
  }
}
```

## Request and response format

Request:

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

Success response:

```json
{
  "success": true,
  "stepResults": {
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

Failure response:

```json
{
  "success": false,
  "error": "Error executing step 'CreateOrderHeader'",
  "details": { "message": "Insufficient credit limit" },
  "step": "CreateOrderHeader",
  "statusCode": 400,
  "completedSteps": ["CreateOrderLines"]
}
```

The response status is the failing step's status. `details` contains the step's upstream error body; a non-JSON body is truncated to 200 characters. `completedSteps` lists the steps completed before the failure.

## Example: multi-service operation

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

| Symptom | Resolution |
|---|---|
| "Endpoint not found" | The step's `Endpoint` must match an existing proxy endpoint name exactly (case-sensitive). |
| Transformation resolves to `null` | Check the `$prev.StepName.property` path and array indices against the referenced step's response. |
| Timeouts | Execution time is the sum of all steps. Test slow steps individually. |

Debug logging:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug"
    }
  }
}
```

Each step logs its name, target URL and method at `Debug`.

## Next steps

- [Proxy Endpoints](/guide/endpoints-proxy)
- [Environments](/guide/environments)
- [Security](/guide/security)
