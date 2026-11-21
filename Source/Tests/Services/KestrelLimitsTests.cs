using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PortwayApi.Services.Configuration;
using Xunit;

namespace PortwayApi.Tests.Services;

public sealed class KestrelLimitsTests
{
    private static KestrelServerLimits Limits(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.ConfigurePortwayWebHost();
        using var app = builder.Build();
        return app.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits;
    }

    [Fact]
    public void MaxConcurrentConnections_DefaultsTo1000() =>
        Assert.Equal(1000, Limits([]).MaxConcurrentConnections);

    [Fact]
    public void MaxConcurrentConnections_IsReadFromConfiguration() =>
        Assert.Equal(5000, Limits(new() { ["Kestrel:Limits:MaxConcurrentConnections"] = "5000" }).MaxConcurrentConnections);
}
