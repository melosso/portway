---
title: MCP Server
description: "Expose Portway endpoints as Model Context Protocol tools that AI agents can discover and call"
---

# MCP Server

Portway hosts an MCP server over HTTP. Endpoints with `Mcp.Exposed: true` are registered as tools for MCP clients (e.g. Claude Desktop, VS Code Copilot, custom agents) and the built-in [Chat UI](/guide/mcp-chat). Every tool call runs under the caller's token, scopes and environments.

## Enable the MCP server

Setting `Mcp:Enabled: true` in `appsettings.json` enables the server at `Mcp:Path` (default `/mcp`).

```json
"Mcp": {
  "Enabled": true,
  "Path": "/mcp",
  "RequireAuthentication": true,
  "AppsEnabled": true,
  "ChatEnabled": true
}
```

| Field | Required | Type | Description |
|---|---|---|---|
| `Enabled` | Yes | bool | Enables the MCP server (default `false`) |
| `Path` | No | string | Server path (default `/mcp`) |
| `RequireAuthentication` | No | bool | Requires a Portway Bearer token (default `true`) |
| `AppsEnabled` | No | bool | Registers embedded UI resources as MCP resource URIs (default `true`) |
| `ChatEnabled` | No | bool | Enables the Chat UI and `/ui/api/mcp/chat`; provider credentials are set in the setup wizard (default `false`) |

:::warning
Keep `RequireAuthentication: true` on any network-reachable deployment; `false` exposes all registered tools without credentials.
:::

## Expose an endpoint as an MCP tool

Setting `Mcp.Exposed: true` in `entity.json` registers one tool per HTTP method of the endpoint.

```json
{
  "DatabaseObjectName": "OutstandingItems",
  "DatabaseSchema": "dbo",
  "AllowedEnvironments": ["500","700"],
  "AllowedMethods": ["GET"],
  "Mcp": {
    "Exposed": true,
    "Instruction": "Always include a $filter on AccountCode. Results are paginated; use $top and $skip."
  }
}
```

| Field | Required | Type | Description |
|---|---|---|---|
| `Exposed` | Yes | bool | Registers the endpoint as MCP tools (default `false`) |
| `Instruction` | No | string | Text appended to the tool description for the model, e.g. required filters or data shape |

Tool names follow `{namespace}_{name}_{method}`, or `{name}_{method}` without a namespace (e.g. `products_GET`). `Instruction` changes only the model-facing description, not the Explorer summary.

Tools for endpoints with [tenant headers](/guide/security#tenant-headers) list them in `GetEndpointInfo` (`TenantHeaders`) and accept their values in the `tenants` argument of `CallEndpoint`, e.g. `{"X-Company-Id": "ACME"}`.

## Namespaces

The Explorer and `ListEndpoints` group tools by `Namespace`; tools without one are listed under `default`.

```json
{
  "Namespace": "inventory",
  "Mcp": {
    "Exposed": true
  }
}
```

Configuration: [Namespaces](/reference/namespaces).

## Built-in server tools

Available in every session with `Mcp:Enabled: true`:

| Tool | Description |
|---|---|
| `ListEndpoints` | Returns all registered tools grouped by namespace |
| `GetEndpointInfo` | Returns URL, methods, environments and tenant headers of an endpoint |
| `ListUiEnabledEndpoints` | Returns endpoints with an embedded UI resource |
| `CallEndpoint` | Calls an endpoint with an environment, OData query, JSON body and tenant values |

## Connect an MCP client

MCP-over-HTTP endpoint:

```
http(s)://{host}{Mcp:Path}
```

With `RequireAuthentication: true`, requests include a Portway Bearer token:

```
Authorization: Bearer {token}
```

Scope agent tokens to the required endpoints and environments: [Access Tokens](/guide/tokens).

## Web UI

Console views under **MCP**:

- **Explorer** (`/ui/mcp/explorer`): registered tools by namespace, with methods and endpoint links; requires only `Mcp:Enabled: true`
- **Chat** (`/ui/mcp/chat`): an AI model calling Portway tools; requires [Chat configuration](/guide/mcp-chat)

## Next steps

- [MCP Chat](/guide/mcp-chat)
- [Access Tokens](/guide/tokens)
- [Namespaces](/reference/namespaces)
- [Entity configuration](/reference/entity-config): full `entity.json` reference including `Exposed`
