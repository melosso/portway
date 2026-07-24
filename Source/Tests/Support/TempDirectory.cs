namespace PortwayApi.Tests.Support;

using IOPath = System.IO.Path;

/// <summary>Scratch directory for tests, created on construction and removed on dispose</summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory(string prefix, string? root = null)
    {
        Path = IOPath.Combine(root ?? IOPath.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Combine(params string[] segments) => IOPath.Combine([Path, .. segments]);

    public void Dispose() => TryDelete(Path);

    public static void TryDelete(string path)
    {
        if (!Directory.Exists(path)) return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not delete test directory {path}: {ex.Message}");
        }
    }
}
