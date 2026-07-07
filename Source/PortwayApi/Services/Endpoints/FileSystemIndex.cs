using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using PortwayApi.Helpers;
using PortwayApi.Services.Caching;
using Serilog;

namespace PortwayApi.Services.Files;

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
            return await GetDirectoryIndexInternalAsync(environment, forceRefresh);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private async Task<Dictionary<string, FileMetadata>> GetDirectoryIndexInternalAsync(string environment, bool forceRefresh = false)
    {
        string cacheKey = $"file:index:{environment}";
        
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
                var fileInfo = new System.IO.FileInfo(file);
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
                // Load from cache or rebuild (using internal method to avoid deadlock)
                index = await GetDirectoryIndexInternalAsync(environment);
            }
            
            if (isDeleted)
            {
                index.Remove(fileName);
                _logger.Debug("Removed {FileName} from file index for {Environment}", fileName, environment);
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
        
        _logger.Information("Refreshed all file indices");
    }
}
