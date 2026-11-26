using System.Text;
using System.Text.Json;
using PortwayApi.Classes;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// A deprecated composite endpoint sends the same lifecycle headers as a deprecated proxy endpoint
/// </summary>
public sealed class CompositeLifecycleHeadersTests : ApiTestBase, IDisposable
{
    private const string Env = "500";
    private static readonly string Dir = Path.Combine("endpoints", "Proxy", "LifecycleComposite");

    public CompositeLifecycleHeadersTests()
    {
        SetAllowedEnvironments(Env);
        Write("Step", new { Url = "http://127.0.0.1:1/step", Methods = new[] { "POST" }, AllowedEnvironments = new[] { Env } });
        Write("Flow", new
        {
            Type = "Composite",
            Url = "http://127.0.0.1:1/flow",
            Methods = new[] { "POST" },
            AllowedEnvironments = new[] { Env },
            Deprecated = true,
            DeprecatedSince = "2026-01-01T00:00:00Z",
            Sunset = "2027-01-01T00:00:00Z",
            CompositeConfig = new { Name = "Flow", Steps = new[] { new { Name = "Only", Endpoint = "LifecycleComposite/Step", Method = "POST" } } },
        });
        EndpointHandler.ReloadAllEndpoints();
        AddAuthorizationHeader();
    }

    public new void Dispose()
    {
        Directory.Delete(Dir, recursive: true);
        EndpointHandler.ReloadAllEndpoints();
        base.Dispose();
    }

    private static void Write(string name, object content)
    {
        Directory.CreateDirectory(Path.Combine(Dir, name));
        File.WriteAllText(Path.Combine(Dir, name, "entity.json"), JsonSerializer.Serialize(content));
    }

    [Fact]
    public async Task DeprecatedComposite_SendsLifecycleHeaders()
    {
        var response = await _client.PostAsync($"/api/{Env}/LifecycleComposite/Flow",
            new StringContent("{}", Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        Assert.Equal("@1767225600", response.Headers.GetValues("Deprecation").Single());
        Assert.Equal("Fri, 01 Jan 2027 00:00:00 GMT", response.Headers.GetValues("Sunset").Single());
    }

    [Fact]
    public async Task CompositeInAnotherEnvironment_IsRefused()
    {
        SetAllowedEnvironments(Env, "600");

        var response = await _client.PostAsync("/api/600/LifecycleComposite/Flow",
            new StringContent("{}", Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not allowed for this endpoint", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
