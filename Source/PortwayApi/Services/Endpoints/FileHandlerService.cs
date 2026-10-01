using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Options;
using PortwayApi.Helpers;
using PortwayApi.Services.Caching;
using Serilog;

namespace PortwayApi.Services.Files;

/// <summary>
/// Service for handling file operations
/// </summary>
public class FileHandlerService : IDisposable
{
    private readonly IOptionsMonitor<FileStorageOptions> _optionsMonitor;
    private readonly CacheManager _cacheManager;
    private readonly ConcurrentDictionary<string, MemoryStream> _memoryCache = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastAccessTimes = new();
    private readonly Timer _evictTimer;
    private readonly Timer _indexRefreshTimer;
    private readonly FileSystemIndex _fileSystemIndex;
    private readonly Serilog.ILogger _logger;
    private long _currentMemoryUsage = 0;
    private int _evictRunning;
    private int _refreshRunning;
    private bool _disposed = false;

    /// <summary>
    /// Bytes currently held in the memory cache, exposed for diagnostics and tests
    /// </summary>
    internal long CurrentMemoryUsage => Interlocked.Read(ref _currentMemoryUsage);

    /// <summary>
    /// Bytes actually resident in the memory cache, used to assert the counter has not drifted
    /// </summary>
    internal long MeasuredMemoryUsage => _memoryCache.Values.Sum(s => s.Length);

