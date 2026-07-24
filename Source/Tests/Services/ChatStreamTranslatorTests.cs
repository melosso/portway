using System.Text.Json;
using System.Text.Json.Nodes;
using PortwayApi.Services.Mcp;
using PortwayApi.Services.Mcp.Providers;
using Xunit;

namespace PortwayApi.Tests.Services;

public class ChatStreamTranslatorTests
{
    private static JsonNode Parse(string json) => JsonNode.Parse(json)!;

    [Fact]
    public void OpenAi_TextDelta_YieldsText()
    {
        var translator = new OpenAiCompatibleStreamTranslator();

        var deltas = translator.Translate(Parse("""
            {"choices":[{"delta":{"content":"Hello"}}]}
            """)).ToList();

        var delta = Assert.Single(deltas);
        Assert.Equal(ChatDeltaType.Text, delta.Type);
        Assert.Equal("Hello", delta.Delta);
    }

    [Fact]
    public void OpenAi_ToolCall_AccumulatesArgumentsUntilFinishReason()
    {
        var translator = new OpenAiCompatibleStreamTranslator();

        Assert.Empty(translator.Translate(Parse("""
            {"choices":[{"delta":{"tool_calls":[{"function":{"name":"GetAccounts","arguments":"{\"env\":"}}]}}]}
            """)));

        Assert.Empty(translator.Translate(Parse("""
            {"choices":[{"delta":{"tool_calls":[{"function":{"arguments":"\"prod\"}"}}]}}]}
            """)));

        var deltas = translator.Translate(Parse("""
            {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}
            """)).ToList();

        var call = Assert.Single(deltas);
        Assert.Equal(ChatDeltaType.ToolCall, call.Type);
        Assert.Equal("GetAccounts", call.ToolName);
        Assert.Equal("""{"env":"prod"}""", call.ToolInput);
    }

    [Fact]
    public void OpenAi_SecondToolCall_StartsFromCleanState()
    {
        var translator = new OpenAiCompatibleStreamTranslator();

        translator.Translate(Parse("""
            {"choices":[{"delta":{"tool_calls":[{"function":{"name":"First","arguments":"{}"}}]}}]}
            """)).ToList();
        translator.Translate(Parse("""
            {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}
            """)).ToList();

        translator.Translate(Parse("""
            {"choices":[{"delta":{"tool_calls":[{"function":{"name":"Second","arguments":"{\"a\":1}"}}]}}]}
            """)).ToList();
        var second = translator.Translate(Parse("""
            {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}
            """)).Single();

        Assert.Equal("Second", second.ToolName);
        Assert.Equal("""{"a":1}""", second.ToolInput);
    }

    [Fact]
    public void Anthropic_ContentBlocks_YieldTextThenToolCall()
    {
        var translator = new AnthropicStreamTranslator();

        var text = translator.Translate(Parse("""
            {"type":"content_block_delta","delta":{"type":"text_delta","text":"Checking"}}
            """)).Single();
        Assert.Equal(ChatDeltaType.Text, text.Type);
        Assert.Equal("Checking", text.Delta);

        Assert.Empty(translator.Translate(Parse("""
            {"type":"content_block_start","content_block":{"type":"tool_use","name":"GetAccounts"}}
            """)));
        Assert.Empty(translator.Translate(Parse("""
            {"type":"content_block_delta","delta":{"type":"input_json_delta","partial_json":"{\"env\":\"prod\"}"}}
            """)));

        var call = translator.Translate(Parse("""{"type":"content_block_stop"}""")).Single();
        Assert.Equal(ChatDeltaType.ToolCall, call.Type);
        Assert.Equal("GetAccounts", call.ToolName);
        Assert.Equal("""{"env":"prod"}""", call.ToolInput);
    }

    [Fact]
    public void Anthropic_MessageStop_MarksStreamComplete()
    {
        var translator = new AnthropicStreamTranslator();
        Assert.False(translator.IsComplete);

        Assert.Empty(translator.Translate(Parse("""{"type":"message_stop"}""")));

        Assert.True(translator.IsComplete);
    }

    [Fact]
    public void Anthropic_ContentBlockStop_WithoutToolUse_YieldsNothing()
    {
        var translator = new AnthropicStreamTranslator();

        Assert.Empty(translator.Translate(Parse("""{"type":"content_block_stop"}""")));
    }

    [Fact]
    public void Gemini_Parts_YieldTextAndFunctionCall()
    {
        var translator = new GeminiStreamTranslator();

        var deltas = translator.Translate(Parse("""
            {"candidates":[{"content":{"parts":[
                {"text":"Looking it up"},
                {"functionCall":{"name":"GetAccounts","args":{"env":"prod"}}}
            ]}}]}
            """)).ToList();

        Assert.Equal(2, deltas.Count);
        Assert.Equal(ChatDeltaType.Text, deltas[0].Type);
        Assert.Equal("Looking it up", deltas[0].Delta);
        Assert.Equal(ChatDeltaType.ToolCall, deltas[1].Type);
        Assert.Equal("GetAccounts", deltas[1].ToolName);
        Assert.Equal("""{"env":"prod"}""", deltas[1].ToolInput);
    }

    [Fact]
    public void Gemini_ChunkWithoutCandidates_YieldsNothing()
    {
        var translator = new GeminiStreamTranslator();

        Assert.Empty(translator.Translate(Parse("""{"promptFeedback":{}}""")));
    }

    [Fact]
    public void ChatDelta_SerialisesForSse()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        var json = JsonSerializer.Serialize(
            new ChatDelta { Type = ChatDeltaType.ToolCall, ToolName = "GetAccounts" }, options);

        Assert.Contains("\"toolName\":\"GetAccounts\"", json);
    }

    [Fact]
    public void ToolPayloads_CarryTheSchemaUnderTheVendorProperty()
    {
        var tools = new[] { new ToolDefinition("GetAccounts", "Reads accounts", """{"type":"object"}""") };

        var anthropic = ChatPayloadFactory.Declarations(tools, "input_schema");
        var openAi    = ChatPayloadFactory.Functions(tools);

        Assert.Equal("object", anthropic[0]!["input_schema"]!["type"]!.GetValue<string>());
        Assert.Equal("function", openAi[0]!["type"]!.GetValue<string>());
        Assert.Equal("object", openAi[0]!["function"]!["parameters"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void ToolPayloads_FallBackToAnEmptySchemaWhenUnparseable()
    {
        var tools = new[] { new ToolDefinition("Broken", "Bad schema", "not json") };

        var declarations = ChatPayloadFactory.Declarations(tools, "parameters");

        Assert.Empty(declarations[0]!["parameters"]!.AsObject());
    }

    [Fact]
    public void GeminiContents_MapAssistantRoleToModel()
    {
        var history = new[]
        {
            new ChatMessage("user", "hi"),
            new ChatMessage("assistant", "hello")
        };

        var contents = ChatPayloadFactory.GeminiContents(history);

        Assert.Equal("user", contents[0]!["role"]!.GetValue<string>());
        Assert.Equal("model", contents[1]!["role"]!.GetValue<string>());
        Assert.Equal("hello", contents[1]!["parts"]![0]!["text"]!.GetValue<string>());
    }
}
