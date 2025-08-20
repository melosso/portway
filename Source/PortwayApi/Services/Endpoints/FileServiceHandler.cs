using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using PortwayApi.Services.Caching;
using Serilog;

namespace PortwayApi.Services.Files;

/// <summary>
/// Configuration options for file storage
/// </summary>
public class FileStorageOptions
{
    /// <summary>
    /// Root directory for file storage
    /// </summary>
    public string StorageDirectory { get; set; } = "files";

    /// <summary>
    /// Maximum file size in bytes (default: 50MB)
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024;

    /// <summary>
    /// Cache files in memory before writing to disk
    /// </summary>
    public bool UseMemoryCache { get; set; } = true;

    /// <summary>
    /// How long to keep files in memory cache before flushing to disk (seconds)
    /// </summary>
    public int MemoryCacheTimeSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum size of all files to keep in memory (in MB)
    /// </summary>
    public int MaxTotalMemoryCacheMB { get; set; } = 200;

    /// <summary>
    /// Allowed file extensions (empty means all extensions are allowed)
    /// </summary>
    public List<string> AllowedExtensions { get; set; } = new List<string>();

    /// <summary>
    /// Blocked file extensions (files with these extensions will be rejected)
    /// </summary>
    public List<string> BlockedExtensions { get; set; } = new List<string> { ".exe", ".dll", ".bat", ".sh", ".cmd", ".msi", ".vbs" };
}

/// <summary>
/// Service for handling file operations
/// </summary>
public class FileHandlerService : IDisposable
{
    private readonly FileStorageOptions _options;
    private readonly CacheManager _cacheManager;
    private readonly ConcurrentDictionary<string, MemoryStream> _memoryCache = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastAccessTimes = new();
    private readonly ConcurrentDictionary<string, bool> _dirtyFlags = new();
    private readonly Timer _flushTimer;
    private readonly FileSystemIndex _fileSystemIndex;
    private readonly Serilog.ILogger _logger;
    private long _currentMemoryUsage = 0;
    private bool _disposed = false;

