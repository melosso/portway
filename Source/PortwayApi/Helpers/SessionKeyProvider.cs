namespace PortwayApi.Helpers;

using System.Security.Cryptography;
using Serilog;

/// <summary>
/// The key that signs console session cookies. Kept in portway.key rather than derived from
/// WebUi:AdminApiKey, so sessions survive that setting being removed.
/// </summary>
public static class SessionKeyProvider
{
    private const string KeyFileName = "portway.key";
    private static readonly Lock Gate = new();
    private static byte[]? _key;

    public static byte[] Key
    {
        get
        {
            if (_key is not null) return _key;
            lock (Gate)
            {
                _key ??= LoadOrCreate();
                return _key;
            }
        }
    }

    private static byte[] LoadOrCreate()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), KeyFileName);

        try
        {
            if (File.Exists(path))
            {
                var existing = Convert.FromBase64String(File.ReadAllText(path).Trim());
                if (existing.Length >= 32) return existing;
                Log.Warning("{File} is too short to sign sessions; generating a new one", KeyFileName);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not read {File}; generating a new one", KeyFileName);
        }

        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            File.WriteAllText(path, Convert.ToBase64String(key));
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Log.Information("Created {File}; keep it with the deployment, deleting it signs everyone out", KeyFileName);
        }
        catch (Exception ex)
        {
            // An unwritable directory is survivable: sessions then last only as long as the process
            Log.Warning(ex, "Could not persist {File}; sessions will end when Portway restarts", KeyFileName);
        }

        return key;
    }
}
