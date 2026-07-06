using Serilog;

namespace PortwayApi.Services.Configuration;

/// <summary>Timestamped copies of config files before Web UI writes; keeps the last 10 per file</summary>
public static class ConfigBackupService
{
    private const int MaxBackupsPerFile = 10;

    private static string BackupRoot => Path.Combine(Directory.GetCurrentDirectory(), ".backups");

    /// <summary>Copies the current file into .backups before it is overwritten or deleted; returns the backup path or null</summary>
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

            var backupDir = Path.Combine(BackupRoot, Path.GetDirectoryName(relative) ?? "");
            Directory.CreateDirectory(backupDir);

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            var backupPath = Path.Combine(backupDir, $"{stamp}-{Path.GetFileName(fullPath)}");
            File.Copy(fullPath, backupPath, overwrite: false);

            Prune(backupDir, Path.GetFileName(fullPath));
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

    private static void Prune(string backupDir, string fileName)
    {
        var backups = Directory.GetFiles(backupDir, $"*-{fileName}")
            .OrderByDescending(f => f)
            .Skip(MaxBackupsPerFile)
            .ToList();
        foreach (var old in backups)
        {
            try { File.Delete(old); } catch { /* best effort */ }
        }
    }
}
