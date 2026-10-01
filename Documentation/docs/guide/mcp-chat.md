---
title: MCP Chat
description: "Ask questions and trigger operations against your Portway endpoints through a conversational AI interface"
---

# MCP Chat

MCP Chat connects an AI model to the Portway MCP tools. The model selects tools; each call goes through the regular API with authentication, rate limiting and environment scoping. Responses are streamed, and each tool call is displayed as a collapsible panel.

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

The setup wizard opens on the first visit to `/ui/mcp/chat`:

1. Provider and model: Anthropic, OpenAI, Gemini or Mistral.
2. API key: encrypted with the machine-bound PWENC key before it is written to `mcp.db`.

The wizard is available from the Chat page to change credentials.

## Supply the API key via environment variable

The environment variable `PORTWAY_CHAT_API_KEY` overrides the key stored in `mcp.db`:

```bash
export PORTWAY_CHAT_API_KEY=sk-ant-...
```

With the variable set, the wizard and the status endpoint report `api_key_source: environment`. PWENC encryption: [Secret Encryption](/reference/secrets).

## Supported providers

### Anthropic

```json
"Provider": "Anthropic",
"Model": "claude-sonnet-5"
```

Anthropic Messages API with streaming and tool use. API keys: [console.anthropic.com](https://console.anthropic.com).

### OpenAI

```json
"Provider": "OpenAI",
"Model": "gpt-5.5"
```

OpenAI Chat Completions API with streaming function calling.

### Gemini

```json
"Provider": "Gemini",
"Model": "gemini-3.5-flash"
```

Google Generative Language API (`streamGenerateContent`) with function declarations.

### Mistral / Codestral

```json
"Provider": "Mistral",
"Model": "codestral-latest"
```

Mistral Chat Completions API. Codestral models (`codestral-*`) use `codestral.mistral.ai` and a Codestral API key; other models use `api.mistral.ai`.

## How tool calls work

Each chat turn runs up to five tool rounds:

1. The conversation and the exposed tools are sent to the model.
2. The model returns text or tool calls.
3. Each tool call requests `{baseUrl}/api/{environment}/{endpoint}` with the model's method, query, body and tenant values, under the user's token.
4. Tool results are added to the conversation for the next round.
5. The loop ends with a text response or after five rounds.

## Environment selector

The environment selector is populated from `GET /ui/api/environments`. Tool calls use the selected environment unless the message names another one.

## Next steps

- [MCP Server](/guide/mcp)
- [Secret Encryption](/reference/secrets)
- [Access Tokens](/guide/tokens)