    public FileHandlerService(IOptionsMonitor<FileStorageOptions> optionsMonitor, CacheManager cacheManager, Serilog.ILogger logger)
    {
        _optionsMonitor = optionsMonitor;
        var options = optionsMonitor.CurrentValue;
        _cacheManager = cacheManager;
        _logger = Serilog.Log.Logger; // Use Serilog's static logger

        // Create the storage directory if it doesn't exist
        if (!Directory.Exists(_optionsMonitor.CurrentValue.StorageDirectory))
        {
            Directory.CreateDirectory(_optionsMonitor.CurrentValue.StorageDirectory);
            _logger.Information("Created file storage directory: {Directory}", _optionsMonitor.CurrentValue.StorageDirectory);
        }

        // Initialize the file system index
        _fileSystemIndex = new FileSystemIndex(
            _optionsMonitor.CurrentValue.StorageDirectory,
            _cacheManager,
            _logger,
            GenerateFileId,
            GetContentType);

        DeletePartialFiles(_optionsMonitor.CurrentValue.StorageDirectory);

        _evictTimer = new Timer(EvictExpired, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        // Start a timer to refresh the file indices periodically
        _indexRefreshTimer = new Timer(RefreshIndices, null, TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(20));
    }

    /// <summary>
    /// Uploads a file to storage
    /// </summary>
    public Task<string> UploadFileAsync(string environment, string filename, Stream fileStream, bool overwrite = false)
        => UploadFileAsync(environment, string.Empty, filename, fileStream, overwrite);

    /// <summary>
    /// Uploads a file into a validated relative folder below the environment
    /// </summary>
    public async Task<string> UploadFileAsync(string environment, string folder, string filename, Stream fileStream, bool overwrite = false)
    {
        if (folder.Length > 0 && !IsSafeRelativePath(folder))
            throw new ArgumentException("Invalid folder", nameof(folder));

        // Validate file
        if (fileStream == null || fileStream.Length == 0)
        {
            throw new ArgumentException("File is empty", nameof(fileStream));
        }

        if (fileStream.Length > _optionsMonitor.CurrentValue.MaxFileSizeBytes)
        {
            throw new ArgumentException($"File size exceeds the maximum allowed size ({_optionsMonitor.CurrentValue.MaxFileSizeBytes / 1024 / 1024}MB)", nameof(fileStream));
        }

        // Validate file extension
        ValidateExtension(filename);

        // Sanitize filename to prevent path traversal attacks
        string safeFilename = folder.Length > 0 ? $"{folder}/{SanitizeFileName(filename)}" : SanitizeFileName(filename);

        // Create a unique file ID
        string fileId = GenerateFileId(environment, safeFilename);

        string filePath = Path.Combine(_optionsMonitor.CurrentValue.StorageDirectory, environment, safeFilename);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        await WriteDurablyAsync(filePath, fileStream, overwrite, safeFilename);
        await EvictAsync(fileId);

        Log.Debug("File {Filename} written to {FilePath}", safeFilename, filePath);

        await _fileSystemIndex.UpdateIndexAsync(environment, safeFilename, new FileSystemIndex.FileMetadata
        {
            FileId = fileId,
            FileName = safeFilename,
            ContentType = GetContentType(safeFilename),
            Size = fileStream.Length,
            LastModified = DateTime.UtcNow,
            IsInMemoryOnly = false
        });

        return fileId;
    }

    /// <summary>
    /// Downloads a file from storage. The caller's authorized route environment must match the one encoded in the file ID
    /// </summary>
    public Task<(Stream FileStream, string Filename, string ContentType)> DownloadFileAsync(string fileId, string expectedEnvironment)
        => DownloadFileAsync(fileId, expectedEnvironment, string.Empty);

    /// <summary>
    /// Downloads a file inside the given folder; throws UnauthorizedAccessException otherwise
    /// </summary>
    public async Task<(Stream FileStream, string Filename, string ContentType)> DownloadFileAsync(string fileId, string expectedEnvironment, string folder)
    {
        var (environment, filename) = ResolveFileId(fileId, expectedEnvironment, folder);
        fileId = GenerateFileId(environment, filename);

        // Check if file exists in memory cache
        if (_memoryCache.TryGetValue(fileId, out var cachedStream))
        {
            // Update last access time
            _lastAccessTimes[fileId] = DateTime.UtcNow;

            // Return a copy of the stream to avoid modification of the cached stream
            var streamCopy = new MemoryStream();
            cachedStream.Position = 0;
            await cachedStream.CopyToAsync(streamCopy);
            streamCopy.Position = 0;

            Log.Debug("File {Filename} retrieved from memory cache with ID {FileId}", filename, fileId);

            return (streamCopy, filename, GetContentType(filename));
        }

        string filePath = DiskPath(environment, filename);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File {filename} not found", filename);
        }

        // Load file into memory stream
        var fileStream = new MemoryStream();
        using (var diskStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            await diskStream.CopyToAsync(fileStream);
        }

        fileStream.Position = 0;

        if (_optionsMonitor.CurrentValue.UseMemoryCache && fileStream.Length <= _optionsMonitor.CurrentValue.MaxFileSizeBytes)
            await CacheAsync(fileId, fileStream.ToArray());

        Log.Debug("File {Filename} retrieved from disk with ID {FileId}", filename, fileId);

        return (fileStream, filename, GetContentType(filename));
    }

    /// <summary>
    /// Deletes a file from storage. The caller's authorized route environment must match the one encoded in the file ID
    /// </summary>
    public Task DeleteFileAsync(string fileId, string expectedEnvironment)
        => DeleteFileAsync(fileId, expectedEnvironment, string.Empty);

    /// <summary>
    /// Deletes a file inside the given folder; throws UnauthorizedAccessException otherwise
    /// </summary>
    public async Task DeleteFileAsync(string fileId, string expectedEnvironment, string folder)
    {
        var (environment, filename) = ResolveFileId(fileId, expectedEnvironment, folder);
        fileId = GenerateFileId(environment, filename);

        await EvictAsync(fileId);

        string filePath = DiskPath(environment, filename);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            Log.Debug("File {Filename} deleted from disk at {FilePath}", filename, filePath);
        }

