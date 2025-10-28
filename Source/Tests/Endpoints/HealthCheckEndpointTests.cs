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
        var response = await _client.GetAsync("/health/live");
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("Alive", content);
    }
    
    [Fact]
    public async Task GetHealth_WithAuthorization_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/health");
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
        
        Assert.NotNull(result);
        Assert.True(result.ContainsKey("status"));
        Assert.True(result.ContainsKey("timestamp"));
    }
    
    [Fact]
    public async Task GetHealthDetails_WithAuthorization_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/health/details");
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
        
        Assert.NotNull(result);
        Assert.True(result.ContainsKey("status"));
        Assert.True(result.ContainsKey("checks"));
        Assert.True(result.ContainsKey("totalDuration"));
        Assert.True(result.ContainsKey("version"));
    }
    
    [Fact]
    public async Task GetHealth_WithoutAuthorization_ReturnsUnauthorized()
    {
        // Arrange - remove authorization
        _client.DefaultRequestHeaders.Authorization = null;
        
        // Act
        var response = await _client.GetAsync("/health");
        
        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}