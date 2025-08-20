using PortwayApi.Classes;
using PortwayApi.Tests.Base;
using System.Text.Json;
using Xunit;

namespace PortwayApi.Tests.Services;

public class EnvironmentSettingsTests
{
    [Fact]
    public void IsEnvironmentAllowed_ValidEnvironment_ReturnsTrue()
    {
        // Arrange
        var settings = new TestEnvironmentSettings();
        settings.SetAllowedEnvironments(new List<string> { "600", "700", "test" });
        
        // Act
        bool result = settings.IsEnvironmentAllowed("test");
        
        // Assert
        Assert.True(result);
    }
    
    [Fact]
    public void IsEnvironmentAllowed_InvalidEnvironment_ReturnsFalse()
    {
        // Arrange
        var settings = new TestEnvironmentSettings();
        settings.SetAllowedEnvironments(new List<string> { "600", "700", "test" });
        
        // Act
        bool result = settings.IsEnvironmentAllowed("invalid");
        
        // Assert
        Assert.False(result);
    }
    
    [Fact]
    public void GetAllowedEnvironments_ReturnsCorrectEnvironments()
    {
        // Arrange
        var expectedEnvironments = new List<string> { "600", "700", "test" };
        var settings = new TestEnvironmentSettings();
        settings.SetAllowedEnvironments(expectedEnvironments);
        
        // Act
        var result = settings.GetAllowedEnvironments();
        
        // Assert
        Assert.Equal(expectedEnvironments.Count, result.Count);
        Assert.Equal(expectedEnvironments, result);
    }
    
    [Fact]
    public void Constructor_SetsServerName()
    {
        // Arrange & Act
        var settings = new TestEnvironmentSettings();
        
        // Assert
        Assert.NotNull(settings.ServerName);
        // We can't guarantee what ServerName will be, but it shouldn't be empty
        Assert.False(string.IsNullOrEmpty(settings.ServerName));
    }
}