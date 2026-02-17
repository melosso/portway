using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using PortwayApi.Classes;

namespace PortwayApi.Classes.OpenApi;

public class FileEndpointDocumentFilter : IOpenApiDocumentTransformer
{
    private readonly ILogger<FileEndpointDocumentFilter> _logger;

    public FileEndpointDocumentFilter(ILogger<FileEndpointDocumentFilter> logger)
    {
        _logger = logger;
    }

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        try
        {
            // Load file endpoint definitions
            var fileEndpoints = EndpointHandler.GetFileEndpoints();

            // Get allowed environments for parameter description
            var allowedEnvironments = GetAllowedEnvironments();

            // Add schema definitions for file models
            AddFileSchemas(document);

            // Collect tag descriptions for file endpoints
            var documentTags = new Dictionary<string, string>();

            // Create paths for each file endpoint
            foreach (var (endpointName, endpoint) in fileEndpoints)
            {
                if (endpoint.IsPrivate)
                {
                    // Skip private endpoints
                    continue;
                }

                // Use "Files" as the main tag for all file endpoints (file endpoints don't use namespaces)
                string mainTag = "Files";

                if (!documentTags.ContainsKey(mainTag))
                {
                    // Use consistent description for Files tag
                    documentTags[mainTag] = "**File Management**\n\nComprehensive file storage and retrieval system. Upload, download, list, and delete files across different storage categories with support for various file types and access controls.";
                }

                // Add file upload operation
                AddFileUploadOperation(document, endpointName, endpoint, allowedEnvironments, mainTag);

                // Add file download operation
                AddFileDownloadOperation(document, endpointName, endpoint, allowedEnvironments, mainTag);

                // Add file delete operation
                AddFileDeleteOperation(document, endpointName, endpoint, allowedEnvironments, mainTag);

                // Add file listing operation
                AddFileListOperation(document, endpointName, endpoint, allowedEnvironments, mainTag);
            }

            // Add collected file endpoint tags to the document
            AddFileTagsToDocument(document, documentTags);

            // Note: Tag sorting is now handled by TagSorterDocumentFilter which runs after this filter
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating OpenAPI documentation for file endpoints");
        }