    public FileHandlerService(IOptions<FileStorageOptions> options, CacheManager cacheManager, Serilog.ILogger logger)
    {
        _options = options.Value;
        _cacheManager = cacheManager;
        _logger = Serilog.Log.Logger; // Use Serilog's static logger

        // Create the storage directory if it doesn't exist
        if (!Directory.Exists(_options.StorageDirectory))
        {
            Directory.CreateDirectory(_options.StorageDirectory);
            _logger.Information("✅ Created file storage directory: {Directory}", _options.StorageDirectory);
        }

        // Initialize the file system index
        _fileSystemIndex = new FileSystemIndex(
            _options.StorageDirectory,
            _cacheManager,
            _logger,
            GenerateFileId,
            GetContentType);

        // Start the flush timer to periodically write memory-cached files to disk
        _flushTimer = new Timer(FlushMemoryCache, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        
        // Start a timer to refresh the file indices periodically
        _ = new Timer(_ => _fileSystemIndex.RefreshAllIndicesAsync().ConfigureAwait(false), 
            null, TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(20));
    }

    /// <summary>
    /// Uploads a file to storage
    /// </summary>
    public async Task<string> UploadFileAsync(string environment, string filename, Stream fileStream, bool overwrite = false)
    {
        // Validate file
        if (fileStream == null || fileStream.Length == 0)
        {
            throw new ArgumentException("File is empty", nameof(fileStream));
        }

        if (fileStream.Length > _options.MaxFileSizeBytes)
        {
            throw new ArgumentException($"File size exceeds the maximum allowed size ({_options.MaxFileSizeBytes / 1024 / 1024}MB)", nameof(fileStream));
        }

        // Validate file extension
        string extension = Path.GetExtension(filename).ToLowerInvariant();

        if (_options.BlockedExtensions.Contains(extension))
        {
            throw new ArgumentException($"Files with extension {extension} are not allowed", nameof(filename));
        }

        if (_options.AllowedExtensions.Count > 0 && !_options.AllowedExtensions.Contains(extension))
        {
            throw new ArgumentException($"Only files with extensions {string.Join(", ", _options.AllowedExtensions)} are allowed", nameof(filename));
        }

        // Sanitize filename to prevent path traversal attacks
        string safeFilename = SanitizeFileName(filename);

        // Create a unique file ID
        string fileId = GenerateFileId(environment, safeFilename);

        // Create the environment directory if it doesn't exist
        string environmentDir = Path.Combine(_options.StorageDirectory, environment);
        Directory.CreateDirectory(environmentDir);

        // Determine the file path
        string filePath = Path.Combine(environmentDir, safeFilename);

        // Check if file exists and handle overwrite
        if (File.Exists(filePath) && !overwrite)
        {
            throw new InvalidOperationException($"File {safeFilename} already exists. Use overwrite=true to replace it.");
        }

        // Determine whether to use memory cache
        if (_options.UseMemoryCache)
        {
            // Check if adding this file would exceed memory limits
            if (_currentMemoryUsage + fileStream.Length > _options.MaxTotalMemoryCacheMB * 1024 * 1024)
            {
                // Memory cache is full, flush old files
                await FlushOldestFilesAsync(_currentMemoryUsage + fileStream.Length - _options.MaxTotalMemoryCacheMB * 1024 * 1024);
            }

            // Store in memory first
            var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            // Add to memory cache
            _memoryCache[fileId] = memoryStream;
            _lastAccessTimes[fileId] = DateTime.UtcNow;
            _dirtyFlags[fileId] = true;

            // Update current memory usage
            _currentMemoryUsage += memoryStream.Length;

            Log.Debug("💾 File {Filename} stored in memory cache with ID {FileId}", safeFilename, fileId);
        }
        else
        {
            // Write directly to disk
            using (var fileStream2 = new FileStream(filePath, FileMode.Create))
            {
                await fileStream.CopyToAsync(fileStream2);
            }

            Log.Debug("💾 File {Filename} saved directly to disk at {FilePath}", safeFilename, filePath);
        }

        await _fileSystemIndex.UpdateIndexAsync(environment, safeFilename, new FileSystemIndex.FileMetadata
        {
            FileId = fileId,
            FileName = safeFilename,
            ContentType = GetContentType(safeFilename),
            Size = fileStream.Length,
            LastModified = DateTime.UtcNow,
            IsInMemoryOnly = _options.UseMemoryCache
        });
        
        return fileId;
    }

    /// <summary>
    /// Downloads a file from storage
    /// </summary>
    public async Task<(Stream FileStream, string Filename, string ContentType)> DownloadFileAsync(string fileId)
    {
        // Parse the file ID to get environment and filename
        if (!ParseFileId(fileId, out string environment, out string filename))
        {
            throw new ArgumentException("Invalid file ID", nameof(fileId));
        }

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

            Log.Debug("📤 File {Filename} retrieved from memory cache with ID {FileId}", filename, fileId);

            return (streamCopy, filename, GetContentType(filename));
        }

        // File not in memory, check on disk
        string filePath = Path.Combine(_options.StorageDirectory, environment, filename);

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

        // If memory caching is enabled, store in cache for next time
        if (_options.UseMemoryCache && fileStream.Length <= _options.MaxFileSizeBytes)
        {
            // Check if adding this file would exceed memory limits
            if (_currentMemoryUsage + fileStream.Length > _options.MaxTotalMemoryCacheMB * 1024 * 1024)
            {
                // Memory cache is full, flush old files
                await FlushOldestFilesAsync(_currentMemoryUsage + fileStream.Length - _options.MaxTotalMemoryCacheMB * 1024 * 1024);
            }

            // Create a copy for the cache
            var cacheStream = new MemoryStream();
            fileStream.Position = 0;
            await fileStream.CopyToAsync(cacheStream);
            fileStream.Position = 0;
            cacheStream.Position = 0;

            // Add to memory cache
            _memoryCache[fileId] = cacheStream;
            _lastAccessTimes[fileId] = DateTime.UtcNow;
            _dirtyFlags[fileId] = false; // Not dirty since we just loaded from disk

            // Update current memory usage
            _currentMemoryUsage += cacheStream.Length;

            Log.Debug("💾 File {Filename} loaded into memory cache with ID {FileId}", filename, fileId);
        }

        Log.Debug("📤 File {Filename} retrieved from disk with ID {FileId}", filename, fileId);

        return (fileStream, filename, GetContentType(filename));
    }

