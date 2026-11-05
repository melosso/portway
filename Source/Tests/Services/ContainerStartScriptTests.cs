using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// A failed binary refresh stops the container.
/// </summary>
public sealed class ContainerStartScriptTests
{
    [Fact]
    public void BinaryRefresh_PropagatesCopyFailures()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "start.sh")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var script = File.ReadAllText(Path.Combine(directory.FullName, "start.sh"));

        Assert.Contains("set -e", script);
        Assert.Contains("-exec cp -at /app {} +", script);
    }
}
