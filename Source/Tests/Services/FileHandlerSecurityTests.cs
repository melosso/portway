using System;
using System.Text;
using PortwayApi.Services.Files;
using Xunit;

namespace PortwayApi.Tests.Services;

public class FileHandlerSecurityTests
{
    // Helper: build a base64url fileId the same way GenerateFileId does
    private static string MakeFileId(string environment, string filename)
    {
        string combined = $"{environment}:{filename}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(combined))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    // --- ValidateFileIdComponents ---

    [Theory]
    [InlineData("500", "report.pdf")]
    [InlineData("prod", "image.png")]
    [InlineData("env-1", "data.csv")]
    public void ValidateFileIdComponents_LegitimateInputs_ReturnsTrue(string env, string filename)
        => Assert.True(FileHandlerService.ValidateFileIdComponents(env, filename));

    [Theory]
    [InlineData("500", "../../appsettings.json")]
    [InlineData("500", "../secret.txt")]
    [InlineData("500", "sub/../../etc/passwd")]
    public void ValidateFileIdComponents_TraversalInFilename_ReturnsFalse(string env, string filename)
        => Assert.False(FileHandlerService.ValidateFileIdComponents(env, filename));

    [Theory]
    [InlineData("../500", "report.pdf")]
    [InlineData("../../", "report.pdf")]
    [InlineData("500/../../", "report.pdf")]
    [InlineData("500\\..\\etc", "report.pdf")]
    public void ValidateFileIdComponents_TraversalInEnvironment_ReturnsFalse(string env, string filename)
        => Assert.False(FileHandlerService.ValidateFileIdComponents(env, filename));

    [Theory]
    [InlineData("500", "/etc/passwd")]
    [InlineData("500", "C:\\Windows\\System32\\config\\sam")]
    public void ValidateFileIdComponents_AbsolutePathInFilename_ReturnsFalse(string env, string filename)
        => Assert.False(FileHandlerService.ValidateFileIdComponents(env, filename));

    [Theory]
    [InlineData("", "report.pdf")]
    [InlineData("500", "")]
    [InlineData("500", "   ")]
    public void ValidateFileIdComponents_EmptyComponents_ReturnsFalse(string env, string filename)
        => Assert.False(FileHandlerService.ValidateFileIdComponents(env, filename));

    [Fact]
    public void ValidateFileIdComponents_NullFilename_ReturnsFalse()
        => Assert.False(FileHandlerService.ValidateFileIdComponents("500", null!));

    [Fact]
    public void ValidateFileIdComponents_NullEnvironment_ReturnsFalse()
        => Assert.False(FileHandlerService.ValidateFileIdComponents(null!, "report.pdf"));

    // --- ParseFileId via MakeFileId round-trip ---

    [Fact]
    public void MakeFileId_LegitimateValues_RoundTripsCorrectly()
    {
        string fileId = MakeFileId("500", "report.pdf");

        // fileId must be decodable and produce valid components
        string decoded = fileId
            .Replace('-', '+')
            .Replace('_', '/');
        while (decoded.Length % 4 != 0) decoded += "=";
        string combined = Encoding.UTF8.GetString(Convert.FromBase64String(decoded));

        int colon = combined.IndexOf(':');
        Assert.True(colon > 0);
        Assert.Equal("500", combined[..colon]);
        Assert.Equal("report.pdf", combined[(colon + 1)..]);
    }

    [Fact]
    public void MakeFileId_TraversalPayload_ValidationRejectsIt()
    {
        // Simulate an attacker crafting a malicious fileId
        string maliciousFileId = MakeFileId("500", "../../appsettings.json");

        string decoded = maliciousFileId
            .Replace('-', '+')
            .Replace('_', '/');
        while (decoded.Length % 4 != 0) decoded += "=";
        string combined = Encoding.UTF8.GetString(Convert.FromBase64String(decoded));

        int colon = combined.IndexOf(':');
        string env = combined[..colon];
        string filename = combined[(colon + 1)..];

        Assert.False(FileHandlerService.ValidateFileIdComponents(env, filename));
    }
}