        if (!Path.IsPathRooted(filename))
            await _fileSystemIndex.UpdateIndexAsync(environment, filename, isDeleted: true);
    }

    /// <summary>
    /// Lists files in an environment
    /// </summary>
    public Task<IEnumerable<FileInfo>> ListFilesAsync(string environment, string? prefix = null)
        => ListFilesAsync(environment, string.Empty, prefix);

    /// <summary>
    /// Lists files inside the given folder; prefix is relative to the folder
    /// </summary>
    public async Task<IEnumerable<FileInfo>> ListFilesAsync(string environment, string folder, string? prefix)
    {
        if (folder.Length > 0 && !Path.IsPathRooted(folder) && !IsSafeRelativePath(folder))
            throw new ArgumentException("Invalid folder", nameof(folder));
        if (!string.IsNullOrEmpty(prefix) && (Path.IsPathRooted(prefix) || prefix.Contains('\\') || prefix.Split('/').Any(s => s is "." or "..")))
            throw new ArgumentException("Invalid prefix", nameof(prefix));

        if (Path.IsPathRooted(folder))
            return ListAbsolute(environment, folder, prefix);

        var files = folder.Length == 0
            ? await _fileSystemIndex.ListFilesAsync(environment, prefix ?? string.Empty)
            : (await _fileSystemIndex.ListFilesAsync(environment, $"{folder}/"))
                .Where(f => f.FileName.StartsWith($"{folder}/", StringComparison.Ordinal)
                    && (string.IsNullOrEmpty(prefix) || f.FileName[(folder.Length + 1)..].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));

        // Convert to FileInfo objects if needed
        return files.Select(f => new FileInfo
        {
            FileId = f.FileId,
            FileName = f.FileName,
            ContentType = f.ContentType,
            Size = f.Size,
            LastModified = f.LastModified,
            Environment = environment,
            IsInMemoryOnly = f.IsInMemoryOnly
        });
    }

    /// <summary>
    /// Uploads a file to an absolute path location
    /// </summary>
    public async Task<string> UploadFileToAbsolutePathAsync(
        string environment,
        string absoluteFilePath,
        Stream fileStream,
        string baseDirectory,
        bool overwrite = false)
    {
        // Validate file
        if (fileStream == null || fileStream.Length == 0)
            throw new ArgumentException("File is empty", nameof(fileStream));

        if (fileStream.Length > _optionsMonitor.CurrentValue.MaxFileSizeBytes)
            throw new ArgumentException($"File size exceeds maximum allowed size", nameof(fileStream));

        // Sanitize the filename part only
        string fileName = Path.GetFileName(absoluteFilePath);
        string sanitizedFileName = SanitizeFileName(fileName);

        // Reconstruct the full path with sanitized filename
        string directoryPath = Path.GetDirectoryName(absoluteFilePath) ?? baseDirectory;
        string fullPath = Path.GetFullPath(Path.Combine(directoryPath, sanitizedFileName));

        // Reject blocked or disallowed extensions on this branch too
        ValidateExtension(sanitizedFileName);

        // Ensure the resolved path stays within the configured base directory
        string baseRoot = Path.GetFullPath(baseDirectory);
        if (!fullPath.StartsWith(baseRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) && fullPath != baseRoot)
            throw new UnauthorizedAccessException("Access denied: resolved path escapes the configured base directory.");

        // Create directory if it doesn't exist
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? baseDirectory);

        await WriteDurablyAsync(fullPath, fileStream, overwrite, sanitizedFileName);
        string fileId = GenerateFileId(environment, fullPath);
        await EvictAsync(fileId);

        Log.Debug("File saved to absolute path: {Path}", fullPath);
        return fileId;
    }

    /// <summary>
    /// Timer callback to refresh file system indices
    /// </summary>
    private async void RefreshIndices(object? state)
    {
        // Timer does not await the callback, so a slow pass would otherwise overlap the next tick
        if (Interlocked.Exchange(ref _refreshRunning, 1) == 1)
            return;

        try
        {
            await _fileSystemIndex.RefreshAllIndicesAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error refreshing file indices");
        }
        finally
        {
            Interlocked.Exchange(ref _refreshRunning, 0);
        }
    }

    /// <summary>
    /// Timer callback that evicts cached files not read within MemoryCacheTimeSeconds
    /// </summary>
    private async void EvictExpired(object? state)
    {
        if (Interlocked.Exchange(ref _evictRunning, 1) == 1)
            return;

        try
        {
            var expired = _lastAccessTimes
                .Where(kv => (DateTime.UtcNow - kv.Value).TotalSeconds > _optionsMonitor.CurrentValue.MemoryCacheTimeSeconds)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var fileId in expired)
                await EvictAsync(fileId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error evicting the file memory cache");
        }
        finally
        {
            Interlocked.Exchange(ref _evictRunning, 0);
        }
    }

    /// <summary>
    /// Evicts least recently read files until at least bytesToFree bytes are released
    /// </summary>
    private async Task EvictOldestAsync(long bytesToFree)
    {
        var freed = 0L;
        foreach (var fileId in _lastAccessTimes.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList())
        {
            if (freed >= bytesToFree)
                break;
            if (_memoryCache.TryGetValue(fileId, out var stream))
                freed += stream.Length;
            await EvictAsync(fileId);
        }
    }

    /// <summary>
    /// Adds a file to the memory cache when no copy is cached
    /// </summary>
    private async Task CacheAsync(string fileId, byte[] content)
    {
        var memoryBudget = _optionsMonitor.CurrentValue.MaxTotalMemoryCacheMB * 1024L * 1024L;
        var projectedUsage = CurrentMemoryUsage + content.Length;
        if (projectedUsage > memoryBudget)
            await EvictOldestAsync(projectedUsage - memoryBudget);

        var stream = new MemoryStream(content, writable: false);
        if (_memoryCache.TryAdd(fileId, stream))
        {
            Interlocked.Add(ref _currentMemoryUsage, stream.Length);
            _lastAccessTimes[fileId] = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Lists files below an absolute folder from disk instead of the storage index
    /// </summary>
    private static IEnumerable<FileInfo> ListAbsolute(string environment, string folder, string? prefix)
    {
        if (!Directory.Exists(folder))
            return [];

        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(PartialSuffix, StringComparison.Ordinal))
            .Select(path => (Path: Path.GetFullPath(path), Relative: Path.GetRelativePath(folder, path).Replace('\\', '/')))
            .Where(f => string.IsNullOrEmpty(prefix) || f.Relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(f =>
            {
                var info = new System.IO.FileInfo(f.Path);
                return new FileInfo
                {
                    FileId = GenerateFileId(environment, f.Path),
                    FileName = f.Relative,
                    ContentType = ContentTypeHelper.GetContentType(f.Path),
                    Size = info.Length,
                    LastModified = info.LastWriteTimeUtc,
                    Environment = environment,
                };
            })
            .ToList();
    }

    internal const string PartialSuffix = ".partial";

    /// <summary>
    /// Deletes partial files left by uploads interrupted before their rename
    /// </summary>
    internal static void DeletePartialFiles(string storageDirectory)
    {
        foreach (var partial in Directory.EnumerateFiles(storageDirectory, "*" + PartialSuffix, SearchOption.AllDirectories))
        {
            try
            {
                File.Delete(partial);
            }
            catch (IOException ex)
            {
                Log.Warning(ex, "Could not delete partial upload {Path}", partial);
            }
        }
    }

    /// <summary>
    /// Saves content to a partial file, flushes it and renames it to filePath; without overwrite an exclusive create reserves the name and other callers get InvalidOperationException
    /// </summary>
    private static async Task WriteDurablyAsync(string filePath, Stream content, bool overwrite, string displayName)
    {
        var claimed = false;
        if (!overwrite)
        {
            try
            {
                new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None).Dispose();
                claimed = true;
            }
            catch (IOException) when (File.Exists(filePath))
            {
                throw new InvalidOperationException($"File {displayName} already exists. Use overwrite=true to replace it.");
            }
        }

        var partialPath = $"{filePath}.{Guid.NewGuid():N}{PartialSuffix}";
        var moved = false;
        try
        {
            await using (var target = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await content.CopyToAsync(target);
                target.Flush(flushToDisk: true);
            }

            File.Move(partialPath, filePath, overwrite: true);
            moved = true;
        }
        finally
        {
            File.Delete(partialPath);
            if (claimed && !moved)
                File.Delete(filePath);
        }
    }

    /// <summary>
    /// Removes the cached copy of a file
    /// </summary>
    private async Task EvictAsync(string fileId)
    {
        _lastAccessTimes.TryRemove(fileId, out _);
        if (_memoryCache.TryRemove(fileId, out var stream))
        {
            Interlocked.Add(ref _currentMemoryUsage, -stream.Length);
            await stream.DisposeAsync();
        }
    }

    /// <summary>
    /// Generates a file ID from environment and filename
    /// </summary>
    private static string GenerateFileId(string environment, string filename) => FileIdProtector.Protect(environment, filename);

    /// <summary>
    /// Parses a file id and checks environment and folder; legacy ids resolve only when folder is empty
    /// </summary>
    private (string Environment, string Filename) ResolveFileId(string fileId, string expectedEnvironment, string folder)
    {
        var absoluteFolder = Path.IsPathRooted(folder);
        if (folder.Length > 0 && !absoluteFolder && !IsSafeRelativePath(folder))
            throw new ArgumentException("Invalid folder", nameof(folder));

        if (!ParseFileId(fileId, out string environment, out string filename))
            throw new ArgumentException("Invalid file ID", nameof(fileId));

        if (!string.Equals(environment, expectedEnvironment, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("File ID does not belong to the requested environment");

        var protectedId = FileIdProtector.IsProtected(fileId);
        bool inside = (absoluteFolder, Path.IsPathRooted(filename)) switch
        {
            (true, true) => protectedId && filename.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, PathComparison),
            (true, false) or (false, true) => false,
            _ when folder.Length == 0 => protectedId || !filename.Contains('/'),
            _ => protectedId && filename.StartsWith($"{folder}/", StringComparison.Ordinal),
        };
        if (!inside)
            throw new UnauthorizedAccessException("File ID does not belong to this endpoint");

        return (environment, filename);
    }

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool IsCanonicalAbsolutePath(string path) =>
        Path.IsPathRooted(path) && string.Equals(Path.GetFullPath(path), path, StringComparison.Ordinal);

    /// <summary>
    /// Disk path of a stored file; relative paths resolve below the environment directory inside the storage root
    /// </summary>
    private string DiskPath(string environment, string filename)
    {
        if (Path.IsPathRooted(filename))
            return filename;

        var storageRoot = Path.GetFullPath(_optionsMonitor.CurrentValue.StorageDirectory);
        var filePath = Path.GetFullPath(Path.Combine(storageRoot, environment, filename));
        if (!filePath.StartsWith(storageRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Access denied: resolved path escapes storage root.");
        return filePath;
    }

    private static readonly char[] UnsafePathChars = [.. Path.GetInvalidFileNameChars(), ':', '\\'];

    /// <summary>
    /// True for a relative path without root, empty, dot or dot-dot segments and without invalid characters
    /// </summary>
    internal static bool IsSafeRelativePath(string? path) =>
        !string.IsNullOrEmpty(path)
        && !Path.IsPathRooted(path)
        && path.Split('/').All(segment => segment.Length > 0 && segment is not "." and not ".." && segment.IndexOfAny(UnsafePathChars) < 0);

    /// <summary>
    /// Validates the environment and filename components decoded from a fileId. Both components must be non-empty single-segment values with no path separators or traversal sequences
    /// </summary>
    internal static bool ValidateFileIdComponents(string? environment, string? filename)
    {
        if (string.IsNullOrWhiteSpace(environment) || string.IsNullOrWhiteSpace(filename))
            return false;

        // Environment must be a single path segment; no separators, no traversal
        if (environment.Contains('/') || environment.Contains('\\') || environment.Contains(".."))
            return false;

        // Reject absolute paths and traversal sequences regardless of platform
        if (Path.IsPathRooted(filename))
            return false;

        // Reject backslashes explicitly; on Linux Path.GetFileName does not treat them
        // as separators, so a Windows-style path would otherwise slip through
        if (filename.Contains('\\'))
            return false;

        // Path.GetFileName returns only the last segment; if it differs, the original
        // contained forward-slash path components
        string safeFilename = Path.GetFileName(filename);
        if (string.IsNullOrEmpty(safeFilename) || safeFilename != filename)
            return false;

        return true;
    }

    /// <summary>
    /// Parses a file ID into environment and filename, rejecting any path traversal in either component
    /// </summary>
    private bool ParseFileId(string fileId, out string environment, out string filename)
    {
        if (FileIdProtector.IsProtected(fileId))
        {
            if (FileIdProtector.TryUnprotect(fileId, out environment, out filename)
                && !environment.Contains('/') && !environment.Contains('\\') && !environment.Contains("..")
                && (IsSafeRelativePath(filename) || IsCanonicalAbsolutePath(filename)))
                return true;

            environment = string.Empty;
            filename = string.Empty;
            return false;
        }

        try
        {
            string decoded = fileId
                .Replace('-', '+')
                .Replace('_', '/');
            while (decoded.Length % 4 != 0)
                decoded += "=";

            byte[] bytes = Convert.FromBase64String(decoded);
            string combined = Encoding.UTF8.GetString(bytes);

            int colonIndex = combined.IndexOf(':');
            if (colonIndex > 0)
            {
                string rawEnv = combined[..colonIndex];
                string rawFile = combined[(colonIndex + 1)..];

                if (!ValidateFileIdComponents(rawEnv, rawFile))
                {
                    Log.Warning("Rejected fileId with invalid components — possible path traversal attempt");
                    environment = string.Empty;
                    filename = string.Empty;
                    return false;
                }

                environment = rawEnv;
                filename = rawFile;
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to parse file ID");
        }
        environment = string.Empty;
        filename = string.Empty;
        return false;
    }

    /// <summary>
    /// Sanitizes a filename to prevent path traversal attacks
    /// </summary>
    /// <summary>
    /// Rejects files whose extension is blocked or not in the configured allow list
    /// </summary>
    private void ValidateExtension(string filename)
    {
        string extension = Path.GetExtension(filename).ToLowerInvariant();

        if (_optionsMonitor.CurrentValue.BlockedExtensions.Contains(extension))
            throw new ArgumentException($"Files with extension {extension} are not allowed", nameof(filename));

        if (_optionsMonitor.CurrentValue.AllowedExtensions.Count > 0 && !_optionsMonitor.CurrentValue.AllowedExtensions.Contains(extension))
            throw new ArgumentException($"Only files with extensions {string.Join(", ", _optionsMonitor.CurrentValue.AllowedExtensions)} are allowed", nameof(filename));
    }

    private string SanitizeFileName(string filename)
    {
        filename = Path.GetFileName(filename);

        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in invalidChars)
        {
            filename = filename.Replace(c, '_');
        }

        return filename;
    }

    /// <summary>
    /// Determines the content type for a filename
    /// </summary>
    private string GetContentType(string filename) => ContentTypeHelper.GetContentType(filename);

    public class FileInfo
    {
        public string FileId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
        public string Environment { get; set; } = string.Empty;
        public bool IsInMemoryOnly { get; set; } = false;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _evictTimer?.Dispose();
            _indexRefreshTimer?.Dispose();

            // Dispose all memory streams
            foreach (var stream in _memoryCache.Values)
            {
                stream.Dispose();
            }

            _memoryCache.Clear();
            _lastAccessTimes.Clear();
        }

        _disposed = true;
    }
}
