using System.Net;
using System.Text;
using System.Text.Json;
using PortwayApi.Classes;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// Composite steps and endpoint identity stay consistent across a reload
/// </summary>
public sealed class CompositeReloadTests : ApiTestBase, IDisposable
{
    private const string Env = "500";
    private const int UpstreamPort = 8022;
    private static readonly string Dir = Path.Combine("endpoints", "Proxy", "ReloadComposite");
    private readonly UpstreamCapture _upstream = new(UpstreamPort);

    public CompositeReloadTests()
    {
        SetAllowedEnvironments(Env);
        Write("Step", new { Url = $"http://localhost:{UpstreamPort}/step", Methods = new[] { "POST" }, AllowedEnvironments = new[] { Env } });
        Write("Flow", new
        {
            Type = "Composite",
            Url = $"http://localhost:{UpstreamPort}/flow",
            Methods = new[] { "POST" },
            AllowedEnvironments = new[] { Env },
            CompositeConfig = new { Name = "Flow", Steps = new[] { new { Name = "Only", Endpoint = "ReloadComposite/Step", Method = "POST" } } },
        });
        EndpointHandler.ReloadAllEndpoints();
    }

    public new void Dispose()
    {
        Directory.Delete(Dir, recursive: true);
        EndpointHandler.ReloadAllEndpoints();
        _upstream.Dispose();
        base.Dispose();
    }

    private static void Write(string name, object content)
    {
        Directory.CreateDirectory(Path.Combine(Dir, name));
        File.WriteAllText(Path.Combine(Dir, name, "entity.json"), JsonSerializer.Serialize(content));
    }

    private Task<HttpResponseMessage> Post() =>
        _client.PostAsync($"/api/{Env}/ReloadComposite/Flow", new StringContent("{\"a\":1}", Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

    [Fact]
    public async Task StepAddedAfterStartup_IsCalled()
    {
        var response = await Post();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/step", _upstream.PathAndQuery);
    }

    [Fact]
    public async Task StepRemovedFromEnvironment_IsNotCalled()
    {
        Write("Step", new { Url = $"http://localhost:{UpstreamPort}/step", Methods = new[] { "POST" }, AllowedEnvironments = new[] { "700" } });
        EndpointHandler.ReloadEndpointType(EndpointType.Proxy);

        var response = await Post();

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(_upstream.PathAndQuery);
    }

    [Fact]
    public void IdentityResolvedOncePerRequest_SurvivesReload()
    {
        var request = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        string[] segments = ["ReloadComposite", "Step"];
        Assert.NotNull(PortwayApi.Api.EndpointController.ResolveEndpointIdentity(request, segments));

        Directory.Delete(Path.Combine(Dir, "Step"), recursive: true);
        EndpointHandler.ReloadEndpointType(EndpointType.Proxy);

        Assert.NotNull(PortwayApi.Api.EndpointController.ResolveEndpointIdentity(request, segments));
        Assert.Null(PortwayApi.Api.EndpointController.ResolveEndpointIdentity(new Microsoft.AspNetCore.Http.DefaultHttpContext(), segments));
    }

    [Fact]
    public void PartiallyWrittenFile_KeepsLastValidDefinition()
    {
        Assert.True(EndpointHandler.GetProxyEndpoints().ContainsKey("ReloadComposite/Step"));
        File.WriteAllText(Path.Combine(Dir, "Step", "entity.json"), "{\"Url\": \"http://local");
        EndpointHandler.ReloadEndpointType(EndpointType.Proxy);

        Assert.True(EndpointHandler.GetProxyEndpoints().ContainsKey("ReloadComposite/Step"));
    }
}
