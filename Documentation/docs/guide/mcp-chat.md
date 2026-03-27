# MCP Chat

> Ask questions and trigger operations against your Portway endpoints through a conversational AI interface.

MCP Chat connects an AI model to the Portway MCP tool registry. When you send a message, the model decides which tools to call, executes them through Portway's normal API layer (authentication, rate limiting, and environment scoping all apply), and incorporates the results into its response. The chat interface streams responses and shows each tool call as a collapsible panel.

:::info
MCP Chat requires the MCP server to be enabled. Set `Mcp:Enabled: true` and expose at least one endpoint with `"Mcp": { "Exposed": true }` before configuring Chat.
:::

## Enable Chat

Set `Mcp:ChatEnabled` to `true` in `appsettings.json`. This activates the Chat UI and the `/ui/api/mcp/chat` SSE endpoint. The MCP server must also be enabled.

```json
"Mcp": {
  "Enabled": true,
  "ChatEnabled": true
}
```

Provider, model, and API key are **not** stored in `appsettings.json`. They are configured through the setup wizard and stored in the encrypted `mcp.db` database.

## Configure a provider

On first visit to the Chat UI (`/ui/mcp/chat`), a setup wizard opens automatically. It walks through two steps:

1. **Choose a provider** — select Anthropic, OpenAI, Gemini, or Mistral and pick a model.
2. **Enter credentials** — paste the API key. Portway encrypts it using the machine-bound PWENC key before writing it to `mcp.db`. The plaintext key is never stored.

The wizard can be re-opened at any time from the Chat page if credentials need to change.

## Supply the API key via environment variable

When deploying via Docker or a process manager, set the API key as an environment variable instead of entering it through the wizard. Portway checks this variable first and uses it if present, ignoring the database value.

```bash
export PORTWAY_CHAT_API_KEY=sk-ant-...
```

The wizard and the status endpoint will report `api_key_source: environment` when the variable is set.

See [Secret Encryption](/reference/secrets) for how PWENC database encryption works.

## Supported providers

### Anthropic

```json
"Provider": "Anthropic",
"Model": "claude-sonnet-4-6"
```

Uses the Anthropic Messages API with streaming and native tool use. Obtain an API key from [console.anthropic.com](https://console.anthropic.com).

### OpenAI

```json
"Provider": "OpenAI",
"Model": "gpt-4o"
```

Uses the OpenAI Chat Completions API with streaming function calling.

### Gemini

```json
"Provider": "Gemini",
"Model": "gemini-2.0-flash"
```

Uses the Google Generative Language API (`streamGenerateContent`) with function declarations.

### Mistral / Codestral

```json
"Provider": "Mistral",
"Model": "codestral-latest"
```

Uses the Mistral Chat Completions API. Codestral models (`codestral-*`) route to `codestral.mistral.ai`; all other Mistral models route to `api.mistral.ai`. Codestral requires a Codestral-specific API key — general Mistral API keys do not work against the Codestral endpoint.

## How tool calls work

Each chat turn runs a tool-use loop up to five rounds deep:

1. Portway sends the conversation history and the full list of exposed MCP tools to the model.
2. The model either responds with text or requests one or more tool calls.
3. For each tool call, Portway calls the corresponding endpoint at `{baseUrl}/api/{environment}/{endpoint}`, using the method, query string, and body parameters the model provided.
4. The tool result is added to the conversation and the model receives it in the next round.
5. The loop repeats until the model produces a final text response or the five-round limit is reached.

Every tool call goes through the standard Portway request path: the environment selector in the chat UI controls which environment segment is used, and all endpoint-level access rules apply.

## Environment selector

The Chat UI includes an environment dropdown populated from `GET /ui/api/environments`. Select the target environment before sending a message. The model uses this environment for all tool calls in the session unless you specify a different one explicitly in your message.

## Next steps

- [MCP Server](/guide/mcp): enable the MCP server and expose endpoints as tools
- [Secret Encryption](/reference/secrets): how PWENC key storage works
- [Access Tokens](/guide/tokens): scope tokens for MCP tool execution
