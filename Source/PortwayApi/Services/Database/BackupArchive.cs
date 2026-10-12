using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using PortwayApi.Helpers;

namespace PortwayApi.Services.Database;

/// <summary>
/// One snapshot archive in the snapshots folder
/// </summary>
public sealed record BackupFile(string Name, long Bytes, DateTime CreatedUtc);

/// <summary>
/// Snapshot archives of auth.db, mcp.db, environments and endpoints under .backups/snapshots
/// </summary>
public static partial class BackupArchive
{
    private static readonly string[] Databases = ["auth.db", "mcp.db"];

    private static readonly string[] ConfigFolders = ["environments", "endpoints"];

    [GeneratedRegex(@"^portway-\d{8}-\d{6}\.zip$")]
    private static partial Regex NameFormat();

    public static string Folder(string root) => Path.Combine(root, ".backups", "snapshots");

    /// <summary>
    /// Writes a new archive, then deletes all but the newest <paramref name="retention"/> archives
    /// </summary>
    public static async Task<string> CreateAsync(string root, int retention, CancellationToken ct = default, DateTime? nowUtc = null)
    {
        HiddenDirectoryHelper.Ensure(Path.Combine(root, ".backups"));
        var folder = Directory.CreateDirectory(Folder(root)).FullName;
        var name = $"portway-{nowUtc ?? DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";
        var staging = Directory.CreateDirectory(Path.Combine(folder, $".staging-{Guid.NewGuid():N}")).FullName;
        try
        {
            foreach (var db in Databases)
            {
                var source = Path.Combine(root, db);
                if (!File.Exists(source)) continue;

                // consistent copy while the gateway keeps writing
                await using var conn = new SqliteConnection($"Data Source={source};Mode=ReadOnly;Pooling=False");
                await conn.OpenAsync(ct);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "VACUUM INTO $path";
                cmd.Parameters.AddWithValue("$path", Path.Combine(staging, db));
                await cmd.ExecuteNonQueryAsync(ct);
            }

            var temp = Path.Combine(staging, name);
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                foreach (var db in Databases.Where(db => File.Exists(Path.Combine(staging, db))))
                    zip.CreateEntryFromFile(Path.Combine(staging, db), db, CompressionLevel.Optimal);

                foreach (var dir in ConfigFolders.Select(d => Path.Combine(root, d)).Where(Directory.Exists))
                {
                    foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                        zip.CreateEntryFromFile(file, Path.GetRelativePath(root, file).Replace('\\', '/'), CompressionLevel.Optimal);
                }
            }
            File.Move(temp, Path.Combine(folder, name), overwrite: true);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }

        foreach (var old in List(root).Skip(Math.Max(1, retention)))
            File.Delete(Path.Combine(folder, old.Name));

        return name;
    }

    /// <summary>
    /// Archives, newest first
    /// </summary>
    public static IReadOnlyList<BackupFile> List(string root)
    {
        var folder = Folder(root);
        if (!Directory.Exists(folder)) return [];

        return [.. new DirectoryInfo(folder).EnumerateFiles("portway-*.zip")
            .Where(f => NameFormat().IsMatch(f.Name))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Select(f => new BackupFile(f.Name, f.Length, f.LastWriteTimeUtc))];
    }

    /// <summary>
    /// Name of the archive staged for the next start, or null
    /// </summary>
    public static string? PendingRestore(string root)
    {
        var marker = Path.Combine(PendingFolder(root), SourceMarker);
        return File.Exists(marker) ? File.ReadAllText(marker).Trim() : null;
    }

    /// <summary>
    /// Extracts an archive into .backups/restore-pending; the next start applies it
    /// </summary>
    public static void StageRestore(string root, string name)
    {
        var archive = Resolve(root, name) ?? throw new FileNotFoundException("Backup not found", name);
        var pending = PendingFolder(root);
        var staging = pending + ".tmp";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        var stagingRoot = Directory.CreateDirectory(staging).FullName + Path.DirectorySeparatorChar;
        try
        {
            using (var zip = ZipFile.OpenRead(archive))
            {
                foreach (var entry in zip.Entries.Where(e => !e.FullName.EndsWith('/')))
                {
                    var target = Path.GetFullPath(Path.Combine(stagingRoot, entry.FullName));
                    var relative = Path.GetRelativePath(stagingRoot, target).Replace('\\', '/');
                    if (!target.StartsWith(stagingRoot, StringComparison.Ordinal) || !IsArchivePath(relative))
                        throw new InvalidDataException($"'{entry.FullName}' is not part of a Portway backup");

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target);
                }
            }
            File.WriteAllText(Path.Combine(staging, SourceMarker), name);

            CancelRestore(root);
            Directory.Move(staging, pending);
        }
        catch
        {
            Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    public static void CancelRestore(string root)
    {
        var pending = PendingFolder(root);
        if (Directory.Exists(pending)) Directory.Delete(pending, recursive: true);
    }

    /// <summary>
    /// Replaces the databases and folders a staged archive contains; runs before any database opens
    /// </summary>
    public static string? ApplyPendingRestore(string root)
    {
        if (PendingRestore(root) is not { } name) return null;

        var pending = PendingFolder(root);
        var previous = Path.Combine(root, ".backups", $"pre-restore-{DateTime.UtcNow:yyyyMMdd-HHmmss}");

        foreach (var db in Databases.Where(db => File.Exists(Path.Combine(pending, db))))
        {
            foreach (var file in new[] { db, db + "-wal", db + "-shm" }.Select(f => Path.Combine(root, f)).Where(File.Exists))
            {
                CopyFile(file, Path.Combine(previous, Path.GetFileName(file)));
                // a leftover write-ahead log belongs to the replaced database
                if (!file.EndsWith(db, StringComparison.Ordinal)) File.Delete(file);
            }
            File.Copy(Path.Combine(pending, db), Path.Combine(root, db), overwrite: true);
        }

        foreach (var dir in ConfigFolders.Where(d => Directory.Exists(Path.Combine(pending, d))))
        {
            var current = Path.Combine(root, dir);
            if (Directory.Exists(current))
            {
                CopyTree(current, Path.Combine(previous, dir));
                // contents only, the folder itself can be a bind mount
                foreach (var file in Directory.GetFiles(current)) File.Delete(file);
                foreach (var sub in Directory.GetDirectories(current)) Directory.Delete(sub, recursive: true);
            }
            CopyTree(Path.Combine(pending, dir), current);
        }

        Directory.Delete(pending, recursive: true);
        return name;
    }

    private const string SourceMarker = ".source";

    private static string PendingFolder(string root) => Path.Combine(root, ".backups", "restore-pending");

    private static bool IsArchivePath(string relative) =>
        Databases.Contains(relative) || ConfigFolders.Any(f => relative.StartsWith(f + "/", StringComparison.Ordinal));

    private static void CopyFile(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
    }

    private static void CopyTree(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            CopyFile(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }

    /// <summary>
    /// Full path of an existing archive, or null for any other name
    /// </summary>
    public static string? Resolve(string root, string name)
    {
        if (!NameFormat().IsMatch(name)) return null;
        var path = Path.Combine(Folder(root), name);
        return File.Exists(path) ? path : null;
    }
}
