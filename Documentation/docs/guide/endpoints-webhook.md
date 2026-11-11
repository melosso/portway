---
title: Webhook Endpoints
description: "Receive HTTP POST payloads from external services and persist them to a SQL table"
---

# Webhook Endpoints

Webhook endpoints accept POST requests from external services and store the JSON payload in a configured table. The webhook id is checked against `AllowedColumns`, the request body is inserted as sent with a UTC timestamp, and the response returns the new row id.

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

Example with namespace `Webhooks` and endpoint name `Incoming`:

```json [endpoints/Webhooks/Webhooks/Incoming/entity.json]
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

`AllowedColumns` contains a list of the accepted webhook ids; other ids return `404`. An endpoint with an empty or absent `AllowedColumns` will not load and returns `404` for every id. The id is stored in the `WebhookId` column with the casing in `AllowedColumns`. The route is exactly `/api/{env}/{namespace}/{name}/{id}`: extra segments and quoted or parenthesised ids return `404`. All properties: [Entity configuration](/reference/entity-config#webhook).

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
  "message": "Webhook stored.",
  "result": null,
  "id": 12345
}
```

:::warning
A webhook id without `Verification` requires a Portway Bearer token. A sender that signs its deliveries uses `Verification` instead.
:::

## Verification

`Verification` sets one mode per webhook id. A verified id accepts its signature or credential only: Portway tokens and [environment authentication](/reference/environment-auth) do not apply to it. `Verification` requires an endpoint with a namespace.

```json [endpoints/Webhooks/Billing/Events/entity.json]
{
  "DatabaseObjectName": "WebhookData",
  "AllowedColumns": ["stripe_payments", "github_repo", "erp_orders"],
  "Verification": {
    "stripe_payments": { "Scheme": "Stripe", "Secret": "${secret:STRIPE-PAYMENTS}", "PreviousSecret": "${secret:STRIPE-PAYMENTS-OLD}" },
    "github_repo": { "Scheme": "GitHub", "Secret": "${secret:GITHUB-HOOK}", "Deduplicate": true },
    "erp_orders": { "Scheme": "SharedSecret", "Header": "X-Hook-Key", "Secret": "${secret:ERP-HOOK}", "DeliveryIdHeader": "X-Message-Id", "Deduplicate": true }
  }
}
```

