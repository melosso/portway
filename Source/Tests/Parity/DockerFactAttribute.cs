namespace PortwayApi.Tests.Parity;

using DotNet.Testcontainers.Configurations;
using System.Runtime.CompilerServices;
using Xunit;

/// <summary>
/// Fact that skips when no container runtime is reachable, keeps plain dotnet test green without Docker
/// </summary>
public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!DockerProbe.IsAvailable)
            Skip = "Container runtime (docker) is not available on this machine";
    }
}

public static class DockerProbe
{
    public static bool IsAvailable { get; } = Probe();

    // testcontainers discovery pings the endpoint and honours DOCKER_HOST, so podman works without a docker cli
    private static bool Probe()
    {
        try
        {
            return TestcontainersSettings.OS.DockerEndpointAuthConfig != null;
        }
        catch
        {
            return false;
        }
    }
}
