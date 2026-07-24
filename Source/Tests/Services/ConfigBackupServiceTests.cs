using PortwayApi.Services.Configuration;
using PortwayApi.Tests.Support;
using Xunit;

namespace PortwayApi.Tests.Services;

public class ConfigBackupServiceTests : IDisposable
{
    private readonly TempDirectory _workDir;
    private readonly string _configPath;

    public ConfigBackupServiceTests()
    {
        // Backup paths resolve against the current directory
        _workDir = new TempDirectory("backup_test", Directory.GetCurrentDirectory());
        _configPath = _workDir.Combine("entity.json");
    }

    public void Dispose()
    {
        _workDir.Dispose();
        TempDirectory.TryDelete(Path.Combine(Directory.GetCurrentDirectory(), ".backups", Path.GetFileName(_workDir.Path)));
    }

    private void WriteConfig(string content) => File.WriteAllText(_configPath, content);

    [Fact]
    public void Backup_MissingFile_ReturnsNull()
    {
        Assert.Null(ConfigBackupService.Backup(_configPath));
    }

    [Fact]
    public void Backup_UnchangedContent_ReusesTheExistingBackup()
    {
        WriteConfig("""{"a":1}""");

        var first  = ConfigBackupService.Backup(_configPath);
        var second = ConfigBackupService.Backup(_configPath);

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Single(BackupFiles());
    }

    [Fact]
    public void Backup_ChangedContent_AddsAnEntry()
    {
        WriteConfig("""{"a":1}""");
        var first = ConfigBackupService.Backup(_configPath);

        WriteConfig("""{"a":2}""");
        var second = ConfigBackupService.Backup(_configPath);

        Assert.NotEqual(first, second);
        Assert.Equal(2, BackupFiles().Length);
    }

    [Fact]
    public void Backup_SameLengthDifferentContent_AddsAnEntry()
    {
        WriteConfig("""{"a":1}""");
        ConfigBackupService.Backup(_configPath);

        WriteConfig("""{"b":1}""");
        ConfigBackupService.Backup(_configPath);

        Assert.Equal(2, BackupFiles().Length);
    }

    [Fact]
    public void Backup_KeepsAtMostTenVersionsPerFile()
    {
        for (var i = 0; i < 15; i++)
        {
            WriteConfig($$"""{"a":{{i}}}""");
            ConfigBackupService.Backup(_configPath);
        }

        Assert.Equal(10, BackupFiles().Length);
    }

    [Fact]
    public void Restore_PutsBackAnEarlierVersion()
    {
        // Restore only accepts targets under endpoints/ or environments/
        var endpointDir = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "SQL", $"BackupTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(endpointDir);
        var targetPath = Path.Combine(endpointDir, "entity.json");

        try
        {
            File.WriteAllText(targetPath, """{"v":1}""");
            var original = ConfigBackupService.Backup(targetPath)!;

            File.WriteAllText(targetPath, """{"v":2}""");

            Assert.True(ConfigBackupService.Restore(original, targetPath));
            Assert.Equal("""{"v":1}""", File.ReadAllText(targetPath));
        }
        finally
        {
            TempDirectory.TryDelete(endpointDir);
            TempDirectory.TryDelete(Path.Combine(
                Directory.GetCurrentDirectory(), ".backups", "endpoints", "SQL", Path.GetFileName(endpointDir)));
        }
    }

    [Fact]
    public void Restore_RejectsATargetOutsideTheConfigFolders()
    {
        WriteConfig("""{"a":1}""");
        var backup = ConfigBackupService.Backup(_configPath)!;

        Assert.False(ConfigBackupService.Restore(backup, _configPath));
    }

    private string[] BackupFiles()
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), ".backups", Path.GetFileName(_workDir.Path));
        return Directory.Exists(dir) ? Directory.GetFiles(dir, "*-entity.json") : [];
    }
}
