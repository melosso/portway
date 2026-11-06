using System.Net;
using System.Text;
using System.Text.Json;
using PortwayApi.Classes;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

public sealed class CompositeArrayPropertyTests : ApiTestBase, IDisposable
{
    private const string Env = "500";
    private static readonly string Dir = Path.Combine("endpoints", "Proxy", "ArrayProperty");

    public CompositeArrayPropertyTests()
    {
        SetAllowedEnvironments(Env);
        Write("Step", new { Url = "http://localhost:8024/step", Methods = new[] { "POST" }, AllowedEnvironments = new[] { Env } });
        Write("Flow", new
        {
            Type = "Composite",
            Url = "http://localhost:8024/flow",
            Methods = new[] { "POST" },
            AllowedEnvironments = new[] { Env },
            CompositeConfig = new
            {
                Name = "Flow",
                Steps = new[] { new { Name = "Lines", Endpoint = "ArrayProperty/Step", Method = "POST", IsArray = true, ArrayProperty = "Lines" } }
            }
        });
        EndpointHandler.ReloadAllEndpoints();
    }

    public new void Dispose()
    {
        Directory.Delete(Dir, recursive: true);
        EndpointHandler.ReloadAllEndpoints();
        base.Dispose();
    }

    private static void Write(string name, object entity)
    {
        Directory.CreateDirectory(Path.Combine(Dir, name));
        File.WriteAllText(Path.Combine(Dir, name, "entity.json"), JsonSerializer.Serialize(entity));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Lines\":\"not an array\"}")]
    public async Task MissingOrInvalidProperty_Returns400_NamingTheProperty(string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/{Env}/ArrayProperty/Flow")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new("Bearer", "test-token");

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Lines", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
