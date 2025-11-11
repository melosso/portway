using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Serilog;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PortwayApi.Classes.Swagger;

/// <summary>
/// Swagger document filter that enriches SQL endpoint documentation with database column metadata
/// </summary>
public class SqlMetadataDocumentFilter : IDocumentFilter
{
    private readonly Services.SqlMetadataService _metadataService;

    public SqlMetadataDocumentFilter(Services.SqlMetadataService metadataService)
    {
        _metadataService = metadataService;
    }

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        try
        {
            EnrichSqlEndpointsWithMetadata(swaggerDoc);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error applying SQL metadata to OpenAPI documentation");
        }
    }

    /// <summary>
    /// Enriches SQL endpoints in the Swagger document with column metadata
    /// </summary>
    private void EnrichSqlEndpointsWithMetadata(OpenApiDocument swaggerDoc)
    {
        var sqlEndpoints = EndpointHandler.GetSqlEndpoints();

        foreach (var endpoint in sqlEndpoints)
        {
            var endpointName = endpoint.Key;
            var definition = endpoint.Value;

            // Skip private endpoints
            if (definition.IsPrivate)
                continue;

            // Find the path in swagger doc
            var path = $"/api/{{env}}/{endpointName}";
            if (!swaggerDoc.Paths.ContainsKey(path))
            {
                Log.Debug("Path {Path} not found in Swagger document", path);
                continue;
            }

            var pathItem = swaggerDoc.Paths[path];

            // Enrich GET operation with object metadata
            if (pathItem.Operations.TryGetValue(OperationType.Get, out var getOperation))
            {
                EnrichGetOperationWithObjectMetadata(getOperation, endpointName, definition);
            }

            // Enrich POST operation with procedure metadata
            if (pathItem.Operations.TryGetValue(OperationType.Post, out var postOperation))
            {
                EnrichModificationOperationWithProcedureMetadata(postOperation, endpointName, definition, "POST");
            }

            // Enrich PUT operation with procedure metadata
            if (pathItem.Operations.TryGetValue(OperationType.Put, out var putOperation))
            {
                EnrichModificationOperationWithProcedureMetadata(putOperation, endpointName, definition, "PUT");
            }

            // Enrich PATCH operation with procedure metadata
            if (pathItem.Operations.TryGetValue(OperationType.Patch, out var patchOperation))
            {
                EnrichModificationOperationWithProcedureMetadata(patchOperation, endpointName, definition, "PATCH");
            }

            // Enrich DELETE operation with object metadata (for primary key info)
            if (pathItem.Operations.TryGetValue(OperationType.Delete, out var deleteOperation))
            {
                EnrichDeleteOperationWithObjectMetadata(deleteOperation, endpointName, definition);
            }

            Log.Debug("Enriched OpenAPI documentation for endpoint {EndpointName}", endpointName);
        }
    }

    /// <summary>
    /// Enriches GET operation with object metadata (table/view columns)
    /// </summary>
    private void EnrichGetOperationWithObjectMetadata(
        OpenApiOperation operation,
        string endpointName,
        EndpointDefinition definition)
    {
        var metadata = _metadataService.GetObjectMetadata(endpointName);
        if (metadata == null || !metadata.Any())
        {
            Log.Debug("No object metadata available for GET endpoint {EndpointName}", endpointName);
            return;
        }

        var responseSchema = CreateSchemaFromObjectMetadata(metadata, excludePrimaryKey: false, endpoint: definition);

        // Update response to include OData-compliant schema
        if (operation.Responses.TryGetValue("200", out var response))
        {
            var odataSchema = new OpenApiSchema
            {
                Type = "object",
                Properties = new Dictionary<string, OpenApiSchema>
                {
                    ["count"] = new OpenApiSchema
                    {
                        Type = "integer",
                        Description = "Total number of records returned",
                        Example = new OpenApiInteger(5)
                    },
                    ["value"] = new OpenApiSchema
                    {
                        Type = "array",
                        Items = responseSchema,
                        Description = "Array of records"
                    },
                    ["nextLink"] = new OpenApiSchema
                    {
                        Type = "string",
                        Nullable = true,
                        Description = "URL for pagination (null if no more pages)",
                        Example = new OpenApiNull()
                    }
                },
                Required = new HashSet<string> { "count", "value" }
            };

            response.Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType
                {
                    Schema = odataSchema
                }
            };
        }

        Log.Debug("Enriched GET operation for {EndpointName} with {ColumnCount} columns", 
            endpointName, metadata.Count);
    }

    /// <summary>
    /// Enriches modification operations (POST, PUT, PATCH) with procedure metadata
    /// </summary>
    private void EnrichModificationOperationWithProcedureMetadata(
        OpenApiOperation operation,
        string endpointName,
        EndpointDefinition definition,
        string method)
    {
        var parameters = _metadataService.GetProcedureMetadata(endpointName);
        if (parameters == null || !parameters.Any())
        {
            Log.Debug("No procedure metadata available for {Method} endpoint {EndpointName}", 
                method, endpointName);
            return;
        }

        var requestSchema = CreateSchemaFromProcedureMetadata(parameters, definition, method);
        
        // For POST response, we should return the created record (not wrapped)
        // For PUT/PATCH, return success message with affected record count
        var objectMetadata = _metadataService.GetObjectMetadata(endpointName);
        OpenApiSchema responseSchema;
        
        if (method == "POST")
        {
            // POST should return the created record directly (not wrapped)
            responseSchema = objectMetadata != null && objectMetadata.Any() 
                ? CreateSchemaFromObjectMetadata(objectMetadata, excludePrimaryKey: false, endpoint: definition)
                : new OpenApiSchema { Type = "object" };
        }
        else
        {
            // PUT/PATCH return success response
            responseSchema = new OpenApiSchema
            {
                Type = "object",
                Properties = new Dictionary<string, OpenApiSchema>
                {
                    ["success"] = new OpenApiSchema { Type = "boolean" },
                    ["message"] = new OpenApiSchema { Type = "string" },
                    ["result"] = new OpenApiSchema
                    {
                        Type = "array",
                        Items = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["Id"] = new OpenApiSchema { Type = "string", Example = new OpenApiString("12345") }
                            }
                        }
                    }
                }
            };
        }

        // Set request body schema
        operation.RequestBody = new OpenApiRequestBody
        {
            Description = GetRequestBodyDescription(method, definition),
            Required = true,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType
                {
                    Schema = requestSchema,
                    Example = CreateExampleObjectFromProcedure(parameters, method, definition)
                }
            }
        };

        // Update response schema
        var successStatus = method == "POST" ? "201" : "200";
        if (operation.Responses.TryGetValue(successStatus, out var response))
        {
            response.Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType
                {
                    Schema = responseSchema,
                    Example = method == "POST" 
                        ? CreateExampleObjectFromObjectMetadata(objectMetadata, definition)
                        : CreateSuccessResponseExample(method)
                }
            };
        }

        // Add 422 response for validation errors
        if (!operation.Responses.ContainsKey("422"))
        {
            operation.Responses["422"] = CreateValidationErrorResponse();
        }

        Log.Debug("Enriched {Method} operation for {EndpointName} with {ParameterCount} parameters", 
            method, endpointName, parameters.Count);
    }

    /// <summary>
    /// Creates an example success response for PUT/PATCH operations
    /// </summary>
    private IOpenApiAny CreateSuccessResponseExample(string method)
    {
        return new OpenApiObject
        {
            ["success"] = new OpenApiBoolean(true),
            ["message"] = new OpenApiString($"Record updated successfully"),
            ["result"] = new OpenApiArray
            {
                new OpenApiObject
                {
                    ["result"] = new OpenApiString("array")
                }
            }
        };
    }

    private IOpenApiAny CreateExampleObjectFromObjectMetadata(
        List<Services.SqlMetadataService.ColumnMetadata>? metadata,
        EndpointDefinition definition)
    {
        if (metadata == null || !metadata.Any())
            return new OpenApiObject();

        var obj = new OpenApiObject();

        foreach (var column in metadata)
        {
            // CRITICAL FIX: Skip columns with empty or whitespace names
            if (string.IsNullOrWhiteSpace(column.ColumnName))
            {
                continue;
            }
            
            obj[column.ColumnName] = GenerateExampleValue(column);
        }

        return obj;
    }

    /// <summary>
    /// Enriches DELETE operation with object metadata (for primary key information)
    /// </summary>
    private void EnrichDeleteOperationWithObjectMetadata(
        OpenApiOperation operation,
        string endpointName,
        EndpointDefinition definition)
    {
        var metadata = _metadataService.GetObjectMetadata(endpointName);
        if (metadata == null || !metadata.Any())
        {
            Log.Debug("No object metadata available for DELETE endpoint {EndpointName}", endpointName);
            return;
        }

        // Use the same ID field detection as the controller
        var idFields = metadata.Where(m => IsIdField(m.ColumnName)).ToList();
        
        if (idFields.Any())
        {
            var idField = idFields.First();
            operation.Description = (operation.Description ?? "") + 
                $"\n\n**Primary Key:** `{idField.ColumnName}` ({idField.DataType})";
        }

        // Add procedure info if DELETE uses a stored procedure
        if (!string.IsNullOrEmpty(definition.Procedure))
        {
            operation.Description += $"\n\n**Uses Stored Procedure:** `{definition.Procedure}`";
        }
    }

    /// <summary>
    /// Creates an OpenAPI schema from object metadata (table/view columns)
    /// </summary>
    private OpenApiSchema CreateSchemaFromObjectMetadata(
        List<Services.SqlMetadataService.ColumnMetadata> metadata,
        bool excludePrimaryKey = false,
        EndpointDefinition? endpoint = null,
        bool isRequest = false)
    {
        var properties = new Dictionary<string, OpenApiSchema>();
        var required = new List<string>();

        // Get required columns from endpoint definition (for POST requests)
        if (isRequest && endpoint?.RequiredColumns != null)
        {
            required.AddRange(endpoint.RequiredColumns);
        }

        foreach (var column in metadata)
        {
            // Skip primary key in request schemas if specified
            if (excludePrimaryKey && column.IsPrimaryKey)
                continue;

            var columnSchema = CreateColumnSchema(column, endpoint);
            properties[column.ColumnName] = columnSchema;
        }

        var schema = new OpenApiSchema
        {
            Type = "object",
            Properties = properties
        };

        // Add required fields for request schemas
        if (required.Any())
        {
            schema.Required = new HashSet<string>(required);
        }

        return schema;
    }

    /// <summary>
    /// Creates an OpenAPI schema from procedure metadata
    /// </summary>
    private OpenApiSchema CreateSchemaFromProcedureMetadata(
        List<Services.SqlMetadataService.ParameterMetadata> parameters,
        EndpointDefinition endpoint,
        string method)
    {
        var properties = new Dictionary<string, OpenApiSchema>();
        var required = new List<string>();

        // Parse column mappings to understand aliases
        var allowedColumns = endpoint.AllowedColumns ?? new List<string>();
        var (aliasToDatabase, databaseToAlias) = 
            Classes.Helpers.ColumnMappingHelper.ParseColumnMappings(allowedColumns);

        // Add required columns from endpoint definition
        if (endpoint.RequiredColumns != null)
        {
            required.AddRange(endpoint.RequiredColumns);
        }

        foreach (var parameter in parameters)
        {
            // Skip output parameters for request body
            if (parameter.IsOutput)
                continue;

            var parameterSchema = CreateParameterSchema(parameter, endpoint);
            
            // Remove @ from parameter name for JSON property and map to column name
            var parameterNameWithoutAt = parameter.ParameterName.StartsWith('@') 
                ? parameter.ParameterName.Substring(1) 
                : parameter.ParameterName;

            // Skip reserved parameters
            if (IsReservedParameterName(parameterNameWithoutAt))
                continue;

            // Map parameter name to property name using the definition
            string? propertyName = MapParameterToPropertyName(parameterNameWithoutAt, databaseToAlias, aliasToDatabase, endpoint);

            // Skip if property name is empty or invalid
            if (string.IsNullOrWhiteSpace(propertyName))
                continue;

            // SPECIAL HANDLING: For PUT operations, include primary key parameters
            // For POST, exclude primary key (auto-generated)
            // For PUT, include primary key (required for updates)
            if (method == "POST" && propertyName.Equals(endpoint.PrimaryKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            properties[propertyName] = parameterSchema;
            
            // For PUT operations, primary key should be required
            if (method == "PUT" && propertyName.Equals(endpoint.PrimaryKey, StringComparison.OrdinalIgnoreCase))
            {
                required.Add(propertyName);
            }
            else if (method != "PATCH" && !parameter.IsNullable && !parameter.HasDefaultValue)
            {
                if (endpoint.RequiredColumns?.Contains(propertyName, StringComparer.OrdinalIgnoreCase) == true ||
                    (!parameter.IsNullable && !parameter.HasDefaultValue))
                {
                    required.Add(propertyName);
                }
            }
        }

        var schema = new OpenApiSchema
        {
            Type = "object",
            Properties = properties,
            Description = GetSchemaDescription(method, parameters.Count)
        };

        // For PATCH, no required fields (all optional)
        if (method != "PATCH" && required.Any())
        {
            schema.Required = new HashSet<string>(required);
        }

        return schema;
    }

    /// <summary>
    /// Creates a schema for a single column from object metadata
    /// </summary>
    private OpenApiSchema CreateColumnSchema(
        Services.SqlMetadataService.ColumnMetadata column,
        EndpointDefinition? endpoint = null)
    {
        var columnSchema = new OpenApiSchema
        {
            Type = MapClrTypeToOpenApiType(column.ClrType),
            Description = BuildColumnDescription(column, endpoint),
            Nullable = column.IsNullable
        };

        // Add format for specific types
        if (column.ClrType == "System.DateTime")
        {
            columnSchema.Format = "date-time";
        }
        else if (column.ClrType == "System.Guid")
        {
            columnSchema.Format = "uuid";
        }
        else if (column.DataType.Contains("int"))
        {
            columnSchema.Format = GetIntegerFormat(column.ClrType);
        }

        // Add max length for strings
        if (column.MaxLength.HasValue && column.MaxLength.Value > 0 && column.MaxLength.Value != -1)
        {
            columnSchema.MaxLength = column.MaxLength.Value;
        }

        // Add regex pattern validation if defined in endpoint
        if (endpoint?.ColumnValidation?.TryGetValue(column.ColumnName, out var validation) == true
            && !string.IsNullOrEmpty(validation.Regex))
        {
            columnSchema.Pattern = validation.Regex;

            // Append validation message to description
            if (!string.IsNullOrEmpty(validation.ValidationMessage))
            {
                columnSchema.Description += $"\n\n**Validation:** {validation.ValidationMessage}";
            }
        }

        // Add example values based on type
        columnSchema.Example = GenerateExampleValue(column);

        return columnSchema;
    }

    /// <summary>
    /// Creates a schema for a single procedure parameter
    /// </summary>
    private OpenApiSchema CreateParameterSchema(
        Services.SqlMetadataService.ParameterMetadata parameter,
        EndpointDefinition endpoint)
    {
        // Remove @ from parameter name for JSON property
        var parameterNameWithoutAt = parameter.ParameterName.StartsWith('@') 
            ? parameter.ParameterName.Substring(1) 
            : parameter.ParameterName;

        var parameterSchema = new OpenApiSchema
        {
            Type = MapClrTypeToOpenApiType(parameter.ClrType),
            Description = BuildParameterDescription(parameter),
            Nullable = parameter.IsNullable || parameter.HasDefaultValue
        };

        // Add format for specific types
        if (parameter.ClrType == "System.DateTime")
        {
            parameterSchema.Format = "date-time";
        }
        else if (parameter.ClrType == "System.Guid")
        {
            parameterSchema.Format = "uuid";
        }
        else if (parameter.DataType.Contains("int"))
        {
            parameterSchema.Format = GetIntegerFormat(parameter.ClrType);
        }

        // Add max length for strings
        if (parameter.MaxLength.HasValue && parameter.MaxLength.Value > 0 && parameter.MaxLength.Value != -1)
        {
            parameterSchema.MaxLength = parameter.MaxLength.Value;
        }

        // Add regex pattern validation if defined in endpoint for this parameter
        if (endpoint.ColumnValidation?.TryGetValue(parameterNameWithoutAt, out var validation) == true 
            && !string.IsNullOrEmpty(validation.Regex))
        {
            parameterSchema.Pattern = validation.Regex;
            
            if (!string.IsNullOrEmpty(validation.ValidationMessage))
            {
                parameterSchema.Description += $"\n\n**Validation:** {validation.ValidationMessage}";
            }
        }

        // Add example value
        parameterSchema.Example = GenerateExampleValueFromParameter(parameter, parameterNameWithoutAt);

        return parameterSchema;
    }

    /// <summary>
    /// Builds a descriptive text for a column
    /// </summary>
    private string BuildColumnDescription(
        Services.SqlMetadataService.ColumnMetadata column,
        EndpointDefinition? endpoint = null)
    {
        var parts = new List<string>();

        parts.Add($"SQL Type: {column.DataType}");

        if (column.MaxLength.HasValue && column.MaxLength.Value > 0 && column.MaxLength.Value != -1)
        {
            parts.Add($"Max Length: {column.MaxLength.Value}");
        }

        if (column.NumericPrecision.HasValue)
        {
            parts.Add($"Precision: {column.NumericPrecision.Value}");
        }

        if (column.NumericScale.HasValue)
        {
            parts.Add($"Scale: {column.NumericScale.Value}");
        }

        if (column.IsPrimaryKey)
        {
            parts.Add("**Primary Key**");
        }

        // Add required indicator from endpoint definition
        if (endpoint?.RequiredColumns?.Contains(column.ColumnName, StringComparer.OrdinalIgnoreCase) == true)
        {
            parts.Add("**Required**");
        }

        return string.Join(" | ", parts);
    }

    /// <summary>
    /// Builds a descriptive text for a procedure parameter
    /// </summary>
    private string BuildParameterDescription(Services.SqlMetadataService.ParameterMetadata parameter)
    {
        var parts = new List<string>();

        parts.Add($"SQL Type: {parameter.DataType}");
        parts.Add($"Position: {parameter.Position}");

        if (parameter.MaxLength.HasValue && parameter.MaxLength.Value > 0 && parameter.MaxLength.Value != -1)
        {
            parts.Add($"Max Length: {parameter.MaxLength.Value}");
        }

        if (parameter.NumericPrecision.HasValue)
        {
            parts.Add($"Precision: {parameter.NumericPrecision.Value}");
        }

        if (parameter.NumericScale.HasValue)
        {
            parts.Add($"Scale: {parameter.NumericScale.Value}");
        }

        if (parameter.IsOutput)
        {
            parts.Add("**Output Parameter**");
        }

        if (parameter.HasDefaultValue)
        {
            parts.Add("**Has Default Value**");
        }

        if (parameter.IsNullable)
        {
            parts.Add("**Nullable**");
        }

        return string.Join(" | ", parts);
    }

    /// <summary>
    /// Gets the request body description based on method
    /// </summary>
    private string GetRequestBodyDescription(string method, EndpointDefinition definition)
    {
        return method switch
        {
            "POST" => "The object to create" + (definition.Procedure != null ? "" : ""),
            "PUT" => "The updated object" + (definition.Procedure != null ? "" : ""),
            "PATCH" => "The properties to update (partial)" + (definition.Procedure != null ? "" : ""),
            _ => "The request body"
        };
    }

    /// <summary>
    /// Gets the schema description based on method
    /// </summary>
    private string GetSchemaDescription(string method, int parameterCount)
    {
        return method switch
        {
            "POST" => $"Stored procedure parameters for creating a new record ({parameterCount} parameters)",
            "PUT" => $"Stored procedure parameters for updating a record ({parameterCount} parameters)",
            "PATCH" => $"Stored procedure parameters for partial update ({parameterCount} parameters, all optional)",
            _ => $"Stored procedure parameters ({parameterCount} parameters)"
        };
    }

    /// <summary>
    /// Creates a validation error response schema
    /// </summary>
    private OpenApiResponse CreateValidationErrorResponse()
    {
        return new OpenApiResponse
        {
            Description = "Validation failed - Required fields missing or regex pattern mismatch",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = "object",
                        Properties = new Dictionary<string, OpenApiSchema>
                        {
                            ["error"] = new OpenApiSchema { Type = "string" },
                            ["details"] = new OpenApiSchema
                            {
                                Type = "array",
                                Items = new OpenApiSchema
                                {
                                    Type = "object",
                                    Properties = new Dictionary<string, OpenApiSchema>
                                    {
                                        ["field"] = new OpenApiSchema { Type = "string" },
                                        ["message"] = new OpenApiSchema { Type = "string" }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    /// <summary>
    /// Creates an example object from procedure parameters
    /// </summary>
    private IOpenApiAny CreateExampleObjectFromProcedure(
        List<Services.SqlMetadataService.ParameterMetadata> parameters,
        string method,
        EndpointDefinition definition)
    {
        var obj = new OpenApiObject();

        // Parse column mappings
        var allowedColumns = definition.AllowedColumns ?? new List<string>();
        var (aliasToDatabase, databaseToAlias) =
            Classes.Helpers.ColumnMappingHelper.ParseColumnMappings(allowedColumns);

        foreach (var parameter in parameters)
        {
            if (parameter.IsOutput)
                continue;

            // Remove @ from parameter name for JSON property
            var parameterNameWithoutAt = parameter.ParameterName.StartsWith('@')
                ? parameter.ParameterName.Substring(1)
                : parameter.ParameterName;

            // Skip reserved parameters
            if (IsReservedParameterName(parameterNameWithoutAt))
                continue;

            // Map parameter name to column name
            string? propertyName = MapParameterToPropertyName(parameterNameWithoutAt, databaseToAlias, aliasToDatabase, definition);

            // Skip if property name is empty or invalid
            if (string.IsNullOrWhiteSpace(propertyName))
                continue;

            // Use the same ID field detection logic as EndpointController
            bool isIdField = IsIdField(propertyName);

            // If field can't be mapped, don't return empty
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                continue;
            }

            // Special handling for ID parameters - exclude for POST, include for others
            if (method == "POST" && isIdField)
            {
                continue;
            }

            obj[propertyName!] = GenerateExampleValueFromParameter(parameter, propertyName!);
        }

        // If no properties were added, add some basic examples
        if (!obj.Any())
        {
            obj["exampleField"] = new OpenApiString("exampleValue");
        }


        return obj;
    }

    /// <summary>
    /// Maps a parameter name to a JSON property name using column mappings
    /// </summary>
    private string? MapParameterToPropertyName(
        string parameterName, 
        Dictionary<string, string> databaseToAlias,
        Dictionary<string, string> aliasToDatabase,
        EndpointDefinition definition)
    {
        // Special case: primary key parameters should always be allowed for PUT operations
        // Check if this parameter maps to the primary key column
        if (!string.IsNullOrEmpty(definition.PrimaryKey))
        {
            // If parameter name matches primary key directly
            if (parameterName.Equals(definition.PrimaryKey, StringComparison.OrdinalIgnoreCase))
            {
                return definition.PrimaryKey;
            }
            
            // If parameter name is "id" and primary key is something like "RequestId"
            if (parameterName.Equals("id", StringComparison.OrdinalIgnoreCase))
            {
                // Check if primary key has an alias in the allowed columns
                if (databaseToAlias.TryGetValue(definition.PrimaryKey, out var pkAlias))
                {
                    return pkAlias; // Returns "ID"
                }
                return definition.PrimaryKey; // Returns "RequestId"
            }
        }

        // If parameter maps directly to a database column with an alias, use the alias
        if (databaseToAlias.TryGetValue(parameterName, out var columnAlias))
        {
            return columnAlias;
        }
        
        // If parameter is an alias that maps to a database column, use the parameter name
        if (aliasToDatabase.ContainsKey(parameterName))
        {
            return parameterName;
        }
        
        // For parameters that don't map to columns, use the parameter name
        if (string.IsNullOrWhiteSpace(parameterName))
        {
            return null;
        }
        
        return parameterName;
    }

    /// <summary>
    /// Checks if a parameter is a common ID parameter in stored procedures
    /// </summary>
    private bool IsIdParameter(string parameterName)
    {
        var idParameters = new[]
        {
            "id", "Id", "ID", "primarykey", "PrimaryKey", "PK",
            "recordid", "RecordId", "internalid", "InternalId"
        };

        return idParameters.Contains(parameterName, StringComparer.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// Checks if a parameter name is reserved and should be excluded
    /// </summary>
    private bool IsReservedParameterName(string parameterName)
    {
        var reservedNames = new[] { "method", "action", "operation" };
        return reservedNames.Contains(parameterName.ToLowerInvariant());
    }

    /// <summary>
    /// Generates an example value based on column metadata
    /// </summary>
    private IOpenApiAny GenerateExampleValue(Services.SqlMetadataService.ColumnMetadata column)
    {
        if (column.IsNullable)
            return new OpenApiNull();

        return column.ClrType switch
        {
            "System.String" => new OpenApiString(column.IsPrimaryKey ? "ABC123" : "example"),
            "System.Int32" => new OpenApiInteger(column.IsPrimaryKey ? 1 : 42),
            "System.Int64" => new OpenApiLong(column.IsPrimaryKey ? 1L : 42L),
            "System.Boolean" => new OpenApiBoolean(true),
            "System.Decimal" or "System.Double" => new OpenApiDouble(99.99),
            "System.DateTime" => new OpenApiString(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")),
            "System.Guid" => new OpenApiString(Guid.NewGuid().ToString()),
            _ => new OpenApiString("value")
        };
    }

    /// <summary>
    /// Checks if a field name matches common ID field patterns (same logic as EndpointController)
    /// </summary>
    private bool IsIdField(string fieldName)
    {
        var idFieldNames = new[]
        {
            "id", "Id", "ID", "IdField", "IDField",
            "pk", "PK", "PrimaryKey", "primaryKey", "primarykey",
            "internalId", "InternalId", "InternalID", "internalid",
            "recordId", "RecordId"
        };
        
        return idFieldNames.Contains(fieldName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Generates an example value based on parameter metadata (without property name)
    /// </summary>
    private IOpenApiAny GenerateExampleValueFromParameter(Services.SqlMetadataService.ParameterMetadata parameter)
    {
        // Use parameter name for context-aware examples
        var paramName = parameter.ParameterName.ToLowerInvariant().Replace("@", "");

        if (paramName.Contains("id") && parameter.ClrType == "System.Guid")
        {
            return new OpenApiString(Guid.NewGuid().ToString());
        }
        else if (paramName.Contains("id") && parameter.ClrType.Contains("Int"))
        {
            return new OpenApiInteger(1);
        }
        else if (paramName.Contains("name") || paramName.Contains("title"))
        {
            return new OpenApiString($"Example {paramName}");
        }
        else if (paramName.Contains("email"))
        {
            return new OpenApiString("user@example.com");
        }
        else if (paramName.Contains("date") || paramName.Contains("time"))
        {
            return new OpenApiString(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"));
        }
        else if (paramName.StartsWith("is") || paramName.StartsWith("has") || paramName.Contains("active"))
        {
            return new OpenApiBoolean(true);
        }
        else if (paramName.Contains("amount") || paramName.Contains("price") || paramName.Contains("cost"))
        {
            return new OpenApiDouble(99.99);
        }

        // Fallback to type-based examples
        return parameter.ClrType switch
        {
            "System.String" => new OpenApiString($"example {paramName}"),
            "System.Int32" => new OpenApiInteger(42),
            "System.Int64" => new OpenApiLong(42L),
            "System.Boolean" => new OpenApiBoolean(true),
            "System.Decimal" or "System.Double" => new OpenApiDouble(99.99),
            "System.DateTime" => new OpenApiString(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")),
            "System.Guid" => new OpenApiString(Guid.NewGuid().ToString()),
            _ => parameter.IsNullable || parameter.HasDefaultValue ? new OpenApiNull() : new OpenApiString("value")
        };
    }

    /// <summary>
    /// Generates an example value based on parameter metadata and property name
    /// </summary>
    private IOpenApiAny GenerateExampleValueFromParameter(
        Services.SqlMetadataService.ParameterMetadata parameter,
        string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return new OpenApiNull();
        }

        // Use property name for context-aware examples
        var propName = propertyName.ToLowerInvariant();

        if (propName.Contains("id") && parameter.ClrType == "System.Guid")
        {
            return new OpenApiString(Guid.NewGuid().ToString());
        }
        else if (propName.Contains("id") && parameter.ClrType.Contains("Int"))
        {
            return new OpenApiInteger(1);
        }
        else if (propName.Contains("name") || propName.Contains("title"))
        {
            return new OpenApiString($"Example {propertyName}");
        }
        else if (propName.Contains("email"))
        {
            return new OpenApiString("user@example.com");
        }
        else if (propName.Contains("date") || propName.Contains("time"))
        {
            return new OpenApiString(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"));
        }
        else if (propName.StartsWith("is") || propName.StartsWith("has") || propName.Contains("active"))
        {
            return new OpenApiBoolean(true);
        }
        else if (propName.Contains("amount") || propName.Contains("price") || propName.Contains("cost"))
        {
            return new OpenApiDouble(99.99);
        }

        // Fallback to type-based examples
        return parameter.ClrType switch
        {
            "System.String" => new OpenApiString($"example {propertyName}"),
            "System.Int32" => new OpenApiInteger(42),
            "System.Int64" => new OpenApiLong(42L),
            "System.Boolean" => new OpenApiBoolean(true),
            "System.Decimal" or "System.Double" => new OpenApiDouble(99.99),
            "System.DateTime" => new OpenApiString(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")),
            "System.Guid" => new OpenApiString(Guid.NewGuid().ToString()),
            _ => parameter.IsNullable || parameter.HasDefaultValue ? new OpenApiNull() : new OpenApiString("value")
        };
    }

    /// <summary>
    /// Creates an example object from metadata
    /// </summary>
    private IOpenApiAny CreateExampleObject(
        List<Services.SqlMetadataService.ColumnMetadata> metadata,
        bool excludePrimaryKey = false)
    {
        var obj = new OpenApiObject();

        foreach (var column in metadata)
        {
            if (excludePrimaryKey && column.IsPrimaryKey)
                continue;

            obj[column.ColumnName] = GenerateExampleValue(column);
        }

        return obj;
    }

    /// <summary>
    /// Maps CLR type to OpenAPI type
    /// </summary>
    private string MapClrTypeToOpenApiType(string clrType)
    {
        return clrType switch
        {
            "System.String" => "string",
            "System.Int16" or "System.Int32" or "System.Int64" or "System.Byte" => "integer",
            "System.Decimal" or "System.Double" or "System.Single" => "number",
            "System.Boolean" => "boolean",
            "System.DateTime" or "System.DateTimeOffset" => "string",
            "System.Guid" => "string",
            "System.TimeSpan" => "string",
            "System.Byte[]" => "string",
            _ => "object"
        };
    }

    /// <summary>
    /// Gets the format string for integer types
    /// </summary>
    private string GetIntegerFormat(string clrType)
    {
        return clrType switch
        {
            "System.Int64" => "int64",
            "System.Int32" => "int32",
            _ => "int32"
        };
    }
}