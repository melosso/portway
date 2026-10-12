namespace PortwayApi.Helpers;

/// <summary>
/// Replaces a file in one rename so concurrent readers see complete content
/// </summary>
public static class AtomicFile
{
    public static async Task WriteAllTextAsync(string path, string contents)
    {
        var temp = TempPath(path);
        await File.WriteAllTextAsync(temp, contents);
        Publish(temp, path, contents);
    }

    public static void WriteAllText(string path, string contents)
    {
        var temp = TempPath(path);
        File.WriteAllText(temp, contents);
        Publish(temp, path, contents);
    }

    // unique per write so two concurrent saves never rename each other's half-written temp file
    private static string TempPath(string path) => $"{path}.{Guid.NewGuid():N}.tmp";

    private static void Publish(string temp, string path, string contents)
    {
        try
        {
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && File.Exists(path))
        {
            // single-file bind mounts reject rename with EBUSY
            File.Delete(temp);
            File.WriteAllText(path, contents);
        }
    }
}
