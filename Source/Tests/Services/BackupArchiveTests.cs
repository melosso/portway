using System.IO.Compression;
using Microsoft.Data.Sqlite;
using PortwayApi.Services.Database;
using PortwayApi.Tests.Support;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// Snapshot archives of auth.db, mcp.db, environments and endpoints
/// </summary>
public sealed class BackupArchiveTests : IDisposable
{
    private readonly TempDirectory _root = new("portway_backup");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.Dispose();
    }

    private void Seed()
    {
        using (var conn = new SqliteConnection($"Data Source={_root.Combine("auth.db")}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE AdminUsers (Id INTEGER PRIMARY KEY, Username TEXT); INSERT INTO AdminUsers (Username) VALUES ('admin-x')";
            cmd.ExecuteNonQuery();
        }
        File.WriteAllText(_root.Combine("metrics.db"), "not copied");
        Directory.CreateDirectory(_root.Combine("environments", "prod"));
        File.WriteAllText(_root.Combine("environments", "prod", "settings.json"), "{}");
        Directory.CreateDirectory(_root.Combine("endpoints", "SQL", "Items"));
        File.WriteAllText(_root.Combine("endpoints", "SQL", "Items", "entity.json"), "{}");
        Directory.CreateDirectory(_root.Combine("tokens"));
        File.WriteAllText(_root.Combine("tokens", "plain.txt"), "secret");
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task SnapshotContainsDatabasesAndConfigurationOnly()
    {
        Seed();

        var name = await BackupArchive.CreateAsync(_root.Path, 7, TestContext.Current.CancellationToken);

        using var zip = ZipFile.OpenRead(Path.Combine(BackupArchive.Folder(_root.Path), name));
        var entries = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        Assert.Contains("auth.db", entries);
        Assert.Contains("environments/prod/settings.json", entries);
        Assert.Contains("endpoints/SQL/Items/entity.json", entries);
        Assert.DoesNotContain(entries, e => e.StartsWith("tokens/") || e == "metrics.db" || e.StartsWith(".backups/"));

        var restored = _root.Combine("restored.db");
        zip.GetEntry("auth.db")!.ExtractToFile(restored);
        using var conn = new SqliteConnection($"Data Source={restored};Mode=ReadOnly");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Username FROM AdminUsers";
        Assert.Equal("admin-x", cmd.ExecuteScalar());
    }

    [Fact]
    public async Task RetentionKeepsTheNewestArchives()
    {
        Seed();
        var start = new DateTime(2026, 1, 1, 3, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 9; i++)
            await BackupArchive.CreateAsync(_root.Path, 7, TestContext.Current.CancellationToken, start.AddDays(i));

        var kept = BackupArchive.List(_root.Path);
        Assert.Equal(7, kept.Count);
        Assert.Equal("portway-20260109-030000.zip", kept[0].Name);
        Assert.DoesNotContain(kept, b => b.Name == "portway-20260101-030000.zip");
    }

    [Theory]
    [InlineData("../auth.db")]
    [InlineData("portway-20260101-030000.zip/../../auth.db")]
    [InlineData("/etc/passwd")]
    [InlineData("other.zip")]
    [InlineData("portway-20260101-030000.zip")]
    public void ResolveRefusesUnknownAndTraversingNames(string name)
    {
        Assert.Null(BackupArchive.Resolve(_root.Path, name));
    }

    [Fact]
    public async Task ResolveFindsAnExistingArchive()
    {
        Seed();
        var name = await BackupArchive.CreateAsync(_root.Path, 7, TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(BackupArchive.Folder(_root.Path), name), BackupArchive.Resolve(_root.Path, name));
    }

    [Fact]
    public async Task RestoreIsStagedThenAppliedWithThePreviousFilesKept()
    {
        Seed();
        var name = await BackupArchive.CreateAsync(_root.Path, 7, TestContext.Current.CancellationToken);

        File.WriteAllText(_root.Combine("environments", "prod", "settings.json"), "{\"changed\":true}");
        File.WriteAllText(_root.Combine("environments", "added.json"), "{}");
        File.WriteAllText(_root.Combine("auth.db-wal"), "stale");

        BackupArchive.StageRestore(_root.Path, name);
        Assert.Equal(name, BackupArchive.PendingRestore(_root.Path));
        Assert.Equal("{\"changed\":true}", File.ReadAllText(_root.Combine("environments", "prod", "settings.json")));

        var applied = BackupArchive.ApplyPendingRestore(_root.Path);

        Assert.Equal(name, applied);
        Assert.Null(BackupArchive.PendingRestore(_root.Path));
        Assert.Equal("{}", File.ReadAllText(_root.Combine("environments", "prod", "settings.json")));
        Assert.False(File.Exists(_root.Combine("environments", "added.json")));
        Assert.False(File.Exists(_root.Combine("auth.db-wal")));

        var previous = Assert.Single(Directory.GetDirectories(_root.Combine(".backups"), "pre-restore-*"));
        Assert.Equal("{\"changed\":true}", File.ReadAllText(Path.Combine(previous, "environments", "prod", "settings.json")));
        Assert.True(File.Exists(Path.Combine(previous, "auth.db-wal")));
    }

    [Fact]
    public void ApplyWithoutAPendingRestoreChangesNothing()
    {
        Seed();

        Assert.Null(BackupArchive.ApplyPendingRestore(_root.Path));
        Assert.False(Directory.Exists(_root.Combine(".backups")));
    }

    [Theory]
    [InlineData("../evil.json")]
    [InlineData("tokens/plain.txt")]
    [InlineData("appsettings.json")]
    [InlineData("environments/../../evil.json")]
    public void StageRefusesEntriesOutsideTheArchiveLayout(string entry)
    {
        var folder = Directory.CreateDirectory(BackupArchive.Folder(_root.Path)).FullName;
        const string name = "portway-20260101-030000.zip";
        using (var zip = ZipFile.Open(Path.Combine(folder, name), ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write("x");
        }

        Assert.Throws<InvalidDataException>(() => BackupArchive.StageRestore(_root.Path, name));
        Assert.Null(BackupArchive.PendingRestore(_root.Path));
        Assert.False(File.Exists(Path.Combine(_root.Path, "..", "evil.json")));
    }
}