`Secret` and `PreviousSecret` are [secret references](/reference/secrets#secret-store) in the request environment. A literal value, an unknown scheme or an invalid field makes that id answer `401` and logs one startup error. Use one secret per sender: two ids with the same secret log a startup warning.

### Modes

| `Scheme` | Sender | Fields |
|---|---|---|
| `GitHub`, `Stripe`, `Slack`, `Shopify`, `Jira`, `Mollie`, `ShipHero` | Presets | `Secret`, `PreviousSecret`, `Deduplicate`, `AcceptForm` |
| `StandardWebhooks` | Senders following [Standard Webhooks](https://github.com/standard-webhooks/standard-webhooks) (`whsec_` keys) | `Secret`, `PreviousSecret`, `Deduplicate`, `AcceptForm` |
| `Hmac` | Any sender with an HMAC-SHA256 signature | Table below |
| `SharedSecret` | A sender with a static credential | `Header`, or `Username` for HTTP Basic |

| Preset | Signature header | Signed content | Delivery id |
|---|---|---|---|
| `GitHub` | `X-Hub-Signature-256` (`sha256=`, hex) | Body | `X-GitHub-Delivery` |
| `Stripe` | `Stripe-Signature` (`t=`, `v1=`) | `{t}.{body}`, 300 s window | Body `id` |
| `Slack` | `X-Slack-Signature` (`v0=`), `X-Slack-Request-Timestamp` | `v0:{ts}:{body}`, 300 s window | Body `event_id` |
| `Shopify` | `X-Shopify-Hmac-SHA256` (Base64) | Body | `X-Shopify-Webhook-Id` |
| `Jira` | `X-Hub-Signature` (`sha256=`, hex) | Body | `X-Atlassian-Webhook-Identifier` |
| `Mollie` | `X-Mollie-Signature` (`sha256=`, hex) | Body | Body `id` |
| `ShipHero` | `x-shiphero-hmac-sha256` (Base64) | Body | `X-Shiphero-Message-ID` |

`Hmac` fields:

| Field | Values | Default |
|---|---|---|
| `Header` | Signature header | Required |
| `Prefix` | Literal before the digest, such as `sha256=` | Empty |
| `Encoding` | `Hex`, `Base64` | Required |
| `KeyEncoding` | `Utf8`, `Base64` | `Utf8` |
| `Format` | `Plain`, or `TimestampList` for a `t=<unix>,v1=<digest>` header | `Plain` |
| `SignedContent` | Literals with `{body}` exactly once, and optionally `{timestamp}` and `{id}` | Required |
| `TimestampHeader` | Unix seconds header. Required when `SignedContent` contains `{timestamp}` and `Format` is `Plain` | None |
| `IdHeader` | Required when `SignedContent` contains `{id}` | None |
| `ToleranceSeconds` | 60 to 900 | 300 |
| `DeliveryIdHeader` or `DeliveryIdField` | Header, or top-level JSON field | None |
| `DedupeKey` | `DeliveryId`, or `BodyHash` for the SHA-256 of the body | `DeliveryId` |

Rules:

- Signatures are compared in constant time on the raw body. `PreviousSecret` is tried after `Secret`.
- A key shorter than 16 bytes, a repeated signature, timestamp or id header, and a timestamp outside the window are refused.
- SHA-256 is the only hash. A mode that signs neither a timestamp nor an id logs a startup warning: a captured delivery can be replayed.
- A failed verification answers `401`, the same as a request without credentials. The log entry contains the scheme and the reason.
- Verification header names are redacted in traffic logs. The OpenAPI document lists verified ids with their header names.

## Deduplication

`Deduplicate: true` stores a delivery once per webhook id and delivery id. A duplicate answers `200` with `Duplicate delivery ignored.` A missing delivery id, or one longer than 255 characters, answers `400`.

The table requires a `DeliveryId` column and a unique index:

::: code-group
```sql [SQL Server]
ALTER TABLE dbo.WebhookData ADD DeliveryId NVARCHAR(255) NULL;
CREATE UNIQUE INDEX UX_WebhookData_Delivery ON dbo.WebhookData (WebhookId, DeliveryId) WHERE DeliveryId IS NOT NULL;
```
```sql [PostgreSQL]
ALTER TABLE public."WebhookData" ADD COLUMN "DeliveryId" VARCHAR(255) NULL;
CREATE UNIQUE INDEX "UX_WebhookData_Delivery" ON public."WebhookData" ("WebhookId", "DeliveryId");
```
```sql [MySQL / MariaDB]
ALTER TABLE WebhookData ADD COLUMN DeliveryId VARCHAR(255) NULL;
CREATE UNIQUE INDEX UX_WebhookData_Delivery ON WebhookData (WebhookId, DeliveryId);
```
```sql [SQLite]
ALTER TABLE WebhookData ADD COLUMN DeliveryId TEXT NULL;
CREATE UNIQUE INDEX UX_WebhookData_Delivery ON WebhookData (WebhookId, DeliveryId);
```
:::

Without the unique index, `Deduplicate` stores duplicates. The index also covers concurrent deliveries and multiple instances.

:::warning
A header delivery id removes sender retries. It does not stop a replay with a changed id. `DedupeKey: BodyHash` blocks that replay and also drops two legitimate deliveries with an identical body.
:::

## Content types

Webhook routes accept `application/json` and `application/*+json`. `AcceptForm: true` on a verified id stores an `application/x-www-form-urlencoded` body as raw text; the signature covers the raw body. Other content types answer `415`. A `Hidden` endpoint accepts `application/json` only, and `AcceptForm` on a `Hidden` endpoint is a load error.

A `Slack` id answers a signed `url_verification` request with its challenge and stores nothing.

## Deliveries in the console

**Endpoints** > webhook > **Deliveries** lists the latest 50 rows per environment, with the payload on expand. The view requires an administrator account.

## Processing and retention

A [SQL endpoint](/guide/endpoints-sql) over the webhook table with `WriteMode: Table` lets a job read pending rows and mark them processed:

```http
GET /api/prod/Webhooks/Pending?$filter=ProcessedAt eq null&$top=100
PATCH /api/prod/Webhooks/Pending
{ "Id": 42, "ProcessedAt": "2026-11-11 10:00:00" }
```

On PostgreSQL, a `timestamptz` column refuses a text value over the API; mark rows with a stored procedure there.

Portway does not delete webhook rows. Example cleanup of rows older than 90 days:

::: code-group
```sql [SQL Server]
DELETE FROM dbo.WebhookData WHERE ReceivedAt < DATEADD(day, -90, SYSUTCDATETIME());
```
```sql [PostgreSQL]
DELETE FROM public."WebhookData" WHERE "ReceivedAt" < now() - interval '90 days';
```
```sql [MySQL / MariaDB]
DELETE FROM WebhookData WHERE ReceivedAt < UTC_TIMESTAMP() - INTERVAL 90 DAY;
```
```sql [SQLite]
DELETE FROM WebhookData WHERE ReceivedAt < datetime('now', '-90 days');
```
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
- JSON payloads, plus form bodies on ids with `AcceptForm`
- No validation beyond JSON syntax and the webhook id. A malformed or empty body returns `400`.
- No retry on insert failure; the sender retry is the recovery path
- No forwarding; a separate job or procedure processes the table
- Payload size limit: 10MB (default)
- [`Tenancy`](/guide/tenant-headers) is not supported

## Troubleshooting

| Symptom | Resolution |
|---|---|
| "Webhook not found." | Add the id to `AllowedColumns` (case-insensitive match), and check the route has exactly one segment after the endpoint name. |
| Database errors | Check the table definition and the connection account's INSERT permission. |
| `401` or `403` | Check the token and its access to the environment. |

Debug logging: [Log levels](/guide/monitoring#log-levels).
