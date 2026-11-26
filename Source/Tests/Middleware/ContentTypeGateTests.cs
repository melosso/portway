using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Middleware;

public sealed class ContentTypeGateTests : ApiTestBase
{
    public ContentTypeGateTests() => SetAllowedEnvironments("500", "700");

    [Theory]
    [InlineData("text/plain; x=application/json")]
    [InlineData("text/application/json")]
    [InlineData("application/json-patch+json")]
    public async Task NonJsonMediaType_Returns415(string contentType)
    {
        var response = await PostAsync(contentType);

        Assert.Equal(System.Net.HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("Application/JSON")]
    public async Task JsonMediaType_PassesContentGate(string contentType)
    {
        var response = await PostAsync(contentType);

        Assert.NotEqual(System.Net.HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    private Task<HttpResponseMessage> PostAsync(string contentType)
    {
        var content = new ByteArrayContent("""{"x":1}"""u8.ToArray());
        content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        return _client.PostAsync("/api/700/Inventory/Products", content, TestContext.Current.CancellationToken);
    }
}
