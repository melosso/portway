# MCP Server

> Expose Portway endpoints as Model Context Protocol tools that AI agents can discover and call.

Portway implements an MCP server over HTTP. When you flag an endpoint with `Exposed: true`, it appears in the MCP tool registry and becomes callable by any MCP-compatible client — Claude Desktop, VS Code Copilot, custom agents, or the built-in [Chat UI](/guide/mcp-chat). Portway's own authentication and environment scoping apply to every tool call.

## Enable the MCP server

Set `Mcp:Enabled` to `true` in `appsettings.json`. The server mounts at the path defined by `Mcp:Path` (default `/mcp`).

```json
"Mcp": {
  "Enabled": true,
  "Path": "/mcp",
  "RequireAuthentication": true,
  "AppsEnabled": true
}
```

| Field | Required | Type | Description |
|---|---|---|---|
| `Enabled` | Yes | bool | Activates the MCP server. Default: `false`. |
| `Path` | No | string | HTTP path the MCP server is mounted on. Default: `/mcp`. |
| `RequireAuthentication` | No | bool | Require a valid Portway Bearer token on MCP requests. Default: `true`. |
| `AppsEnabled` | No | bool | Register embedded UI resources as MCP resource URIs. Default: `true`. |

:::warning
Set `RequireAuthentication: true` in any deployment accessible over a network. Disabling it exposes all registered tools without a credential check.
:::

## Expose an endpoint as an MCP tool

Add `"Exposed": true` to the endpoint's `entity.json` under the `Mcp` object. Portway registers one tool per HTTP method the endpoint supports.

```json
{
  "DatabaseObjectName": "OutstandingItems",
  "DatabaseSchema": "dbo",
  "AllowedEnvironments": ["500","700"],
  "AllowedMethods": ["GET"],
  "Mcp": {
    "Exposed": true,
    "Instruction": "Always include a $filter on AccountCode. Results are paginated — use $top and $skip."
  }
}
```

| Field | Required | Type | Description |
|---|---|---|---|
| `Exposed` | Yes | bool | Registers this endpoint as an MCP tool. Default: `false`. |
| `Instruction` | No | string | Text appended to the tool's LLM-facing description. Use it to guide the model on required parameters, filtering conventions, or data shape. |

The tool name in the registry is derived from the endpoint's namespace and name: `{namespace}_{name}_{method}`. If the endpoint has no namespace the name is `{name}_{method}`, for example `products_GET`.

Endpoints where `Exposed` is absent or `false` do not appear in the tool list.

`Instruction` does not affect the human-readable summary shown in the Explorer UI — it only extends the description the AI model receives when deciding whether and how to call the tool.

## Namespaces

Group related tools by setting `Namespace` in `entity.json`. The MCP Explorer UI and the `ListEndpoints` tool both group by namespace. Without a namespace, tools appear under `default`.

```json
{
  "Namespace": "inventory",
  "Mcp": {
    "Exposed": true
  }
}
```

See [Namespaces](/reference/namespaces) for full configuration details.

## Built-in server tools

Portway registers three tools in every MCP session regardless of which endpoints are exposed:

| Tool | Description |
|---|---|
| `ListEndpoints` | Returns all registered tools grouped by namespace |
| `GetEndpointInfo` | Returns URL, methods, and environment constraints for a named endpoint |
| `ListUiEnabledEndpoints` | Returns endpoints that have an embedded UI resource |

These tools are always available once `Mcp:Enabled` is `true`. They do not require `Exposed` on any endpoint.

## Connect an MCP client

Point any MCP-over-HTTP client at:

```
http(s)://{host}{Mcp:Path}
```

Include a Portway Bearer token in the `Authorization` header if `RequireAuthentication` is `true`:

```
Authorization: Bearer {token}
```

Tokens are managed through the Web UI or the token API. Scope tokens to the environments and endpoints the agent is allowed to access. See [Access Tokens](/guide/tokens).

## Web UI

The Web UI provides two views under **MCP** in the sidebar:

- **Explorer** (`/ui/mcp/explorer`) — lists all registered tools grouped by namespace, shows allowed methods, and links to endpoint detail
- **Chat** (`/ui/mcp/chat`) — conversational interface where an AI model calls Portway tools on your behalf

The Explorer requires no additional configuration beyond `Mcp:Enabled: true`. The Chat view requires [Chat configuration](/guide/mcp-chat).

## Next steps

- [MCP Chat](/guide/mcp-chat): configure an AI provider for the Chat UI
- [Access Tokens](/guide/tokens): scope tokens for MCP clients
- [Namespaces](/reference/namespaces): group tools logically
- [Entity configuration](/reference/entity-config): full `entity.json` reference including `Exposed`
