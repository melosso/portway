using PortwayApi.Tests.Base;
using System.Net;
using System.Text.Json;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

public class HealthCheckEndpointTests : ApiTestBase
{
    [Fact]
    public async Task GetHealthLive_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Alive", content);
    }
    
    [Fact]
    public async Task GetHealth_WithAuthorization_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/health", TestContext.Current.CancellationToken);
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
        
        Assert.NotNull(result);
        Assert.True(result.ContainsKey("status"));
        Assert.True(result.ContainsKey("timestamp"));
    }
    
    [Fact]
    public async Task GetHealthDetails_WithAuthorization_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/health/details", TestContext.Current.CancellationToken);
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
        
        Assert.NotNull(result);
        Assert.True(result.ContainsKey("status"));
        Assert.True(result.ContainsKey("checks"));
        Assert.True(result.ContainsKey("totalDuration"));
        Assert.True(result.ContainsKey("version"));
    }
    
    [Fact]
    public async Task GetHealth_WithoutAuthorization_ReturnsOk()
    {
        // The summary endpoint is public so the dashboard badge works without a Bearer token
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetHealthDetails_WithoutAuthorization_ReturnsUnauthorized()
    {
        // The detailed report keeps requiring a token
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/health/details", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}