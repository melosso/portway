namespace PortwayApi.Classes;

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>Detailed exception for composite step failures</summary>
public class CompositeStepException : Exception
{
    /// <summary>The name of the step that failed</summary>
    public string StepName { get; }
    
    /// <summary>The HTTP status code returned by the failed request</summary>
    public int StatusCode { get; }
    
    /// <summary>A detailed error message extracted from the response</summary>
    public string ErrorDetail { get; }
    
    /// <summary>The full response body from the failed request</summary>
    public string ResponseContent { get; }
    
    /// <summary>The structured error data, if the response was JSON</summary>
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