    /// <summary>
    /// Deletes a file from storage
    /// </summary>
    public async Task DeleteFileAsync(string fileId)
    {
        // Parse the file ID to get environment and filename
        if (!ParseFileId(fileId, out string environment, out string filename))
        {
            throw new ArgumentException("Invalid file ID", nameof(fileId));
        }

        // Remove from memory cache if present
        if (_memoryCache.TryRemove(fileId, out var cachedStream))
        {
            // Update memory usage
            _currentMemoryUsage -= cachedStream.Length;

            // Clean up
            await cachedStream.DisposeAsync();
            _lastAccessTimes.TryRemove(fileId, out _);
            _dirtyFlags.TryRemove(fileId, out _);

            Log.Debug("🗑️ File {Filename} removed from memory cache with ID {FileId}", filename, fileId);
        }

        // Delete from disk if it exists
        string filePath = Path.Combine(_options.StorageDirectory, environment, filename);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            Log.Debug("🗑️ File {Filename} deleted from disk at {FilePath}", filename, filePath);
        }
        
        await _fileSystemIndex.UpdateIndexAsync(environment, filename, isDeleted: true);
    }

    /// <summary>
    /// Lists files in an environment
    /// </summary>
    public async Task<IEnumerable<FileInfo>> ListFilesAsync(string environment, string? prefix = null)
    {
        // Use the cached index instead of filesystem operations
        var files = await _fileSystemIndex.ListFilesAsync(environment, prefix ?? string.Empty);
        
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

        if (fileStream.Length > _options.MaxFileSizeBytes)
            throw new ArgumentException($"File size exceeds maximum allowed size", nameof(fileStream));

        // Sanitize the filename part only
        string fileName = Path.GetFileName(absoluteFilePath);
        string sanitizedFileName = SanitizeFileName(fileName);
        
        // Reconstruct the full path with sanitized filename
        string directoryPath = Path.GetDirectoryName(absoluteFilePath) ?? baseDirectory;
        string fullPath = Path.Combine(directoryPath, sanitizedFileName);

        // Create directory if it doesn't exist
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? baseDirectory);

        // Generate file ID for absolute paths (special encoding)
        string fileId = GenerateAbsoluteFileId(environment, fullPath);

        // Check if file exists
        if (File.Exists(fullPath) && !overwrite)
            throw new InvalidOperationException($"File already exists at {fullPath}");

        // Write file
        using var fileStreamWriter = new FileStream(fullPath, FileMode.Create);
        await fileStream.CopyToAsync(fileStreamWriter);

        Log.Debug("💾 File saved to absolute path: {Path}", fullPath);
        return fileId;
    }

    /// <summary>
    /// Generates a special file ID for absolute path files
    /// </summary>
    private string GenerateAbsoluteFileId(string environment, string absolutePath)
    {
        // Special encoding for absolute paths
        string combined = $"ABS:{environment}:{absolutePath}";
        byte[] bytes = Encoding.UTF8.GetBytes(combined);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>
    /// Flushes all dirty files from memory to disk
    /// </summary>
    public async Task FlushAllAsync()
    {
        foreach (var fileId in _memoryCache.Keys)
        {
            // Check if file is dirty
            if (_dirtyFlags.TryGetValue(fileId, out var isDirty) && isDirty)
            {
                await FlushFileToDiskAsync(fileId);
            }
        }

        Log.Information("✅ Flushed all dirty files from memory cache to disk");
    }

    /// <summary>
    /// Flushes a specific file from memory to disk
    /// </summary>
    private async Task FlushFileToDiskAsync(string fileId)
    {
        if (!_memoryCache.TryGetValue(fileId, out var memoryStream))
        {
            return; // File not in memory cache
        }

        // Check if file is dirty
        if (!_dirtyFlags.TryGetValue(fileId, out var isDirty) || !isDirty)
        {
            return; // File is not dirty, no need to flush
        }

        // Parse the file ID to get environment and filename
        if (!ParseFileId(fileId, out string environment, out string filename))
        {
            Log.Warning("⚠️ Invalid file ID in memory cache: {FileId}", fileId);
            return;
        }

        // Create the environment directory if it doesn't exist
        string environmentDir = Path.Combine(_options.StorageDirectory, environment);
        Directory.CreateDirectory(environmentDir);

        // Determine the file path
        string filePath = Path.Combine(environmentDir, filename);

        try
        {
            // Write to disk
            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                memoryStream.Position = 0;
                await memoryStream.CopyToAsync(fileStream);
            }

            // Mark as not dirty
            _dirtyFlags[fileId] = false;

            Log.Debug("💾 Flushed file {Filename} from memory to disk at {FilePath}", filename, filePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error flushing file {Filename} to disk", filename);
        }
    }

    /// <summary>
    /// Timer callback to flush memory cache to disk
    /// </summary>
    private async void FlushMemoryCache(object? state)
    {
        try
        {
            // Get all dirty files
            var dirtyFiles = _dirtyFlags
                .Where(kv => kv.Value)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var fileId in dirtyFiles)
            {
                await FlushFileToDiskAsync(fileId);
            }

            // Find old files to remove from memory
            var filesToRemove = _lastAccessTimes
                .Where(kv => (DateTime.UtcNow - kv.Value).TotalSeconds > _options.MemoryCacheTimeSeconds)
                .Select(kv => kv.Key)
                .ToList();

            if (filesToRemove.Any())
            {
                // Remove old files from memory
                foreach (var fileId in filesToRemove)
                {
                    // Make sure file is flushed to disk if dirty
                    if (_dirtyFlags.TryGetValue(fileId, out var isDirty) && isDirty)
                    {
                        await FlushFileToDiskAsync(fileId);
                    }

                    // Remove from memory cache
                    if (_memoryCache.TryRemove(fileId, out var stream))
                    {
                        _currentMemoryUsage -= stream.Length;
                        await stream.DisposeAsync();
                    }

                    _lastAccessTimes.TryRemove(fileId, out _);
                    _dirtyFlags.TryRemove(fileId, out _);
                }

                Log.Debug("🧹 Removed {Count} old files from memory cache", filesToRemove.Count);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error flushing memory cache");
        }
    }

    /// <summary>
    /// Flushes oldest files from memory until the specified amount of space is freed
    /// </summary>
    private async Task FlushOldestFilesAsync(long bytesToFree)
    {
        if (bytesToFree <= 0)
        {
            return; // Nothing to free
        }

        // Sort files by last access time
        var filesOrderedByAge = _lastAccessTimes
            .OrderBy(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToList();

        long freedBytes = 0;

        foreach (var fileId in filesOrderedByAge)
        {
            // Skip if this file doesn't exist in memory anymore
            if (!_memoryCache.TryGetValue(fileId, out var memoryStream))
            {
                continue;
            }

            // Check if file is dirty
            if (_dirtyFlags.TryGetValue(fileId, out var isDirty) && isDirty)
            {
                // Flush to disk first
                await FlushFileToDiskAsync(fileId);
            }

            // Remove from memory
            if (_memoryCache.TryRemove(fileId, out var stream))
            {
                freedBytes += stream.Length;
                _currentMemoryUsage -= stream.Length;
                await stream.DisposeAsync();
            }

            _lastAccessTimes.TryRemove(fileId, out _);
            _dirtyFlags.TryRemove(fileId, out _);

            // Check if we've freed enough space
            if (freedBytes >= bytesToFree)
            {
                break;
            }
        }

        Log.Debug("🧹 Freed {FreedBytes} bytes from memory cache by removing oldest files", freedBytes);
    }

    /// <summary>
    /// Generates a file ID from environment and filename
    /// </summary>
    private string GenerateFileId(string environment, string filename)
    {
        // Ensure fileName can contain subdirectory paths
        string combined = $"{environment}:{filename}";
        byte[] bytes = Encoding.UTF8.GetBytes(combined);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>
    /// Parses a file ID into environment and filename
    /// </summary>
    private bool ParseFileId(string fileId, out string environment, out string filename)
    {
        try
        {
            // Decode the file ID
            string decoded = fileId
                .Replace('-', '+')
                .Replace('_', '/');
            // Add padding if needed
            while (decoded.Length % 4 != 0)
            {
                decoded += "=";
            }
            byte[] bytes = Convert.FromBase64String(decoded);
            string combined = Encoding.UTF8.GetString(bytes);
            // Split on first colon to separate environment from filepath
            int colonIndex = combined.IndexOf(':');
            if (colonIndex > 0)
            {
                environment = combined.Substring(0, colonIndex);
                filename = combined.Substring(colonIndex + 1);
                return true;
            }
        }
        catch
        {
            // Invalid file ID format
        }
        environment = string.Empty;
        filename = string.Empty;
        return false;
    }

    /// <summary>
    /// Sanitizes a filename to prevent path traversal attacks
    /// </summary>
    private string SanitizeFileName(string filename)
    {
        // Remove any directory components
        filename = Path.GetFileName(filename);

        // Replace invalid characters
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
    private string GetContentType(string filename)
    {
        string extension = Path.GetExtension(filename).ToLowerInvariant();

        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".ico" => "image/x-icon",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            ".doc" or ".docx" => "application/msword",
            ".xls" or ".xlsx" => "application/vnd.ms-excel",
            ".ppt" or ".pptx" => "application/vnd.ms-powerpoint",
            ".zip" => "application/zip",
            ".rar" => "application/x-rar-compressed",
            ".tar" => "application/x-tar",
            ".gz" => "application/gzip",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".txt" => "text/plain",
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".mp3" => "audio/mpeg",
            ".mp4" => "video/mp4",
            ".avi" => "video/x-msvideo",
            ".mov" => "video/quicktime",
            ".wmv" => "video/x-ms-wmv",
            _ => "application/octet-stream"
        };
    }

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
            // Flush any dirty files before shutting down
            FlushAllAsync().GetAwaiter().GetResult();

            // Dispose the timer
            _flushTimer?.Dispose();

            // Dispose all memory streams
            foreach (var stream in _memoryCache.Values)
            {
                stream.Dispose();
            }

            _memoryCache.Clear();
            _lastAccessTimes.Clear();
            _dirtyFlags.Clear();
        }

        _disposed = true;
    }
}

