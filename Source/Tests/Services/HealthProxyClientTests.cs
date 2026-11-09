using System.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using PortwayApi.Classes;
using Xunit;

using MsHealthCheckService = Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService;

namespace PortwayApi.Tests.Services;

/// <summary>
/// The proxy health check sends requests through the guarded ProxyClient
/// </summary>
[Collection("Integration")]
public sealed class HealthProxyClientTests : IDisposable
{
    private static readonly string Dir = Path.Combine("endpoints", "Proxy", "HealthGuardTest");

    public HealthProxyClientTests()
    {
        Directory.CreateDirectory(Path.Combine(Dir, "Target"));
        File.WriteAllText(Path.Combine(Dir, "Target", "entity.json"),
            """{ "Url": "http://health-guard.invalid/target", "Methods": ["GET"] }""");
        EndpointHandler.ReloadAllEndpoints();
    }

    public void Dispose()
    {
        Directory.Delete(Dir, recursive: true);
        EndpointHandler.ReloadAllEndpoints();
    }

    [Fact]
    public async Task ProxyHealthCheck_UsesNamedProxyClient()
    {
        var handler = new RecordingHandler();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("ProxyClient")).Returns(() => new HttpClient(handler, disposeHandler: false));

        var inner = new Mock<MsHealthCheckService>();
        inner.Setup(s => s.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthReport(new Dictionary<string, HealthReportEntry>(), TimeSpan.Zero));

        var service = new PortwayApi.Services.HealthCheckService(inner.Object, TimeSpan.Zero, factory.Object);
        var report = await service.CheckHealthAsync(TestContext.Current.CancellationToken);

        factory.Verify(f => f.CreateClient("ProxyClient"), Times.Once);
        Assert.Contains(handler.Requests, u => u.Host == "health-guard.invalid");
        Assert.Equal(HealthStatus.Healthy, report.Entries["ProxyEndpoints"].Status);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests)
                Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }
}
