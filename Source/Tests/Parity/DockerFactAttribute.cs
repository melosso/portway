namespace PortwayApi.Tests.Parity;

using System.Diagnostics;
using Xunit;

/// <summary>Fact that skips when no container runtime is reachable, keeps plain dotnet test green without Docker</summary>
public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!DockerProbe.IsAvailable)
            Skip = "Container runtime (docker) is not available on this machine";
    }
}

public static class DockerProbe
{
    public static bool IsAvailable { get; } = Probe();

    private static bool Probe()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            return process != null && process.WaitForExit(5000) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