public class FileSystemIndex
{
    private readonly string _baseDirectory;
    private readonly ICacheProvider _cacheProvider;
    private readonly SemaphoreSlim _indexLock = new SemaphoreSlim(1, 1);
    private readonly Serilog.ILogger _logger;
    private readonly ConcurrentDictionary<string, Dictionary<string, FileMetadata>> _environmentIndices = new();
    private readonly Func<string, string, string> _fileIdGenerator;
    private readonly Func<string, string> _contentTypeResolver;
    
    public FileSystemIndex(
        string baseDirectory, 
        ICacheProvider cacheProvider, 
        Serilog.ILogger logger,
        Func<string, string, string> fileIdGenerator,
        Func<string, string> contentTypeResolver)
    {
        _baseDirectory = baseDirectory;
        _cacheProvider = cacheProvider;
        _logger = logger;
        _fileIdGenerator = fileIdGenerator;
        _contentTypeResolver = contentTypeResolver;
    }
    
    // File metadata stored in cache
    public class FileMetadata
    {
        public string FileId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
        public bool IsInMemoryOnly { get; set; }
    }
    
    public async Task<Dictionary<string, FileMetadata>> GetDirectoryIndexAsync(string environment, bool forceRefresh = false)
    {
        string cacheKey = $"file:index:{environment}";
        // Try to get from cache first if not forcing refresh
        if (!forceRefresh)
        {
            var cachedIndex = await _cacheProvider.GetAsync<Dictionary<string, FileMetadata>>(cacheKey);
            if (cachedIndex != null)
            {
                return cachedIndex;
            }
        }
        // Cache miss or force refresh - rebuild index from filesystem
        await _indexLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (!forceRefresh)
            {
                var cachedIndex = await _cacheProvider.GetAsync<Dictionary<string, FileMetadata>>(cacheKey);
                if (cachedIndex != null)
                {
                    return cachedIndex;
                }
            }
            string environmentDir = Path.Combine(_baseDirectory, environment);
            Dictionary<string, FileMetadata> index = new();
            if (Directory.Exists(environmentDir))
            {
                // Use recursive enumeration to scan ALL subdirectories
                foreach (var file in Directory.EnumerateFiles(environmentDir, "*", SearchOption.AllDirectories))
                {
                    var fileInfo = new FileInfo(file);
                    // Calculate relative path from environment directory
                    string relativePath = Path.GetRelativePath(environmentDir, file);
                    // Normalize path separators to forward slashes for consistency
                    string normalizedPath = relativePath.Replace('\\', '/');
                    index[normalizedPath] = new FileMetadata
                    {
                        FileId = _fileIdGenerator(environment, normalizedPath),
                        FileName = normalizedPath,
                        ContentType = _contentTypeResolver(normalizedPath),
                        Size = fileInfo.Length,
                        LastModified = fileInfo.LastWriteTimeUtc,
                        IsInMemoryOnly = false
                    };
                }
            }
            // Cache the index with expiration
            await _cacheProvider.SetAsync(cacheKey, index, TimeSpan.FromMinutes(30));
            // Also store in memory for very fast access
            _environmentIndices[environment] = index;
            _logger.Debug("📂 Built file index for environment {Environment}: {Count} files", environment, index.Count);
            return index;
        }
        finally
        {
            _indexLock.Release();
        }
    }
    
    // Update index when files are added/modified/deleted
    public async Task UpdateIndexAsync(string environment, string fileName, FileMetadata? metadata = null, bool isDeleted = false)
    {
        string cacheKey = $"file:index:{environment}";
        
        await _indexLock.WaitAsync();
        try
        {
            // Get current index (from memory or rebuild if needed)
            Dictionary<string, FileMetadata> index;
            if (_environmentIndices.TryGetValue(environment, out var existingIndex))
            {
                index = existingIndex;
            }
            else
            {
                // Load from cache or rebuild
                index = await GetDirectoryIndexAsync(environment);
            }
            
            if (isDeleted)
            {
                index.Remove(fileName);
                _logger.Debug("🗑️ Removed {FileName} from file index for {Environment}", fileName, environment);
            }
            else if (metadata != null)
            {
                index[fileName] = metadata;
                _logger.Debug("📝 Updated index for {FileName} in {Environment}", fileName, environment);
            }
            
            // Update cache
            await _cacheProvider.SetAsync(cacheKey, index, TimeSpan.FromMinutes(30));
            
            // Update in-memory index
            _environmentIndices[environment] = index;
        }
        finally
        {
            _indexLock.Release();
        }
    }
    
    // List files with efficient filtering using the index
    public async Task<IEnumerable<FileMetadata>> ListFilesAsync(string environment, string? prefix = null)
    {
        var index = await GetDirectoryIndexAsync(environment);
        // Efficiently filter in memory - handle both filename and path prefixes
        if (string.IsNullOrEmpty(prefix))
        {
            return index.Values;
        }
        // Normalize prefix to use forward slashes
        string normalizedPrefix = prefix.Replace('\\', '/');
        return index.Values.Where(f =>
            f.FileName.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(f.FileName).StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase)
        );
    }
    
    // Periodic refresh to catch files added outside the API
    public async Task RefreshAllIndicesAsync()
    {
        foreach (var environment in _environmentIndices.Keys.ToList())
        {
            await GetDirectoryIndexAsync(environment, forceRefresh: true);
        }
        
        _logger.Information("🔄 Refreshed all file indices");
    }
}
