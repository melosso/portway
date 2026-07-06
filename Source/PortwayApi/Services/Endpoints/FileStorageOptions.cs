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

/// <summary>Configuration options for file storage</summary>
public class FileStorageOptions
{
    /// <summary>Root directory for file storage</summary>
    public string StorageDirectory { get; set; } = "files";

    /// <summary>Maximum file size in bytes (default: 50MB)</summary>
    public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024;

    /// <summary>Cache files in memory before writing to disk</summary>
    public bool UseMemoryCache { get; set; } = true;

    /// <summary>How long to keep files in memory cache before flushing to disk (seconds)</summary>
    public int MemoryCacheTimeSeconds { get; set; } = 60;

    /// <summary>Maximum size of all files to keep in memory (in MB)</summary>
    public int MaxTotalMemoryCacheMB { get; set; } = 200;

    /// <summary>Allowed file extensions (empty eq. all are allowed)</summary>
    public List<string> AllowedExtensions { get; set; } = new List<string>();

    /// <summary>Blocked file extensions</summary>
    public List<string> BlockedExtensions { get; set; } = new List<string> 
        { 
            ".exe", ".dll", ".bat", ".sh", ".cmd", ".msi", ".vbs",
            ".ps1", ".scr", ".wsf", ".hta", ".cpl", ".msc", ".pif", ".reg", ".com", ".vbe", ".wsh",
            ".php", ".php3", ".php4", ".php5", ".phtml", 
            ".asp", ".aspx", ".ashx", ".asmx", 
            ".jsp", ".jspx", ".cgi", ".pl", ".py", ".rb",
            ".jar", ".bin", ".elf", ".app", ".dmg", ".run",
            ".docm", ".xlsm", ".pptm"
        };
}