        return Task.CompletedTask;
    }

    private void AddFileTagsToDocument(OpenApiDocument document, Dictionary<string, string> documentTags)
    {
        // Initialize tags collection if it doesn't exist
        document.Tags ??= new HashSet<OpenApiTag>();

        // Add each file endpoint tag with its description (sorting will be handled by AlphabeticalEndpointSorter)
        foreach (var tagEntry in documentTags)
        {
            var existingTag = document.Tags.FirstOrDefault(t => string.Equals(t.Name, tagEntry.Key, StringComparison.OrdinalIgnoreCase));
            if (existingTag == null)
            {
                document.Tags.Add(new OpenApiTag
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

    private void AddFileSchemas(OpenApiDocument document)
    {
        // Ensure components is initialized
        document.Components = document.Components ?? new OpenApiComponents();
        document.Components.Schemas = document.Components.Schemas ?? new Dictionary<string, IOpenApiSchema>();

        // Add FileInfo schema
        document.Components.Schemas["FileInfo"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["fileId"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["fileName"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["contentType"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["size"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int64" },
                ["lastModified"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time" },
                ["environment"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["isInMemoryOnly"] = new OpenApiSchema { Type = JsonSchemaType.Boolean }
            },
            Required = new HashSet<string> { "fileId", "fileName", "contentType" }
        };

        // Add FileUploadResponse schema
        document.Components.Schemas["FileUploadResponse"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
                ["fileId"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["filename"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["contentType"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["size"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int64" },
                ["url"] = new OpenApiSchema { Type = JsonSchemaType.String }
            },
            Required = new HashSet<string> { "success", "fileId", "filename" }
        };

        // Add FileListResponse schema
        document.Components.Schemas["FileListResponse"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["Success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
                ["Count"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
                ["Value"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Items = new OpenApiSchemaReference("FileInfo")
                }
            },
            Required = new HashSet<string> { "Success", "Count", "Value" }
        };
    }

    private void AddFileUploadOperation(OpenApiDocument document, string endpointName, EndpointDefinition endpoint, List<string> allowedEnvironments, string tag)
    {
        // Path for upload: /api/{env}/files/{endpointName}
        string path = $"/api/{{env}}/files/{endpointName}";

        if (!document.Paths.ContainsKey(path))
        {
            document.Paths.Add(path, new OpenApiPathItem { Operations = new Dictionary<HttpMethod, OpenApiOperation>() });
        }

        var operation = new OpenApiOperation
        {
            Tags = new HashSet<OpenApiTagReference> { new OpenApiTagReference(tag) },
            Summary = $"Upload file to {endpointName}",
            Description = GetOperationDescription(endpoint, "POST", $"Uploads a file to the {endpointName} storage endpoint"),
            OperationId = $"uploadFile_{endpointName}".Replace(" ", "_"),
            Parameters = new List<IOpenApiParameter>()
        };

        // Environment parameter
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "env",
            In = ParameterLocation.Path,
            Required = true,
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = allowedEnvironments.Select(e => (JsonNode)JsonValue.Create(e)!).ToList()
            },
            Description = $"Environment to target. Allowed values: {string.Join(", ", allowedEnvironments)}"
        });

        // Overwrite parameter
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "overwrite",
            In = ParameterLocation.Query,
            Required = false,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Boolean, Default = JsonValue.Create(false) },
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
                Type = JsonSchemaType.String,
                Default = JsonValue.Create("application/json")
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
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["file"] = new OpenApiSchema
                            {
                                Type = JsonSchemaType.String,
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
            ["201"] = new OpenApiResponse
            {
                Description = "Created - File successfully uploaded",
                Headers = new Dictionary<string, IOpenApiHeader>
                {
                    ["Location"] = new OpenApiHeader
                    {
                        Description = "URL of the newly uploaded file",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                    }
                },
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            Properties = new Dictionary<string, IOpenApiSchema>
                            {
                                ["success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
                                ["fileId"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                ["filename"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                ["contentType"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                ["size"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int64" },
                                ["url"] = new OpenApiSchema { Type = JsonSchemaType.String }
                            }
                        }
                    }
                }
            },
            ["400"] = new OpenApiResponse { Description = "Bad request - invalid file or request" },
            ["401"] = new OpenApiResponse { Description = "Unauthorized" },
            ["403"] = new OpenApiResponse { Description = "Forbidden - file type not allowed" },
            ["409"] = new OpenApiResponse { Description = "Conflict - file already exists (when overwrite is false)" },
            ["413"] = new OpenApiResponse { Description = "Payload Too Large - file exceeds size limit" },
            ["500"] = new OpenApiResponse
            {
                Description = "Internal Server Error",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            Properties = new Dictionary<string, IOpenApiSchema>
                            {
                                ["success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
                                ["error"] = new OpenApiSchema { Type = JsonSchemaType.String }
                            }
                        },
                        Example = new JsonObject
                        {
                            ["success"] = false,
                            ["error"] = "An error occurred while uploading the file"
                        }
                    }
                }
            }
        };

        // Add file endpoint properties info
        AddFileEndpointPropertiesInfo(operation, endpoint, "upload");

        // Add the upload operation
        document.Paths[path].Operations![HttpMethod.Post] = operation;
    }

    private void AddFileDownloadOperation(OpenApiDocument document, string endpointName, EndpointDefinition endpoint, List<string> allowedEnvironments, string tag)
    {
        // Path for download: /api/{env}/files/{endpointName}/{fileId}
        string path = $"/api/{{env}}/files/{endpointName}/{{fileId}}";

        if (!document.Paths.ContainsKey(path))
        {
            document.Paths.Add(path, new OpenApiPathItem { Operations = new Dictionary<HttpMethod, OpenApiOperation>() });
        }

        var operation = new OpenApiOperation
        {
            Tags = new HashSet<OpenApiTagReference> { new OpenApiTagReference(tag) },
            Summary = $"Download file from {endpointName}",
            Description = GetOperationDescription(endpoint, "GET", $"Downloads a file from the {endpointName} storage endpoint"),
            OperationId = $"downloadFile_{endpointName}".Replace(" ", "_"),
            Parameters = new List<IOpenApiParameter>()
        };

        // Environment parameter
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "env",
            In = ParameterLocation.Path,
            Required = true,
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = allowedEnvironments.Select(e => (JsonNode)JsonValue.Create(e)!).ToList()
            },
            Description = $"Environment to target. Allowed values: {string.Join(", ", allowedEnvironments)}"
        });

        // File ID parameter
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "fileId",
            In = ParameterLocation.Path,
            Required = true,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
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
                Type = JsonSchemaType.String,
                Default = JsonValue.Create("*/*")
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
                            Type = JsonSchemaType.String,
                            Format = "binary"
                        }
                    }
                }
            },
            ["400"] = new OpenApiResponse { Description = "Bad request - invalid file ID" },
            ["401"] = new OpenApiResponse { Description = "Unauthorized" },
            ["404"] = new OpenApiResponse { Description = "File not found" },
            ["500"] = new OpenApiResponse
            {
                Description = "Internal Server Error",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            Properties = new Dictionary<string, IOpenApiSchema>
                            {
                                ["success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
                                ["error"] = new OpenApiSchema { Type = JsonSchemaType.String }
                            }
                        },
                        Example = new JsonObject
                        {
                            ["success"] = false,
                            ["error"] = "An error occurred while downloading the file"
                        }
                    }
                }
            }
        };

        // Add file endpoint properties info
        AddFileEndpointPropertiesInfo(operation, endpoint, "download");

        // Add the download operation
        document.Paths[path].Operations![HttpMethod.Get] = operation;
    }

    private void AddFileDeleteOperation(OpenApiDocument document, string endpointName, EndpointDefinition endpoint, List<string> allowedEnvironments, string tag)
    {
        // Path for delete: /api/{env}/files/{endpointName}/{fileId}
        string path = $"/api/{{env}}/files/{endpointName}/{{fileId}}";

        if (!document.Paths.ContainsKey(path))
        {
            document.Paths.Add(path, new OpenApiPathItem { Operations = new Dictionary<HttpMethod, OpenApiOperation>() });
        }

        var operation = new OpenApiOperation
        {
            Tags = new HashSet<OpenApiTagReference> { new OpenApiTagReference(tag) },
            Summary = $"Delete file from {endpointName}",
            Description = GetOperationDescription(endpoint, "DELETE", $"Deletes a file from the {endpointName} storage endpoint"),
            OperationId = $"deleteFile_{endpointName}".Replace(" ", "_"),
            Parameters = new List<IOpenApiParameter>()
        };

        // Environment parameter
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "env",
            In = ParameterLocation.Path,
            Required = true,
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = allowedEnvironments.Select(e => (JsonNode)JsonValue.Create(e)!).ToList()
            },
            Description = $"Environment to target. Allowed values: {string.Join(", ", allowedEnvironments)}"
        });

        // File ID parameter
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "fileId",
            In = ParameterLocation.Path,
            Required = true,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
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
                Type = JsonSchemaType.String,
                Default = JsonValue.Create("application/json")
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
                            Type = JsonSchemaType.Object,
                            Properties = new Dictionary<string, IOpenApiSchema>
                            {
                                ["success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
                                ["message"] = new OpenApiSchema { Type = JsonSchemaType.String }
                            }
                        }
                    }
                }
            },
            ["400"] = new OpenApiResponse { Description = "Bad request - invalid file ID" },
            ["401"] = new OpenApiResponse { Description = "Unauthorized" },
            ["404"] = new OpenApiResponse { Description = "File not found" },
            ["500"] = new OpenApiResponse
            {
                Description = "Internal Server Error",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            Properties = new Dictionary<string, IOpenApiSchema>
                            {
                                ["success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
                                ["error"] = new OpenApiSchema { Type = JsonSchemaType.String }
                            }
                        },
                        Example = new JsonObject
                        {
                            ["success"] = false,
                            ["error"] = "An error occurred while deleting the file"
                        }
                    }
                }
            }
        };

        // Add examples
        AddExamples(operation, "delete", endpointName);

        // Add file endpoint properties info
        AddFileEndpointPropertiesInfo(operation, endpoint, "delete");

        // Add the delete operation
        document.Paths[path].Operations![HttpMethod.Delete] = operation;
    }

    private void AddFileListOperation(OpenApiDocument document, string endpointName, EndpointDefinition endpoint, List<string> allowedEnvironments, string tag)
    {
        // Path for listing: /api/{env}/files/{endpointName}/list
        string path = $"/api/{{env}}/files/{endpointName}/list";

        if (!document.Paths.ContainsKey(path))
        {
            document.Paths.Add(path, new OpenApiPathItem { Operations = new Dictionary<HttpMethod, OpenApiOperation>() });
        }

        var operation = new OpenApiOperation
        {
            Tags = new HashSet<OpenApiTagReference> { new OpenApiTagReference(tag) },
            Summary = $"List files in {endpointName}",
            Description = GetOperationDescription(endpoint, "LIST", $"Lists all files in the {endpointName} storage endpoint"),
            OperationId = $"listFiles_{endpointName}".Replace(" ", "_"),
            Parameters = new List<IOpenApiParameter>()
        };

        // Environment parameter
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "env",
            In = ParameterLocation.Path,
            Required = true,
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = allowedEnvironments.Select(e => (JsonNode)JsonValue.Create(e)!).ToList()
            },
            Description = $"Environment to target. Allowed values: {string.Join(", ", allowedEnvironments)}"
        });

        // Prefix parameter
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "prefix",
            In = ParameterLocation.Query,
            Required = false,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
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
                Type = JsonSchemaType.String,
                Default = JsonValue.Create("application/json")
            },
            Description = "Specifies the media type of the response (default is application/json)"
        });

        // Success response
        operation.Responses = new OpenApiResponses
        {
            ["200"] = new OpenApiResponse
            {
                Description = "Successful response",
                Headers = new Dictionary<string, IOpenApiHeader>
                {
                    ["X-Total-Count"] = new OpenApiHeader
                    {
                        Description = "Total number of files available (when $count=true)",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.Integer }
                    },
                    ["X-Returned-Count"] = new OpenApiHeader
                    {
                        Description = "Number of files returned in this response",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.Integer }
                    },
                    ["X-Has-More-Results"] = new OpenApiHeader
                    {
                        Description = "Indicates if more files are available (true/false)",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.Boolean }
                    }
                },
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            Properties = new Dictionary<string, IOpenApiSchema>
                            {
                                ["Success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
                                ["Count"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
                                ["Value"] = new OpenApiSchema
                                {
                                    Type = JsonSchemaType.Array,
                                    Items = new OpenApiSchema
                                    {
                                        Type = JsonSchemaType.Object,
                                        Properties = new Dictionary<string, IOpenApiSchema>
                                        {
                                            ["fileId"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                            ["fileName"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                            ["contentType"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                            ["size"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int64" },
                                            ["lastModified"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time" },
                                            ["url"] = new OpenApiSchema { Type = JsonSchemaType.String }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            },
            ["401"] = new OpenApiResponse { Description = "Unauthorized" },
            ["404"] = new OpenApiResponse { Description = "Endpoint not found" },
            ["500"] = new OpenApiResponse
            {
                Description = "Internal Server Error",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            Properties = new Dictionary<string, IOpenApiSchema>
                            {
                                ["success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
                                ["error"] = new OpenApiSchema { Type = JsonSchemaType.String }
                            }
                        },
                        Example = new JsonObject
                        {
                            ["success"] = false,
                            ["error"] = "An error occurred while listing files"
                        }
                    }
                }
            }
        };

        // Add examples
        AddExamples(operation, "list", endpointName);

        // Add file endpoint properties info
        AddFileEndpointPropertiesInfo(operation, endpoint, "list");

        // Add the list operation
        document.Paths[path].Operations![HttpMethod.Get] = operation;
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
            if (operation.Responses?["200"]?.Content?.ContainsKey("application/json") == true)
            {
                operation.Responses["200"].Content!["application/json"].Examples = new Dictionary<string, IOpenApiExample>
                {
                    ["success"] = new OpenApiExample
                    {
                        Value = new JsonObject
                        {
                            ["success"] = true,
                            ["message"] = "File deleted successfully"
                        },
                        Summary = "Successful deletion"
                    }
                };
            }
        }
        else if (operationType == "list")
        {
            if (operation.Responses?["200"]?.Content?.ContainsKey("application/json") == true)
            {
                operation.Responses["200"].Content!["application/json"].Examples = new Dictionary<string, IOpenApiExample>
                {
                    ["fileList"] = new OpenApiExample
                    {
                        Value = new JsonObject
                        {
                            ["success"] = true,
                            ["files"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["fileId"] = "YTAwOmV4YW1wbGUucGRm",
                                    ["fileName"] = "example.pdf",
                                    ["contentType"] = "application/pdf",
                                    ["size"] = 12345,
                                    ["lastModified"] = "2025-05-20T10:15:30Z",
                                    ["url"] = $"/api/prod/files/{endpointName}/YTAwOmV4YW1wbGUucGRm"
                                },
                                new JsonObject
                                {
                                    ["fileId"] = "YTAwOmltYWdlLmpwZw",
                                    ["fileName"] = "image.jpg",
                                    ["contentType"] = "image/jpeg",
                                    ["size"] = 54321,
                                    ["lastModified"] = "2025-05-19T14:30:45Z",
                                    ["url"] = $"/api/prod/files/{endpointName}/YTAwOmltYWdlLmpwZw"
                                }
                            },
                            ["count"] = 2
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
