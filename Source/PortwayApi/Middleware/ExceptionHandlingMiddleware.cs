namespace PortwayApi.Middleware;

using System;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Serilog;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;

    public ExceptionHandlingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The caller hung up; nothing failed and there is nobody left to answer
            Log.Debug("Request cancelled by the client: {Path}", context.Request.Path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unhandled exception occurred while processing request: {Path}", context.Request.Path);

            // Propagate error status to the active OTel span
            var activity = Activity.Current;
            if (activity != null)
            {
                activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
                {
                    ["exception.type"]       = ex.GetType().FullName,
                    ["exception.message"]    = ex.Message,
                    ["exception.stacktrace"] = ex.ToString()
                }));
            }

            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Headers are already on the wire, so the status cannot be changed and a body would corrupt the response
        if (context.Response.HasStarted)
        {
            Log.Error("Error details (response already started, cannot return 500): {Message}", exception.Message);
            return;
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

        // In production, don't expose detailed exception information
        var response = PortwayApi.Helpers.ErrorResponse.Traced(
            "An unexpected error occurred.",
            PortwayApi.Helpers.PortwayResults.TraceIdOf(context));

        // Log detailed information for debugging
        Log.Error("Error details: {Message}", exception.Message);
        if (exception.StackTrace != null)
        {
            Log.Error("Stack trace: {StackTrace}", exception.StackTrace);
        }

        var jsonResponse = JsonSerializer.Serialize(response);
        await context.Response.WriteAsync(jsonResponse);
    }
}

// Extension method to make it easier to add the middleware to the pipeline
public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionHandlingMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}