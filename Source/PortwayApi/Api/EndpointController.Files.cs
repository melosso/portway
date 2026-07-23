using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using System.Data.Common;
using PortwayApi.Services.Providers;

using Dapper;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Xml.Linq;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Interfaces;
using PortwayApi.Services;
using PortwayApi.Services.Files;
using Serilog;
using System.Runtime.CompilerServices;

namespace PortwayApi.Api;

public partial class EndpointController
{
    /// <summary>Handle file uploads</summary>
    [HttpPost("{env}/files/{**catchall}")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UploadFileAsync(
        string env,
        string catchall,
        [FromForm] IFormFile file,
        [FromQuery] bool overwrite = false)
    {
        try
        {
            // Parse the catchall to extract namespace and endpoint information for files
            string endpointName;
            string? namespaceName = null;
            string? subpath = null;
            
            var segments = catchall.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return PortwayResults.BadRequest(this, "Missing endpoint name in the URL path");
            }
            
            // Check if we have namespace/endpoint format (2+ segments)
            if (segments.Length >= 2)
            {
                // Could be namespace/endpoint/subpath or just endpoint/subpath; Try to determine if first segment is a namespace by checking if namespace/endpoint exists
                var potentialNamespace = segments[0];
                var potentialEndpoint = segments[1];
                var allFileEndpoints = EndpointHandler.GetFileEndpoints();
                
                // Check if namespace/endpoint key exists
                if (allFileEndpoints.ContainsKey($"{potentialNamespace}/{potentialEndpoint}"))
                {
                    namespaceName = potentialNamespace;
                    endpointName = potentialEndpoint;
                    if (segments.Length > 2)
                    {
                        subpath = string.Join('/', segments.Skip(2));
                    }
                }
                else
                {
                    // Fallback to treating first segment as endpoint name
                    endpointName = segments[0];
                    if (segments.Length > 1)
                    {
                        subpath = string.Join('/', segments.Skip(1));
                    }
                }
            }
            else
            {
                endpointName = segments[0];
            }
            
            // Check if this endpoint exists
            if (TryResolveEndpoint(EndpointType.Files, endpointName, namespaceName, out var endpoint) is { } resolveError)
            {
                return resolveError;
            }

            // Check environment restrictions
            var (isAllowed, errorResponse) = ValidateEnvironmentRestrictions(env, namespaceName, endpointName, EndpointType.Files);
            if (!isAllowed)
            {
                return errorResponse!;
            }

            // Validate file
            if (file == null || file.Length == 0)
            {
                return PortwayResults.BadRequest(this, "No file was uploaded");
            }
            
            // Get storage options from endpoint definition
            var baseDirectory = endpoint?.Properties != null && endpoint.Properties.TryGetValue("BaseDirectory", out var baseDirObj) 
                ? baseDirObj?.ToString() ?? string.Empty
                : string.Empty;

            // PROCESS THE BASE DIRECTORY TO REPLACE PLACEHOLDERS
            baseDirectory = ProcessBaseDirectory(baseDirectory, env);
                
            var allowedExtensions = endpoint?.Properties != null && endpoint.Properties.TryGetValue("AllowedExtensions", out var extensionsObj) 
                && extensionsObj is List<string> extensions
                ? extensions
                : new List<string>();
            
            // Construct the target filename
            string filename = file.FileName;
            
            // Add subpath if provided
            if (!string.IsNullOrEmpty(subpath))
            {
                filename = Path.Combine(subpath, filename);
            }
            
            // Add base directory if configured
            if (!string.IsNullOrEmpty(baseDirectory))
            {
                filename = Path.Combine(baseDirectory, filename);
            }
            
            // Normalize path separators
            filename = filename.Replace('\\', '/');
            
            // Validate file extension
            string extension = Path.GetExtension(filename).ToLowerInvariant();
            if (allowedExtensions.Count > 0 && !allowedExtensions.Contains(extension))
            {
                return PortwayResults.UnsupportedMediaType(this, $"Files with extension {extension} are not allowed for this endpoint");
            }
            
            // Upload the file
            using var stream = file.OpenReadStream();
            string fileId;
            
            // Check if we should use absolute path handling
            if (!string.IsNullOrEmpty(baseDirectory) && Path.IsPathRooted(baseDirectory))
            {
                // For absolute paths, construct the full path
                string absoluteFilePath = filename;
                if (!string.IsNullOrEmpty(subpath))
                {
                    absoluteFilePath = Path.Combine(subpath, file.FileName);
                }
                absoluteFilePath = Path.Combine(baseDirectory, absoluteFilePath);
                
                // Use the absolute path upload method
                fileId = await _fileHandlerService.UploadFileToAbsolutePathAsync(env, absoluteFilePath, stream, baseDirectory, overwrite);
            }
            else
            {
                // Use the standard relative path upload method
                fileId = await _fileHandlerService.UploadFileAsync(env, filename, stream, overwrite);
            }
            
            // Return success with file info; preserve namespace in the download URL so it round-trips
            var fileEndpointPath = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}/{endpointName}" : endpointName;
            var fileUrl = $"/api/{env}/files/{fileEndpointPath}/{fileId}";
            return PortwayResults.FileCreate(this, fileUrl, fileId, filename, file.ContentType, file.Length, fileUrl);
        }
        catch (ArgumentException ex)
        {
            return PortwayResults.BadRequest(this, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return PortwayResults.Conflict(this, ex.Message);
        }
        catch (Exception ex)
        {
            return HandleUnexpectedError(ex, "file upload", Request.Path, "An error occurred while uploading the file");
        }
    }

