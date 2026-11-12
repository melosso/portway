---
title: Pimcore Integration
description: "Proxy endpoint for the Pimcore Data Hub GraphQL API with the API key stored in the endpoint URL"
---

# Pimcore Integration

The Pimcore [Data Hub](https://pimcore.com/docs/data-hub/current/){target="_blank" rel="noopener"} exposes GraphQL endpoints secured with an API key in the `apikey` query parameter. The key is part of the endpoint `Url`; a client `apikey` parameter is dropped.

## Endpoint

```json [endpoints/Proxy/Pimcore/Products/entity.json]
{
  "Url": "https://pim.internal/pimcore-graphql-webservices/products?apikey=YOUR_DATAHUB_KEY",
  "Methods": ["POST"],
  "AllowedEnvironments": ["prod"]
}
```

The key applies to every environment the endpoint allows. Instances with different keys use separate endpoint files with their own `AllowedEnvironments`. Each Data Hub configuration defines the classes, fields and access of its schema; one configuration and key per integration limits what it can reach.

## Requests

```http
POST /api/prod/Pimcore/Products
Authorization: Bearer YOUR_PORTWAY_TOKEN
Content-Type: application/json

{ "query": "{ getProductListing(first: 25) { edges { node { id name sku } } } }" }
```

## Limitations

- All queries are `POST` requests to one endpoint per Data Hub configuration.
- Readable and writable fields are defined in Pimcore.
- A key rotation is an edit of the endpoint file, reloaded without a restart.

## Troubleshooting

| Symptom | Check |
|---|---|
| `403` from Pimcore | `apikey` in `Url`; Data Hub configuration enabled |
| `401` from Portway | Token scope for the endpoint and environment |
| Missing fields | Data Hub schema |
| Empty listings | Workspace permissions of the Data Hub configuration |
