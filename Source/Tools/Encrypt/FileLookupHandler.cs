using System;
using System.IO;

namespace EncryptTool
{
    // Generic file lookup handler for reuse
    public static class FileLookupHandler
    {
        // Returns all files matching pattern under root
        public static string[] FindFiles(string root, string pattern)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return Array.Empty<string>();
            return Directory.GetFiles(root, pattern, SearchOption.AllDirectories);
        }

        // Returns true if the file is the root file (e.g., settings.json at root)
        public static bool IsRootFile(string root, string file, string fileName = "settings.json")
        {
            var filePath = Path.GetFullPath(file).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fileNameOnly = Path.GetFileName(filePath);
            var fileDir = Path.GetDirectoryName(filePath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? string.Empty;
            var rootDir = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            bool isRoot = string.Equals(fileNameOnly, fileName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(fileDir, rootDir, StringComparison.OrdinalIgnoreCase);
            if (isRoot)
            {
                Serilog.Log.Debug($"[IsRootFile] SKIP: file={filePath}, fileDir={fileDir}, rootDir={rootDir}");
            }
            else
            {
                Serilog.Log.Debug($"[IsRootFile] CHECK: file={filePath}, fileDir={fileDir}, rootDir={rootDir}");
            }
            return isRoot;
        }
    }
}
