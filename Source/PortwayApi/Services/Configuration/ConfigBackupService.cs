using System.Security.Cryptography;
using PortwayApi.Helpers;
using Serilog;

namespace PortwayApi.Services.Configuration;

/// <summary>Timestamped copies of config files before Web UI writes; keeps the last 10 distinct versions per file</summary>
public static class ConfigBackupService
{
    private const int MaxBackupsPerFile = 10;

    private static string BackupRoot => Path.Combine(Directory.GetCurrentDirectory(), ".backups");

    /// <summary>Copies the file into .backups; identical content reuses the existing backup</summary>
    public static string? Backup(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            var cwd = Directory.GetCurrentDirectory();
            var fullPath = Path.GetFullPath(filePath);
            var relative = fullPath.StartsWith(cwd, StringComparison.OrdinalIgnoreCase)
                ? Path.GetRelativePath(cwd, fullPath)
                : Path.GetFileName(fullPath);

            HiddenDirectoryHelper.Ensure(BackupRoot);
            var backupDir = Path.Combine(BackupRoot, Path.GetDirectoryName(relative) ?? "");
            Directory.CreateDirectory(backupDir);

            var fileName = Path.GetFileName(fullPath);

            if (LatestBackup(backupDir, fileName) is { } latest && ContentMatches(latest, fullPath))
            {
                Log.Debug("Skipped backup of {FilePath}, content is unchanged since {BackupPath}", filePath, latest);
                return latest;
            }

            var backupPath = UniqueBackupPath(backupDir, fileName);
            File.Copy(fullPath, backupPath, overwrite: false);

            Prune(backupDir, fileName);
            return backupPath;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to back up {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>Restores a backup onto its target; target must live under endpoints/ or environments/</summary>
    public static bool Restore(string backupPath, string targetPath)
    {
        try
        {
            var fullBackup = Path.GetFullPath(backupPath);
            if (!fullBackup.StartsWith(Path.GetFullPath(BackupRoot), StringComparison.OrdinalIgnoreCase) || !File.Exists(fullBackup))
                return false;

            var cwd = Directory.GetCurrentDirectory();
            var fullTarget = Path.GetFullPath(targetPath);
            var allowed = new[] { Path.Combine(cwd, "endpoints"), Path.Combine(cwd, "environments") };
            if (!allowed.Any(a => fullTarget.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                return false;

            // Back up the current state first so a restore is itself undoable
            Backup(fullTarget);

            Directory.CreateDirectory(Path.GetDirectoryName(fullTarget)!);
            File.Copy(fullBackup, fullTarget, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restore {BackupPath} to {TargetPath}", backupPath, targetPath);
            return false;
        }
    }

    // Suffix keeps writes in the same millisecond distinct and still ordinally sorted
    private static string UniqueBackupPath(string backupDir, string fileName)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var path  = Path.Combine(backupDir, $"{stamp}-{fileName}");

        for (var attempt = 1; File.Exists(path); attempt++)
            path = Path.Combine(backupDir, $"{stamp}_{attempt:D3}-{fileName}");

        return path;
    }

    private static string? LatestBackup(string backupDir, string fileName)
        => Directory.EnumerateFiles(backupDir, $"*-{fileName}").Max(StringComparer.Ordinal);

    private static bool ContentMatches(string left, string right)
    {
        if (new FileInfo(left).Length != new FileInfo(right).Length) return false;

        return Hash(left).SequenceEqual(Hash(right));
    }

    private static byte[] Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }

    private static void Prune(string backupDir, string fileName)
    {
        var backups = Directory.GetFiles(backupDir, $"*-{fileName}")
            .OrderByDescending(f => f, StringComparer.Ordinal)
            .Skip(MaxBackupsPerFile)
            .ToList();
        foreach (var old in backups)
        {
            try
            {
                File.Delete(old);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning(ex, "Failed to prune old backup {BackupPath}", old);
            }
        }
    }
}
