using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Moq;
using PortwayApi.Auth;
using PortwayApi.Classes;
using Serilog;
using Xunit;

namespace PortwayApi.Tests.Auth;

public class EnvironmentAuthServiceTests
{
    private readonly Mock<ILogger> _loggerMock;
    private readonly EnvironmentAuthService _service;

    public EnvironmentAuthServiceTests()
    {
        _loggerMock = new Mock<ILogger>();
        _service = new EnvironmentAuthService(_loggerMock.Object);
    }

    [Fact]
    public async Task ValidateAsync_ApiKey_Header_Success()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers["X-API-Key"] = "secret-key";
        
        var settings = new AuthenticationSettings
        {
            Enabled = true,
            Methods = new List<AuthenticationMethod>
            {
                new AuthenticationMethod
                {
                    Type = "ApiKey",
                    Name = "X-API-Key",
                    Value = "secret-key",
                    In = "Header"
                }
            }
        };

        // Act
        var result = await _service.ValidateAsync(context, settings);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ValidateAsync_ApiKey_Query_Success()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?key=secret-key");
        
        var settings = new AuthenticationSettings
        {
            Enabled = true,
            Methods = new List<AuthenticationMethod>
            {
                new AuthenticationMethod
                {
                    Type = "ApiKey",
                    Name = "key",
                    Value = "secret-key",
                    In = "Query"
                }
            }
        };

        // Act
        var result = await _service.ValidateAsync(context, settings);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ValidateAsync_BasicAuth_Success()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:password"));
        context.Request.Headers.Authorization = $"Basic {credentials}";
        
        var settings = new AuthenticationSettings
        {
            Enabled = true,
            Methods = new List<AuthenticationMethod>
            {
                new AuthenticationMethod
                {
                    Type = "Basic",
                    Name = "admin",
                    Value = "password"
                }
            }
        };

        // Act
        var result = await _service.ValidateAsync(context, settings);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ValidateAsync_BearerToken_Success()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer static-token";
        
        var settings = new AuthenticationSettings
        {
            Enabled = true,
            Methods = new List<AuthenticationMethod>
            {
                new AuthenticationMethod
                {
                    Type = "Bearer",
                    Value = "static-token"
                }
            }
        };

        // Act
        var result = await _service.ValidateAsync(context, settings);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ValidateAsync_MultipleMethods_OR_Logic()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers["X-API-Key"] = "wrong-key";
        context.Request.Headers.Authorization = "Bearer correct-token";
        
        var settings = new AuthenticationSettings
        {
            Enabled = true,
            Methods = new List<AuthenticationMethod>
            {
                new AuthenticationMethod
                {
                    Type = "ApiKey",
                    Name = "X-API-Key",
                    Value = "correct-key"
                },
                new AuthenticationMethod
                {
                    Type = "Bearer",
                    Value = "correct-token"
                }
            }
        };

        // Act
        var result = await _service.ValidateAsync(context, settings);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ValidateAsync_Fail_WhenNoMethodsMatch()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers["X-API-Key"] = "wrong-key";
        
        var settings = new AuthenticationSettings
        {
            Enabled = true,
            Methods = new List<AuthenticationMethod>
            {
                new AuthenticationMethod
                {
                    Type = "ApiKey",
                    Name = "X-API-Key",
                    Value = "correct-key"
                }
            }
        };

        // Act
        var result = await _service.ValidateAsync(context, settings);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateAsync_Hmac_Success()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var bodyStr = "{\"test\":true}";
        
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bodyStr));
        context.Request.Body = stream;
        context.Request.Method = "POST";
        context.Request.Path = "/api/test";
        
        var secret = "super-secret-key";
        var rawData = $"{context.Request.Method}{context.Request.Path}{timestamp}{bodyStr}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawData))).ToLowerInvariant();
        
        context.Request.Headers["X-Signature"] = signature;
        context.Request.Headers["X-Timestamp"] = timestamp;
        
        var settings = new AuthenticationSettings
        {
            Enabled = true,
            Methods = new List<AuthenticationMethod>
            {
                new AuthenticationMethod
                {
                    Type = "Hmac",
                    Name = "X-Signature",
                    Secret = secret
                }
            }
        };

        // Act
        var result = await _service.ValidateAsync(context, settings);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ValidateAsync_Hmac_FailsWithOldTimestamp()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString();
        var bodyStr = "{\"test\":true}";
        
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bodyStr));
        context.Request.Body = stream;
        context.Request.Method = "POST";
        context.Request.Path = "/api/test";
        
        var secret = "super-secret-key";
        var rawData = $"{context.Request.Method}{context.Request.Path}{timestamp}{bodyStr}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawData))).ToLowerInvariant();
        
        context.Request.Headers["X-Signature"] = signature;
        context.Request.Headers["X-Timestamp"] = timestamp;
        
        var settings = new AuthenticationSettings
        {
            Enabled = true,
            Methods = new List<AuthenticationMethod>
            {
                new AuthenticationMethod
                {
                    Type = "Hmac",
                    Name = "X-Signature",
                    Secret = secret
                }
            }
        };

        // Act
        var result = await _service.ValidateAsync(context, settings);

        // Assert
        Assert.False(result);
    }
}