    /// <summary>Handle file downloads</summary>
    [HttpGet("{env}/files/{**catchall}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status416RangeNotSatisfiable)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DownloadFileAsync(
        string env,
        string catchall)
    {
        try
        {
            // Extract the namespace, endpoint name, and file ID from the catchall
            var (namespaceName, endpointName, fileId) = ParseFileEndpointPath(catchall);
            if (string.IsNullOrEmpty(endpointName) || string.IsNullOrEmpty(fileId))
            {
                return PortwayResults.BadRequest(this, "Missing endpoint name or file ID in the URL path");
            }

            // A trailing "list" segment means list the endpoint's files (namespaced form)
            if (string.Equals(fileId, "list", StringComparison.OrdinalIgnoreCase))
            {
                return await ListFilesCore(env, namespaceName, endpointName, Request.Query["prefix"].FirstOrDefault());
            }

            // Check if this endpoint exists
            if (TryResolveEndpoint(EndpointType.Files, endpointName, namespaceName, out var endpoint) is { } resolveError)
            {
                return resolveError;
            }

            // Check environment restrictions
            var (isAllowed, errorResponse) = ValidateEnvironmentRestrictions(env, namespaceName, endpointName, EndpointType.Files);
            if (!isAllowed)
            {
                return errorResponse!;
            }

            // Download the file
            var (fileStream, filename, contentType) = await _fileHandlerService.DownloadFileAsync(fileId);

            // Return the file
            return File(fileStream, contentType, filename);
        }
        catch (FileNotFoundException ex)
        {
            return PortwayResults.NotFound(this, $"File not found: {ex.FileName}");
        }
        catch (ArgumentException ex)
        {
            return PortwayResults.BadRequest(this, ex.Message);
        }
        catch (Exception ex)
        {
            return HandleUnexpectedError(ex, "file download", Request.Path, "An error occurred while downloading the file");
        }
    }

    /// <summary>Handle file deletions</summary>
    [HttpDelete("{env}/files/{**catchall}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteFileAsync(
        string env,
        string catchall)
    {
        try
        {
            // Extract the namespace, endpoint name, and file ID from the catchall
            var (namespaceName, endpointName, fileId) = ParseFileEndpointPath(catchall);
            if (string.IsNullOrEmpty(endpointName) || string.IsNullOrEmpty(fileId))
            {
                return PortwayResults.BadRequest(this, "Missing endpoint name or file ID in the URL path");
            }

            // Check if this endpoint exists
            if (TryResolveEndpoint(EndpointType.Files, endpointName, namespaceName, out var endpoint) is { } resolveError)
            {
                return resolveError;
            }

            // Check environment restrictions
            var (isAllowed, errorResponse) = ValidateEnvironmentRestrictions(env, namespaceName, endpointName, EndpointType.Files);
            if (!isAllowed)
            {
                return errorResponse!;
            }

            // Delete the file
            await _fileHandlerService.DeleteFileAsync(fileId);

            return PortwayResults.Mutation(this, "File deleted successfully");
        }
        catch (ArgumentException ex)
        {
            return PortwayResults.BadRequest(this, ex.Message);
        }
        catch (Exception ex)
        {
            return HandleUnexpectedError(ex, "file delete", Request.Path, "An error occurred while deleting the file");
        }
    }

    /// <summary>List files in an endpoint</summary>
    [HttpGet("{env}/files/{endpointName}/list")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ListFilesAsync(
        string env,
        string endpointName,
        [FromQuery] string? prefix = null)
        => await ListFilesCore(env, null, endpointName, prefix);

    /// <summary>Lists files for a (possibly namespaced) file endpoint</summary>
    private async Task<IActionResult> ListFilesCore(string env, string? namespaceName, string endpointName, string? prefix)
    {
        try
        {
            // Check if this endpoint exists (namespace-aware)
            if (TryResolveEndpoint(EndpointType.Files, endpointName, namespaceName, out var endpoint) is { } resolveError)
            {
                return resolveError;
            }

            // Check environment restrictions
            var (isAllowed, errorResponse) = ValidateEnvironmentRestrictions(env, namespaceName, endpointName, EndpointType.Files);
            if (!isAllowed)
            {
                return errorResponse!;
            }

            // Get base directory for this endpoint
            var baseDirectory = (endpoint!.Properties != null && endpoint.Properties.TryGetValue("BaseDirectory", out var baseDirObj))
                ? baseDirObj?.ToString() ?? string.Empty
                : string.Empty;

            // PROCESS THE BASE DIRECTORY TO REPLACE PLACEHOLDERS
            baseDirectory = ProcessBaseDirectory(baseDirectory, env);

            // Prepare the prefix by combining base directory and provided prefix
            if (!string.IsNullOrEmpty(baseDirectory))
            {
                prefix = string.IsNullOrEmpty(prefix)
                    ? baseDirectory
                    : Path.Combine(baseDirectory, prefix).Replace('\\', '/');
            }

            // List the files
            var files = await _fileHandlerService.ListFilesAsync(env, prefix);

            // Add download URLs; preserve namespace so the links round-trip
            var endpointPath = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}/{endpointName}" : endpointName;
            var filesWithUrls = files.Select(f => new
            {
                fileId = f.FileId,
                fileName = f.FileName,
                contentType = f.ContentType,
                size = f.Size,
                lastModified = f.LastModified,
                url = $"/api/{env}/files/{endpointPath}/{f.FileId}",
                isInMemoryOnly = f.IsInMemoryOnly
            }).ToList();

            // Set pagination headers for consistency with other endpoints
            ResponseHeaderHelper.SetPaginationHeaders(HttpContext, filesWithUrls.Count, filesWithUrls.Count, false);

            return PortwayResults.Collection(this, filesWithUrls);
        }
        catch (Exception ex)
        {
            return HandleUnexpectedError(ex, "file listing", Request.Path, "An error occurred while listing files");
        }
    }

}
