using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PortwayApi.Services.Mcp;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// Real /mcp requests through the middleware pipeline
/// </summary>
public sealed class McpPipelineTests : ApiTestBase
{
    public McpPipelineTests() : base(connectTimeoutSeconds: 1, mcpEnabled: true)
    {
    }

    private async Task<(HttpStatusCode Status, string Body)> Post(string payload, string? origin = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (origin is not null)
            request.Headers.Add("Origin", origin);

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private const string Initialize =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""";

    private static JsonElement ResultOf(string body)
    {
        var data = body.Split('\n').First(line => line.StartsWith("data: ", StringComparison.Ordinal))["data: ".Length..];
        return JsonDocument.Parse(data).RootElement.GetProperty("result");
    }

    [Fact]
    public async Task ForeignOrigin_Returns403()
    {
        var (status, body) = await Post(Initialize, "https://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Contains("Origin not allowed", body);
    }

    [Fact]
    public async Task AllowedOrigin_IsServed()
    {
        Assert.Equal(HttpStatusCode.OK, (await Post(Initialize, "https://console.example")).Status);
    }

    [Fact]
    public async Task Initialize_ReportsThePortwayVersion()
    {
        var (status, body) = await Post(Initialize);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(McpServiceExtensions.ServerVersion(), ResultOf(body).GetProperty("serverInfo").GetProperty("version").GetString());
    }

    [Fact]
    public async Task FailedToolCall_SetsIsError()
    {
        var (status, body) = await Post(
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"call_endpoint","arguments":{"endpointName":"missing","environment":"500"}}}""");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(ResultOf(body).GetProperty("isError").GetBoolean());
    }
}
