---
title: What is Portway?
description: "Portway is a self-hosted API gateway that exposes databases, internal services and files through one authenticated REST and MCP interface"
keywords: [API Gateway, Docker, Windows, SQL Server, REST, OData, MCP]
---

# What is Portway?

Portway is a self-hosted API gateway that exposes database tables and views, internal web services and files through one documented REST API. Endpoints are defined in configuration files and require no application code.

## Use cases

Portway is installed on the internal network, next to the ERP database or internal services. Partners, applications and AI agents call the Portway API. The database and internal services require no access from external networks.

- **Share ERP data.** A partner or webshop reads stock levels, prices or order status from Exact, AFAS, SAP Business One or Business Central. The partner's access token is limited to those endpoints.
- **Feed reports and scripts.** BI tools, spreadsheets and scripts read views from the API. Filtering, sorting and paging are executed in the database.
- **Publish an internal service.** Portway forwards requests to a REST or SOAP service on the private network, with authentication and rate limiting.
- **Receive webhooks.** Payment, shipping and e-commerce platforms send events to Portway. Each payload is stored in a SQL table.
- **Exchange documents.** Clients upload and download invoices, exports and images in a folder per client.
- **Give AI assistants controlled access.** Claude, Copilot and other MCP clients call selected endpoints. Each call is limited by the user's access token.

## Building blocks

Every Portway installation is configured with three parts:

- **[Environments](/guide/environments)**: the target systems, e.g. `test` and `prod`, each with its own database connection. One endpoint definition serves every environment.
- **[Endpoints](/guide/endpoints-sql)**: the exposed resources, one folder per endpoint: a database object, an internal service, static content, a file store or a webhook.
- **[Access tokens](/guide/tokens)**: one per client, limited to endpoints, environments and, for multi-company data, companies.

## Next steps

Install Portway and add a first endpoint, or explore the live demo.

::: cards 3
- [Install Portway](/guide/getting-started)
  Docker, Linux or Windows Server, up to the first API call.
- [Your first endpoint](/guide/endpoints-sql)
  Expose a table, view or stored procedure.
- [Live demo](https://portway-demo.melosso.com/)
  Example SQL and proxy endpoints.
:::
