namespace PortwayApi.Helpers;

using Serilog;

/// <summary>Creates support folders like .core and .backups, hidden on Windows where the dot prefix means nothing</summary>
public static class HiddenDirectoryHelper
{
    public static DirectoryInfo Ensure(string path)
    {
        var info = Directory.CreateDirectory(path);

        if (!OperatingSystem.IsWindows() || (info.Attributes & FileAttributes.Hidden) != 0)
            return info;

        try
        {
            info.Attributes |= FileAttributes.Hidden;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Debug(ex, "Could not mark {Path} as hidden", path);
        }

        return info;
    }
}
