using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using PortwayApi.Classes;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

public sealed class ProxyBinaryBodyTests : ApiTestBase
{
    private static readonly string EndpointDir = Path.Combine("endpoints", "Proxy", "BinaryBody");

    private static async Task<WebApplication> StartUpstream(byte[] body, string contentType)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapGet("/blob", async (HttpContext c) =>
        {
            c.Response.ContentType = contentType;
            await c.Response.Body.WriteAsync(body);
        });
        await app.StartAsync();
        return app;
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("application/pdf")]
    [InlineData("application/octet-stream")]
    public async Task BinaryUpstreamBody_IsServedByteForByte(string contentType)
    {
        var body = new byte[4096];
        new Random(1).NextBytes(body);
        await using var upstream = await StartUpstream(body, contentType);
        Directory.CreateDirectory(EndpointDir);
        File.WriteAllText(Path.Combine(EndpointDir, "entity.json"), JsonSerializer.Serialize(new
        {
            Url = upstream.Urls.First() + "/blob",
            Methods = new[] { "GET" },
            AllowedEnvironments = new[] { "500" }
        }));
        EndpointHandler.ReloadAllEndpoints();

        try
        {
            using var response = await _client.GetAsync("/api/500/BinaryBody", TestContext.Current.CancellationToken);

            Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(body, await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(EndpointDir, recursive: true);
            EndpointHandler.ReloadAllEndpoints();
        }
    }
}
