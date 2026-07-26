using System.Text.Json;
using PortwayApi.Classes;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>Covers the Hidden flag and the IsPrivate config alias across every entity model</summary>
public class EndpointVisibilityTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Hidden_DefaultsToFalse_OnEveryEntityModel()
    {
        Assert.False(JsonSerializer.Deserialize<EndpointEntity>("{}", Options)!.Hidden);
        Assert.False(JsonSerializer.Deserialize<ExtendedEndpointEntity>("{}", Options)!.Hidden);
        Assert.False(JsonSerializer.Deserialize<FileEndpointEntity>("{}", Options)!.Hidden);
        Assert.False(JsonSerializer.Deserialize<StaticEndpointEntity>("{}", Options)!.Hidden);
    }

    [Theory]
    [InlineData("Hidden")]
    [InlineData("IsPrivate")]
    public void BothConfigNames_SetHidden_OnEveryEntityModel(string property)
    {
        var json = $"{{ \"{property}\": true }}";

        Assert.True(JsonSerializer.Deserialize<EndpointEntity>(json, Options)!.Hidden);
        Assert.True(JsonSerializer.Deserialize<ExtendedEndpointEntity>(json, Options)!.Hidden);
        Assert.True(JsonSerializer.Deserialize<FileEndpointEntity>(json, Options)!.Hidden);
        Assert.True(JsonSerializer.Deserialize<StaticEndpointEntity>(json, Options)!.Hidden);
    }
}
