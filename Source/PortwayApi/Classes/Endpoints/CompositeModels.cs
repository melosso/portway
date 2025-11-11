namespace PortwayApi.Classes;

using System.Text.Json.Nodes;

/// <summary>
/// Defines a composite endpoint that represents a multi-step API process
/// </summary>
public class CompositeDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<CompositeStep> Steps { get; set; } = new List<CompositeStep>();
}

/// <summary>
/// Represents a step within a composite endpoint process
/// </summary>
public class CompositeStep
{
    public string Name { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Method { get; set; } = "POST";
    public string? DependsOn { get; set; }
    public bool IsArray { get; set; } = false;
    public string? ArrayProperty { get; set; }
    public string? SourceProperty { get; set; }
    public Dictionary<string, string> TemplateTransformations { get; set; } = new();
}

/// <summary>
/// Result of a composite endpoint execution with enhanced error details
/// </summary>
public class CompositeResult
{
    /// <summary>
    /// Indicates whether the composite operation was successful
    /// </summary>
    public bool Success { get; set; } = true;
    
    /// <summary>
    /// Dictionary of results from each step in the composite operation
    /// </summary>
    public Dictionary<string, object> StepResults { get; set; } = new();
    
    /// <summary>
    /// The name of the step that failed (null if successful)
    /// </summary>
    public string? ErrorStep { get; set; }
    
    /// <summary>
    /// Error message if the composite operation failed
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Detailed error information from the underlying API
    /// </summary>
    public string? ErrorDetail { get; set; }
    
    /// <summary>
    /// The HTTP status code of the failed request
    /// </summary>
    public int? StatusCode { get; set; }
}

/// <summary>
/// Detailed exception for composite step failures
/// </summary>
public class CompositeStepException : Exception
{
    /// <summary>
    /// The name of the step that failed
    /// </summary>
    public string StepName { get; }
    
    /// <summary>
    /// The HTTP status code returned by the failed request
    /// </summary>
    public int StatusCode { get; }
    
    /// <summary>
    /// A detailed error message extracted from the response
    /// </summary>
    public string ErrorDetail { get; }
    
    /// <summary>
    /// The full response body from the failed request
    /// </summary>
    public string ResponseContent { get; }
    
    /// <summary>
    /// The structured error data, if the response was JSON
    /// </summary>
    public object? StructuredError { get; }
    
    public CompositeStepException(
        string message,
        string stepName,
        int statusCode,
        string errorDetail,
        string responseContent,
        object? structuredError = null) 
        : base(message)
    {
        StepName = stepName;
        StatusCode = statusCode;
        ErrorDetail = errorDetail;
        ResponseContent = responseContent;
        StructuredError = structuredError;
    }
}