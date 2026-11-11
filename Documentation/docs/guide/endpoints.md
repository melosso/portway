---
title: Endpoints
description: "Endpoint types for SQL, proxy, composite, static, file and webhook resources"
---

# Endpoints

An endpoint is a folder with an `entity.json` file under `endpoints/`. Each endpoint type has its own folder: `SQL/`, `Proxy/`, `Static/`, `Files/` and `Webhooks/`. Portway reloads endpoint files on change.

::: cards 3
- [SQL Endpoints](/guide/endpoints-sql)
  Tables, views, stored procedures and table-valued functions with OData filtering.
- [Proxy Endpoints](/guide/endpoints-proxy)
  Internal HTTP and HTTPS services behind one authenticated gateway URL.
- [Composite Endpoints](/guide/endpoints-composite)
  Several proxy calls in sequence from one request, with values passed between steps.
- [Static Endpoints](/guide/endpoints-static)
  Pre-defined JSON, XML or CSV files with optional OData filtering.
- [File Endpoints](/guide/endpoints-file)
  File upload, download and listing through authenticated API calls.
- [Webhook Endpoints](/guide/endpoints-webhook)
  Inbound HTTP POST payloads stored in a SQL table.
:::

Endpoint versions: [Versions](/guide/endpoint-versions).
