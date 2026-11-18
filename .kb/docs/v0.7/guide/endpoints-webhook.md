---
title: Webhook Endpoints
description: "Receive HTTP POST payloads from external services and persist them to a SQL table"
---

# Webhook Endpoints

Webhook endpoints accept POST requests from external services and store the JSON payload in a configured table. The webhook id is checked against `AllowedColumns`, the payload is inserted unchanged with a UTC timestamp, and the response returns the new row id.

## Database setup

SQL Server table:

```sql
CREATE TABLE [dbo].[WebhookData] (
    [Id]         INT IDENTITY(1,1) PRIMARY KEY,
    [WebhookId]  NVARCHAR(255)    NOT NULL,
    [Payload]    NVARCHAR(MAX)    NOT NULL,
    [ReceivedAt] DATETIME         NOT NULL DEFAULT GETDATE()
);

CREATE INDEX IX_WebhookData_WebhookId
ON [dbo].[WebhookData] ([WebhookId], [ReceivedAt] DESC);
```

Equivalent tables for the other providers. PostgreSQL column names are quoted because they are case-sensitive:

::: code-group
```sql [PostgreSQL]
CREATE TABLE public."WebhookData" (
    "Id"         SERIAL PRIMARY KEY,
    "WebhookId"  VARCHAR(255) NOT NULL,
    "Payload"    TEXT         NOT NULL,
    "ReceivedAt" TIMESTAMPTZ  NOT NULL DEFAULT now()
);
```

```sql [MySQL]
CREATE TABLE WebhookData (
    Id         INT AUTO_INCREMENT PRIMARY KEY,
    WebhookId  VARCHAR(255) NOT NULL,
    Payload    LONGTEXT     NOT NULL,
    ReceivedAt DATETIME     NOT NULL
);
```

```sql [SQLite]
CREATE TABLE WebhookData (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    WebhookId  TEXT NOT NULL,
    Payload    TEXT NOT NULL,
    ReceivedAt TEXT NOT NULL
);
```
:::

The `ReceivedAt` value is stored in UTC.

Optional status columns for the processing job:

```sql
ALTER TABLE WebhookData ADD
    ProcessedAt DATETIME     NULL,
    RetryCount  INT          NOT NULL DEFAULT 0,
    LastError   NVARCHAR(MAX) NULL;
```

## Configuration

Example `endpoints/Webhooks/Webhooks/Incoming/entity.json`, with namespace `Webhooks` and endpoint name `Incoming`:

```json
{
  "DatabaseObjectName": "WebhookData",
  "DatabaseSchema": "dbo",
  "AllowedColumns": [
    "payment_webhook",
    "shipping_webhook",
    "inventory_webhook"
  ]
}
```

### Configuration properties

| Property | Required | Type | Description |
|---|---|---|---|
| `DatabaseObjectName` | Yes | string | Table for webhook payloads |
| `DatabaseSchema` | No | string | Database schema (default `dbo`) |
| `AllowedColumns` | No | array | Accepted webhook ids; other ids return `404` |

The webhook id is stored in the `WebhookId` column.

## Sending webhooks

```
POST /api/{environment}/{namespace}/{name}/{webhookId}
```

```http
POST /api/prod/Webhooks/Incoming/payment_webhook
Content-Type: application/json
Authorization: Bearer <token>

{
  "event": "payment.success",
  "payment_id": "pay_123456",
  "amount": 99.99,
  "currency": "EUR",
  "timestamp": "2024-03-15T10:30:00Z"
}
```

Response (`201 Created`):

```json
{
  "success": true,
  "message": "Webhook processed successfully.",
  "result": null,
  "id": 12345
}
```

:::warning
Webhook endpoints require Bearer token authentication. Services that cannot send custom headers need a proxy or ingress layer that adds the token.
:::

## Querying stored payloads

Field extraction with SQL Server JSON functions:

```sql
-- Recent payloads for one webhook type
SELECT TOP 10
    Id,
    JSON_VALUE(Payload, '$.event')      AS EventType,
    JSON_VALUE(Payload, '$.payment_id') AS PaymentId,
    ReceivedAt
FROM WebhookData
WHERE WebhookId = 'payment_webhook'
ORDER BY ReceivedAt DESC;
```

## Limitations

- POST only
- JSON payloads only; other bodies are rejected
- No validation beyond JSON syntax and the webhook id
- No retry on insert failure
- No forwarding; a separate job or procedure processes the table
- Payload size limit: 10MB (default)
- [`Tenancy`](/guide/tenant-headers) is not supported

## Troubleshooting

| Symptom | Resolution |
|---|---|
| "Webhook ID '...' is not configured." | Add the id to `AllowedColumns` (case-insensitive match). |
| Database errors | Check the table definition and the connection account's INSERT permission. |
| `401` or `403` | Check the token and its access to the environment. |

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

## Next steps

- [SQL Endpoints](/guide/endpoints-sql)
- [Security](/guide/security)
- [Health and Logs](/guide/monitoring)
