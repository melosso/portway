---
title: Pimcore Integration
description: "Front Pimcore's Data Hub GraphQL API with Portway, keeping the API key server-side and your integrations on scoped gateway tokens"
---

# Pimcore Integration

Pimcore is a self-hostable PIM and DAM platform. Its [Data Hub](https://pimcore.com/docs/data-hub/current/){target="_blank" rel="noopener"} module exposes product data through GraphQL endpoints, each secured with a static API key. Portway can act as a gateway in front of those endpoints. The API key stays in the gateway configuration, and your integrations authenticate against Portway with their own scoped tokens.

## Overview

The integration uses a proxy endpoint to forward GraphQL requests to Data Hub. Data Hub expects its API key as an `apikey` query parameter on the endpoint URL. That key goes into the endpoint definition on the Portway side, so clients never handle it. They authenticate against Portway as usual and send plain GraphQL queries.

## Configuration

### Data Hub Endpoint

In Pimcore, create a Data Hub configuration and note two things: the endpoint name and the API key. Both go into the proxy endpoint URL:

```json [endpoints/Proxy/Pimcore/Products/entity.json]
{
  "Url": "https://pim.internal/pimcore-graphql-webservices/products?apikey=YOUR_DATAHUB_KEY",
  "Methods": ["POST"],
  "AllowedEnvironments": ["prod"]
}
```

GraphQL rides entirely on `POST` bodies, so no client query parameters are involved and the embedded key travels with every forwarded request.

::: Note
The key sits in the endpoint definition rather than the environment headers, so it is shared across all environments the endpoint allows. If your production and test Pimcore instances use different keys, give each its own endpoint file with a matching `AllowedEnvironments` list.
:::

### Scoping in Pimcore

Each Data Hub configuration defines its own schema: which classes, which fields, read or read-write. Field curation happens there. A narrow configuration per integration keeps the exposed surface small, and each one gets its own key.

## Usage

Clients send GraphQL queries with their Portway bearer token:

```http
POST /api/prod/Pimcore/Products
Authorization: Bearer YOUR_PORTWAY_TOKEN
Content-Type: application/json

{
  "query": "{ getProductListing(first: 25) { edges { node { id name sku } } } }"
}
```

Responses come back as standard GraphQL JSON envelopes, untouched by the gateway.

## What the gateway adds

Fronting Data Hub with Portway does useful work at every layer:

- **Credential isolation**: the Data Hub key never leaves the server; Portway tokens are scoped per endpoint and environment
- **Network isolation**: Pimcore stays internal; only Portway is exposed
- **Rate limiting**: per-IP and per-token limits protect the PIM from runaway clients
- **Traffic logging and metrics**: every query lands in the [traffic log](/reference/audit) and the `portway.endpoint` metrics dimension

## Things to keep in mind

A few properties of this setup deserve attention before you build on it:

- All calls are `POST` to one endpoint per Data Hub configuration. Caching applies poorly, since identical URLs carry different query bodies.
- What an integration can read or write is bounded by the Data Hub configuration in Pimcore, not by Portway. Review that schema when you review access.
- Data Hub endpoints and keys are managed in the Pimcore admin. Rotating a key means updating the endpoint file; endpoint files reload without a restart.

## Troubleshooting

Most problems here come down to the key or the Data Hub configuration, so start there:

| Symptom | Check |
|---------|-------|
| `403` from Pimcore | `apikey` value in the endpoint `Url`; key matches the Data Hub configuration; configuration enabled |
| `401` from Portway | Bearer token valid and scoped to the endpoint and environment |
| Fields missing from responses | The Data Hub configuration's schema includes the requested fields |
| Empty listings | Workspace permissions on the Data Hub configuration cover the objects being queried |
