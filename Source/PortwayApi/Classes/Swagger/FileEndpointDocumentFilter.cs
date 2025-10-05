using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using PortwayApi.Classes;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PortwayApi.Classes.Swagger;

public class FileEndpointDocumentFilter : IDocumentFilter
{
    private readonly ILogger<FileEndpointDocumentFilter> _logger;

    public FileEndpointDocumentFilter(ILogger<FileEndpointDocumentFilter> logger)
    {
        _logger = logger;
    }

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        try
        {
            // Load file endpoint definitions
            var fileEndpoints = EndpointHandler.GetFileEndpoints();
            
            // Get allowed environments for parameter description
            var allowedEnvironments = GetAllowedEnvironments();

            // Add schema definitions for file models
            AddFileSchemas(swaggerDoc);
            
            // Collect tag descriptions for file endpoints
            var documentTags = new Dictionary<string, string>();

            // Create paths for each file endpoint
            foreach (var (endpointName, endpoint) in fileEndpoints)
            {
                if (endpoint.IsPrivate)
                {
                    continue; // Skip private endpoints
                }
                
                // Use "Files" as the main tag for all file endpoints (file endpoints don't use namespaces)
                string mainTag = "Files";
                
                if (!documentTags.ContainsKey(mainTag))
                {
                    // Use consistent description for Files tag
                    documentTags[mainTag] = "**File Management**\n\nComprehensive file storage and retrieval system. Upload, download, list, and delete files across different storage categories with support for various file types and access controls.";
                }

                // Add file upload operation
                AddFileUploadOperation(swaggerDoc, endpointName, endpoint, allowedEnvironments, mainTag);
                
                // Add file download operation
                AddFileDownloadOperation(swaggerDoc, endpointName, endpoint, allowedEnvironments, mainTag);
                
                // Add file delete operation
                AddFileDeleteOperation(swaggerDoc, endpointName, endpoint, allowedEnvironments, mainTag);
                
                // Add file listing operation
                AddFileListOperation(swaggerDoc, endpointName, endpoint, allowedEnvironments, mainTag);
            }
            
            // Add collected file endpoint tags to the document
            AddFileTagsToDocument(swaggerDoc, documentTags);
            
            // Note: Tag sorting is now handled by TagSorterDocumentFilter which runs after this filter
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Swagger documentation for file endpoints");
        }
    }
    
    private void AddFileTagsToDocument(OpenApiDocument swaggerDoc, Dictionary<string, string> documentTags)
    {
        // Initialize tags collection if it doesn't exist
        swaggerDoc.Tags ??= new List<OpenApiTag>();
        
        // Add each file endpoint tag with its description (sorting will be handled by AlphabeticalEndpointSorter)
        foreach (var tagEntry in documentTags)
        {
            var existingTag = swaggerDoc.Tags.FirstOrDefault(t => t.Name.Equals(tagEntry.Key, StringComparison.OrdinalIgnoreCase));
            if (existingTag == null)
            {
                swaggerDoc.Tags.Add(new OpenApiTag
                {
                    Name = tagEntry.Key,
                    Description = tagEntry.Value
                });
            }
            else
            {
                // Update existing tag description if it's empty
                if (string.IsNullOrWhiteSpace(existingTag.Description))
                {
                    existingTag.Description = tagEntry.Value;
                }
            }
        }
    }

    private string GetOperationDescription(EndpointDefinition endpoint, string method, string defaultDescription)
    {
        // Check if there's a custom description for this method in the endpoint's Documentation
        if (endpoint.Documentation?.MethodDescriptions != null &&
            endpoint.Documentation.MethodDescriptions.TryGetValue(method, out var customDescription) &&
            !string.IsNullOrWhiteSpace(customDescription))
        {
            return customDescription;
        }
        
        return defaultDescription;
    }

    private void AddFileSchemas(OpenApiDocument swaggerDoc)
    {
        // Ensure components is initialized
        swaggerDoc.Components = swaggerDoc.Components ?? new OpenApiComponents();
        swaggerDoc.Components.Schemas = swaggerDoc.Components.Schemas ?? new Dictionary<string, OpenApiSchema>();

        // Add FileInfo schema
        swaggerDoc.Components.Schemas["FileInfo"] = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["fileId"] = new OpenApiSchema { Type = "string" },
                ["fileName"] = new OpenApiSchema { Type = "string" },
                ["contentType"] = new OpenApiSchema { Type = "string" },
                ["size"] = new OpenApiSchema { Type = "integer", Format = "int64" },
                ["lastModified"] = new OpenApiSchema { Type = "string", Format = "date-time" },
                ["environment"] = new OpenApiSchema { Type = "string" },
                ["isInMemoryOnly"] = new OpenApiSchema { Type = "boolean" }
            },
            Required = new HashSet<string> { "fileId", "fileName", "contentType" }
        };

        // Add FileUploadResponse schema
        swaggerDoc.Components.Schemas["FileUploadResponse"] = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["success"] = new OpenApiSchema { Type = "boolean" },
                ["fileId"] = new OpenApiSchema { Type = "string" },
                ["filename"] = new OpenApiSchema { Type = "string" },
                ["contentType"] = new OpenApiSchema { Type = "string" },
                ["size"] = new OpenApiSchema { Type = "integer", Format = "int64" },
                ["url"] = new OpenApiSchema { Type = "string" }
            },
            Required = new HashSet<string> { "success", "fileId", "filename" }
        };

        // Add FileListResponse schema
        swaggerDoc.Components.Schemas["FileListResponse"] = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["success"] = new OpenApiSchema { Type = "boolean" },
                ["files"] = new OpenApiSchema
                {
                    Type = "array",
                    Items = new OpenApiSchema { Reference = new OpenApiReference { Type = ReferenceType.Schema, Id = "FileInfo" } }
                },
                ["count"] = new OpenApiSchema { Type = "integer" }
            },
            Required = new HashSet<string> { "success", "files", "count" }
        };
    }

    private void AddFileUploadOperation(OpenApiDocument swaggerDoc, string endpointName, EndpointDefinition endpoint, List<string> allowedEnvironments, string tag)
    {
        // Path for upload: /api/{env}/files/{endpointName}
        string path = $"/api/{{env}}/files/{endpointName}";
        
        if (!swaggerDoc.Paths.ContainsKey(path))
        {
            swaggerDoc.Paths.Add(path, new OpenApiPathItem());
        }

        var operation = new OpenApiOperation
        {
            Tags = new List<OpenApiTag> { new OpenApiTag { Name = tag } },
            Summary = $"Upload file to {endpointName}",
            Description = GetOperationDescription(endpoint, "POST", $"Uploads a file to the {endpointName} storage endpoint"),
            OperationId = $"uploadFile_{endpointName}".Replace(" ", "_"),
            Parameters = new List<OpenApiParameter>()
        };

        // Environment parameter
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "env",
            In = ParameterLocation.Path,
            Required = true,
            Schema = new OpenApiSchema { 
                Type = "string", 
                Enum = allowedEnvironments.Select(e => new OpenApiString(e)).Cast<IOpenApiAny>().ToList() 
            },
            Description = $"Environment to target. Allowed values: {string.Join(", ", allowedEnvironments)}"
        });

        // Overwrite parameter
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "overwrite",
            In = ParameterLocation.Query,
            Required = false,
            Schema = new OpenApiSchema { Type = "boolean", Default = new OpenApiBoolean(false) },
            Description = "Set to true to overwrite existing files with the same name"
        });

        // Add Accept header for consistency with other endpoints
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Accept",
            In = ParameterLocation.Header,
            Required = false,
            Schema = new OpenApiSchema
            {
                Type = "string",
                Default = new OpenApiString("application/json")
            },
            Description = "Specifies the media type of the response (default is application/json)"
        });

        // File parameter
        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["multipart/form-data"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = "object",
                        Properties = new Dictionary<string, OpenApiSchema>
                        {
                            ["file"] = new OpenApiSchema
                            {
                                Type = "string",
                                Format = "binary",
                                Description = "The file to upload"
                            }
                        },
                        Required = new HashSet<string> { "file" }
                    }
                }
            }
        };

        // Success response
        operation.Responses = new OpenApiResponses
        {
            ["200"] = new OpenApiResponse
            {
                Description = "Successful response",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["success"] = new OpenApiSchema { Type = "boolean" },
                                ["fileId"] = new OpenApiSchema { Type = "string" },
                                ["filename"] = new OpenApiSchema { Type = "string" },
                                ["contentType"] = new OpenApiSchema { Type = "string" },
                                ["size"] = new OpenApiSchema { Type = "integer", Format = "int64" },
                                ["url"] = new OpenApiSchema { Type = "string" }
                            }
                        }
                    }
                }
            },
            ["400"] = new OpenApiResponse { Description = "Bad request - invalid file or request" },
            ["401"] = new OpenApiResponse { Description = "Unauthorized" },
            ["403"] = new OpenApiResponse { Description = "Forbidden - file type not allowed" },
            ["413"] = new OpenApiResponse { Description = "Payload Too Large - file exceeds size limit" }
        };

        // Add file endpoint properties info
        AddFileEndpointPropertiesInfo(operation, endpoint, "upload");
        
        // Add the upload operation
        swaggerDoc.Paths[path].Operations[OperationType.Post] = operation;
    }

    private void AddFileDownloadOperation(OpenApiDocument swaggerDoc, string endpointName, EndpointDefinition endpoint, List<string> allowedEnvironments, string tag)
    {
        // Path for download: /api/{env}/files/{endpointName}/{fileId}
        string path = $"/api/{{env}}/files/{endpointName}/{{fileId}}";
        
        if (!swaggerDoc.Paths.ContainsKey(path))
        {
            swaggerDoc.Paths.Add(path, new OpenApiPathItem());
        }

        var operation = new OpenApiOperation
        {
            Tags = new List<OpenApiTag> { new OpenApiTag { Name = tag } },
            Summary = $"Download file from {endpointName}",
            Description = GetOperationDescription(endpoint, "GET", $"Downloads a file from the {endpointName} storage endpoint"),
            OperationId = $"downloadFile_{endpointName}".Replace(" ", "_"),
            Parameters = new List<OpenApiParameter>()
        };

        // Environment parameter
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "env",
            In = ParameterLocation.Path,
            Required = true,
            Schema = new OpenApiSchema { 
                Type = "string", 
                Enum = allowedEnvironments.Select(e => new OpenApiString(e)).Cast<IOpenApiAny>().ToList() 
            },
            Description = $"Environment to target. Allowed values: {string.Join(", ", allowedEnvironments)}"
        });

        // File ID parameter
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "fileId",
            In = ParameterLocation.Path,
            Required = true,
            Schema = new OpenApiSchema { Type = "string" },
            Description = "ID of the file to download"
        });

        // Add Accept header for consistency with other endpoints
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Accept",
            In = ParameterLocation.Header,
            Required = false,
            Schema = new OpenApiSchema
            {
                Type = "string",
                Default = new OpenApiString("*/*")
            },
            Description = "Specifies the media type of the response"
        });

        // Success response
        operation.Responses = new OpenApiResponses
        {
            ["200"] = new OpenApiResponse
            {
                Description = "Successful response",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["*/*"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "string",
                            Format = "binary"
                        }
                    }
                }
            },
            ["400"] = new OpenApiResponse { Description = "Bad request - invalid file ID" },
            ["401"] = new OpenApiResponse { Description = "Unauthorized" },
            ["404"] = new OpenApiResponse { Description = "File not found" }
        };

        // Add file endpoint properties info
        AddFileEndpointPropertiesInfo(operation, endpoint, "download");
        
        // Add the download operation
        swaggerDoc.Paths[path].Operations[OperationType.Get] = operation;
    }

    private void AddFileDeleteOperation(OpenApiDocument swaggerDoc, string endpointName, EndpointDefinition endpoint, List<string> allowedEnvironments, string tag)
    {
        // Path for delete: /api/{env}/files/{endpointName}/{fileId}
        string path = $"/api/{{env}}/files/{endpointName}/{{fileId}}";
        
        if (!swaggerDoc.Paths.ContainsKey(path))
        {
            swaggerDoc.Paths.Add(path, new OpenApiPathItem());
        }

        var operation = new OpenApiOperation
        {
            Tags = new List<OpenApiTag> { new OpenApiTag { Name = tag } },
            Summary = $"Delete file from {endpointName}",
            Description = GetOperationDescription(endpoint, "DELETE", $"Deletes a file from the {endpointName} storage endpoint"),
            OperationId = $"deleteFile_{endpointName}".Replace(" ", "_"),
            Parameters = new List<OpenApiParameter>()
        };

        // Environment parameter
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "env",
            In = ParameterLocation.Path,
            Required = true,
            Schema = new OpenApiSchema { 
                Type = "string", 
                Enum = allowedEnvironments.Select(e => new OpenApiString(e)).Cast<IOpenApiAny>().ToList() 
            },
            Description = $"Environment to target. Allowed values: {string.Join(", ", allowedEnvironments)}"
        });

        // File ID parameter
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "fileId",
            In = ParameterLocation.Path,
            Required = true,
            Schema = new OpenApiSchema { Type = "string" },
            Description = "ID of the file to delete"
        });

        // Add Accept header for consistency with other endpoints
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Accept",
            In = ParameterLocation.Header,
            Required = false,
            Schema = new OpenApiSchema
            {
                Type = "string",
                Default = new OpenApiString("application/json")
            },
            Description = "Specifies the media type of the response (default is application/json)"
        });

        // Success response
        operation.Responses = new OpenApiResponses
        {
            ["200"] = new OpenApiResponse
            {
                Description = "Successful response",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["success"] = new OpenApiSchema { Type = "boolean" },
                                ["message"] = new OpenApiSchema { Type = "string" }
                            }
                        }
                    }
                }
            },
            ["400"] = new OpenApiResponse { Description = "Bad request - invalid file ID" },
            ["401"] = new OpenApiResponse { Description = "Unauthorized" },
            ["404"] = new OpenApiResponse { Description = "File not found" }
        };

        // Add examples
        AddExamples(operation, "delete", endpointName);
        
        // Add file endpoint properties info
        AddFileEndpointPropertiesInfo(operation, endpoint, "delete");
        
        // Add the delete operation
        swaggerDoc.Paths[path].Operations[OperationType.Delete] = operation;
    }

    private void AddFileListOperation(OpenApiDocument swaggerDoc, string endpointName, EndpointDefinition endpoint, List<string> allowedEnvironments, string tag)
    {
        // Path for listing: /api/{env}/files/{endpointName}/list
        string path = $"/api/{{env}}/files/{endpointName}/list";
        
        if (!swaggerDoc.Paths.ContainsKey(path))
        {
            swaggerDoc.Paths.Add(path, new OpenApiPathItem());
        }

        var operation = new OpenApiOperation
        {
            Tags = new List<OpenApiTag> { new OpenApiTag { Name = tag } },
            Summary = $"List files in {endpointName}",
            Description = GetOperationDescription(endpoint, "LIST", $"Lists all files in the {endpointName} storage endpoint"),
            OperationId = $"listFiles_{endpointName}".Replace(" ", "_"),
            Parameters = new List<OpenApiParameter>()
        };

        // Environment parameter
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "env",
            In = ParameterLocation.Path,
            Required = true,
            Schema = new OpenApiSchema { 
                Type = "string", 
                Enum = allowedEnvironments.Select(e => new OpenApiString(e)).Cast<IOpenApiAny>().ToList() 
            },
            Description = $"Environment to target. Allowed values: {string.Join(", ", allowedEnvironments)}"
        });

        // Prefix parameter
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "prefix",
            In = ParameterLocation.Query,
            Required = false,
            Schema = new OpenApiSchema { Type = "string" },
            Description = "Filter files by prefix"
        });

        // Add Accept header for consistency with other endpoints
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Accept",
            In = ParameterLocation.Header,
            Required = false,
            Schema = new OpenApiSchema
            {
                Type = "string",
                Default = new OpenApiString("application/json")
            },
            Description = "Specifies the media type of the response (default is application/json)"
        });

        // Success response
        operation.Responses = new OpenApiResponses
        {
            ["200"] = new OpenApiResponse
            {
                Description = "Successful response",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["success"] = new OpenApiSchema { Type = "boolean" },
                                ["files"] = new OpenApiSchema
                                {
                                    Type = "array",
                                    Items = new OpenApiSchema
                                    {
                                        Type = "object",
                                        Properties = new Dictionary<string, OpenApiSchema>
                                        {
                                            ["fileId"] = new OpenApiSchema { Type = "string" },
                                            ["fileName"] = new OpenApiSchema { Type = "string" },
                                            ["contentType"] = new OpenApiSchema { Type = "string" },
                                            ["size"] = new OpenApiSchema { Type = "integer", Format = "int64" },
                                            ["lastModified"] = new OpenApiSchema { Type = "string", Format = "date-time" },
                                            ["url"] = new OpenApiSchema { Type = "string" }
                                        }
                                    }
                                },
                                ["count"] = new OpenApiSchema { Type = "integer" }
                            }
                        }
                    }
                }
            },
            ["401"] = new OpenApiResponse { Description = "Unauthorized" },
            ["404"] = new OpenApiResponse { Description = "Endpoint not found" }
        };

        // Add examples
        AddExamples(operation, "list", endpointName);
        
        // Add file endpoint properties info
        AddFileEndpointPropertiesInfo(operation, endpoint, "list");
        
        // Add the list operation
        swaggerDoc.Paths[path].Operations[OperationType.Get] = operation;
    }

    private void AddExamples(OpenApiOperation operation, string operationType, string endpointName)
    {
        if (operationType == "upload")
        {
            // No examples for multipart/form-data uploads
        }
        else if (operationType == "download")
        {
            // No examples for binary downloads
        }
        else if (operationType == "delete")
        {
            if (operation.Responses["200"].Content.ContainsKey("application/json"))
            {
                operation.Responses["200"].Content["application/json"].Examples = new Dictionary<string, OpenApiExample>
                {
                    ["success"] = new OpenApiExample
                    {
                        Value = new OpenApiObject
                        {
                            ["success"] = new OpenApiBoolean(true),
                            ["message"] = new OpenApiString("File deleted successfully")
                        },
                        Summary = "Successful deletion"
                    }
                };
            }
        }
        else if (operationType == "list")
        {
            if (operation.Responses["200"].Content.ContainsKey("application/json"))
            {
                operation.Responses["200"].Content["application/json"].Examples = new Dictionary<string, OpenApiExample>
                {
                    ["fileList"] = new OpenApiExample
                    {
                        Value = new OpenApiObject
                        {
                            ["success"] = new OpenApiBoolean(true),
                            ["files"] = new OpenApiArray
                            {
                                new OpenApiObject
                                {
                                    ["fileId"] = new OpenApiString("YTAwOmV4YW1wbGUucGRm"),
                                    ["fileName"] = new OpenApiString("example.pdf"),
                                    ["contentType"] = new OpenApiString("application/pdf"),
                                    ["size"] = new OpenApiInteger(12345),
                                    ["lastModified"] = new OpenApiString("2025-05-20T10:15:30Z"),
                                    ["url"] = new OpenApiString($"/api/prod/files/{endpointName}/YTAwOmV4YW1wbGUucGRm")
                                },
                                new OpenApiObject
                                {
                                    ["fileId"] = new OpenApiString("YTAwOmltYWdlLmpwZw"),
                                    ["fileName"] = new OpenApiString("image.jpg"),
                                    ["contentType"] = new OpenApiString("image/jpeg"),
                                    ["size"] = new OpenApiInteger(54321),
                                    ["lastModified"] = new OpenApiString("2025-05-19T14:30:45Z"),
                                    ["url"] = new OpenApiString($"/api/prod/files/{endpointName}/YTAwOmltYWdlLmpwZw")
                                }
                            },
                            ["count"] = new OpenApiInteger(2)
                        },
                        Summary = "File listing example"
                    }
                };
            }
        }
    }

    private void AddFileEndpointPropertiesInfo(OpenApiOperation operation, EndpointDefinition endpoint, string operationType)
    {
        // Add description about the file endpoint's restrictions
        var description = new StringBuilder(operation.Description ?? "");
                
        // Add allowed extensions info if exists
        if (endpoint.Properties != null && endpoint.Properties.TryGetValue("AllowedExtensions", out var extensions) && 
            extensions is List<string> allowedExtensions && 
            allowedExtensions.Count > 0)
        {
            description.AppendLine($"\n\nAllowed file extensions: {string.Join(", ", allowedExtensions)}");
        }
        
        // Update operation description
        operation.Description = description.ToString();
    }
    private List<string> GetAllowedEnvironments()
    {
        try
        {
            var settingsFile = Path.Combine(Directory.GetCurrentDirectory(), "environments", "settings.json");
            if (File.Exists(settingsFile))
            {
                var settingsJson = File.ReadAllText(settingsFile);
                
                // Match the structure used in EnvironmentSettings class
                var settings = JsonSerializer.Deserialize<SettingsModel>(settingsJson);
                if (settings?.Environment?.AllowedEnvironments != null && 
                    settings.Environment.AllowedEnvironments.Any())
                {
                    return settings.Environment.AllowedEnvironments;
                }
            }
            
            // Return default if settings not found
            return new List<string> { "prod", "dev" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading environment settings");
            return new List<string> { "prod", "dev" };
        }
    }

    // Helper classes for deserializing settings.json
    private class SettingsModel
    {
        public EnvironmentModel Environment { get; set; } = new EnvironmentModel();
    }

    private class EnvironmentModel
    {
        public string ServerName { get; set; } = ".";
        public List<string> AllowedEnvironments { get; set; } = new List<string>();
    }
}
