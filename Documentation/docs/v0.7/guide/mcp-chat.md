---
title: MCP Chat
description: "Chat assistant that queries and runs operations on Portway endpoints through MCP tools"
---

# MCP Chat

MCP Chat connects an AI model to the Portway MCP tools. The model selects tools; each call goes through the regular API with authentication, rate limiting and environment scoping. Responses are streamed.

:::info
MCP Chat requires `Mcp:Enabled: true` and at least one endpoint with `"Mcp": { "Exposed": true }`.
:::

## Enable chat

Setting `Mcp:ChatEnabled: true` in `appsettings.json` enables the Chat UI and the `/ui/api/mcp/chat` SSE endpoint.

```json
"Mcp": {
  "Enabled": true,
  "ChatEnabled": true
}
```

Provider, model and API key are set in the setup wizard and stored encrypted in `mcp.db`, not in `appsettings.json`.

## Configure a provider

The setup wizard opens on the first visit to `/ui/mcp/chat` and under **Settings → AI**. It sets the provider, the model and the API key. The API key is encrypted (`PWENC:`) before it is written to `mcp.db`.

## Supply the API key via environment variable

The environment variable `PORTWAY_CHAT_API_KEY` overrides the key stored in `mcp.db`:

::: code-group

```bash [Linux]
export PORTWAY_CHAT_API_KEY=sk-ant-...
```

```powershell [Windows]
[Environment]::SetEnvironmentVariable('PORTWAY_CHAT_API_KEY', 'sk-ant-...', 'Machine')
```

:::

With the variable set, the wizard and the status endpoint report `api_key_source: environment`. PWENC encryption: [Secret Encryption](/reference/secrets).

## Supported providers

| Provider | API | Example model |
|---|---|---|
| Anthropic | Messages API with streaming and tool use | `claude-sonnet-5` |
| OpenAI | Chat Completions API with streaming function calling | `gpt-5.5` |
| Gemini | Generative Language API (`streamGenerateContent`) with function declarations | `gemini-3.5-flash` |
| Mistral | Chat Completions API; `codestral-*` models use `codestral.mistral.ai` and a Codestral API key | `codestral-latest` |

## How tool calls work

Each chat turn runs up to five tool rounds:

1. The conversation and the exposed tools are sent to the model.
2. The model returns text or tool calls.
3. Each tool call requests `{baseUrl}/api/{environment}/{endpoint}` with the model's method, query, body and tenant values, under the user's token.
4. Tool results are added to the conversation for the next round.
5. The loop ends with a text response or after five rounds.

## Environment selector

The environment selector lists the environments from `GET /ui/api/environments`. A tool call uses the environment the model passes, or the selected environment. An environment outside the endpoint's `AllowedEnvironments` is replaced by the endpoint's first allowed environment.

## Next steps

- [MCP Server](/guide/mcp)
- [Secret Encryption](/reference/secrets)
- [Access Tokens](/guide/tokens)
