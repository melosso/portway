namespace PortwayApi.Classes;
/// <summary>
/// Detailed exception for composite step failures
/// </summary>
public sealed class CompositeStepException : Exception
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
    /// The gateway's description of the failure, sent to the client
    /// </summary>
    public string ErrorDetail { get; }

    /// <summary>
    /// The masked upstream response body, logged at Debug and never sent to the client
    /// </summary>
    public string ResponseContent { get; }

    public CompositeStepException(
        string message,
        string stepName,
        int statusCode,
        string errorDetail,
        string responseContent)
        : base(message)
    {
        StepName = stepName;
        StatusCode = statusCode;
        ErrorDetail = errorDetail;
        ResponseContent = responseContent;
    }
}
