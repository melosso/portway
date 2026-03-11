using System.IO;

namespace PortwayApi.Helpers;

/// <summary>
/// Provides content type mappings for file extensions
/// </summary>
public static class ContentTypeHelper
{
    /// <summary>
    /// Determines the content type for a filename based on its extension
    /// </summary>
    public static string GetContentType(string filename)
    {
        string extension = Path.GetExtension(filename).ToLowerInvariant();

        return extension switch
        {
            // Images
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".ico" => "image/x-icon",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".avif" => "image/avif",
            ".heic" => "image/heic",
            ".heif" => "image/heif",
            ".tiff" or ".tif" => "image/tiff",

            // Documents
            ".pdf" => "application/pdf",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".md" => "text/markdown",
            ".yaml" or ".yml" => "application/x-yaml",
            ".toml" => "application/toml",
            ".graphql" => "application/graphql",

            // Office
            ".doc" => "application/msword",
            ".xls" => "application/vnd.ms-excel",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".odt" => "application/vnd.oasis.opendocument.text",
            ".ods" => "application/vnd.oasis.opendocument.spreadsheet",
            ".odp" => "application/vnd.oasis.opendocument.presentation",

            // Archives
            ".zip" => "application/zip",
            ".rar" => "application/x-rar-compressed",
            ".tar" => "application/x-tar",
            ".gz" => "application/gzip",
            ".7z" => "application/x-7z-compressed",

            // Webs
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" or ".mjs" => "text/javascript",
            ".wasm" => "application/wasm",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",

            // Modelling
            ".gltf" => "model/gltf+json",
            ".glb" => "model/gltf-binary",

            // Audio
            ".mp3" => "audio/mpeg",
            ".ogg" => "audio/ogg",
            ".wav" => "audio/wav",
            ".flac" => "audio/flac",

            // Video
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".avi" => "video/x-msvideo",
            ".mov" => "video/quicktime",
            ".wmv" => "video/x-ms-wmv",
            ".mkv" => "video/x-matroska",

            // Fallback
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Returns a dictionary of extension to content type for static file caching
    /// </summary>
    public static IReadOnlyDictionary<string, string> StaticFileExtensions => _staticFileExtensions;

    private static readonly Dictionary<string, string> _staticFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Images
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png", "image/png" },
        { ".gif", "image/gif" },
        { ".bmp", "image/bmp" },
        { ".ico", "image/x-icon" },
        { ".svg", "image/svg+xml" },
        { ".webp", "image/webp" },
        { ".avif", "image/avif" },
        { ".heic", "image/heic" },
        { ".heif", "image/heif" },
        { ".tiff", "image/tiff" },
        { ".tif", "image/tiff" },

        // Documents
        { ".pdf", "application/pdf" },
        { ".json", "application/json" },
        { ".xml", "application/xml" },
        { ".txt", "text/plain" },
        { ".csv", "text/csv" },
        { ".md", "text/markdown" },
        { ".yaml", "application/x-yaml" },
        { ".yml", "application/x-yaml" },
        { ".toml", "application/toml" },
        { ".graphql", "application/graphql" },

        // Office
        { ".doc", "application/msword" },
        { ".xls", "application/vnd.ms-excel" },
        { ".ppt", "application/vnd.ms-powerpoint" },
        { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
        { ".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation" },
        { ".odt", "application/vnd.oasis.opendocument.text" },
        { ".ods", "application/vnd.oasis.opendocument.spreadsheet" },
        { ".odp", "application/vnd.oasis.opendocument.presentation" },

        // Archives
        { ".zip", "application/zip" },
        { ".rar", "application/x-rar-compressed" },
        { ".tar", "application/x-tar" },
        { ".gz", "application/gzip" },
        { ".7z", "application/x-7z-compressed" },

        // Webs
        { ".html", "text/html" },
        { ".htm", "text/html" },
        { ".css", "text/css" },
        { ".js", "text/javascript" },
        { ".mjs", "text/javascript" },
        { ".wasm", "application/wasm" },
        { ".woff", "font/woff" },
        { ".woff2", "font/woff2" },

        // Modelling
        { ".gltf", "model/gltf+json" },
        { ".glb", "model/gltf-binary" },

        // Audio
        { ".mp3", "audio/mpeg" },
        { ".ogg", "audio/ogg" },
        { ".wav", "audio/wav" },
        { ".flac", "audio/flac" },

        // Video
        { ".mp4", "video/mp4" },
        { ".webm", "video/webm" },
        { ".avi", "video/x-msvideo" },
        { ".mov", "video/quicktime" },
        { ".wmv", "video/x-ms-wmv" },
        { ".mkv", "video/x-matroska" },
    };

    /// <summary>
    /// Gets cache duration based on file extension category
    /// </summary>
    public static TimeSpan GetCacheDuration(string extension)
    {
        var ext = extension.ToLowerInvariant();
        
        // HTML - 5 minutes (may change frequently)
        if (ext == ".html" || ext == ".htm")
            return TimeSpan.FromMinutes(5);
        
        // JS/CSS - 1 hour (versioned typically)
        if (ext == ".js" || ext == ".mjs" || ext == ".css")
            return TimeSpan.FromHours(1);
        
        // Images - 24 hours (typically immutable)
        if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || 
            ext == ".ico" || ext == ".svg" || ext == ".webp" || ext == ".avif" ||
            ext == ".heic" || ext == ".heif" || ext == ".tiff" || ext == ".tif" ||
            ext == ".bmp")
            return TimeSpan.FromDays(1);
        
        // Fonts - 7 days (rarely change)
        if (ext == ".woff" || ext == ".woff2" || ext == ".ttf" || ext == ".otf")
            return TimeSpan.FromDays(7);
        
        // Default - 30 minutes
        return TimeSpan.FromMinutes(30);
    }
}
