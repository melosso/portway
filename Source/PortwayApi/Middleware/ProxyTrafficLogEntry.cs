namespace PortwayApi.Middleware;

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PortwayApi.Auth;
using Serilog;

/// <summary>Enhanced log entry</summary>
public class ProxyTrafficLogEntry
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string QueryString { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string EndpointName { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public long RequestSize { get; set; }
    public long ResponseSize { get; set; }
    public int DurationMs { get; set; }
    public string? Username { get; set; }
    public string ClientIp { get; set; } = string.Empty;
    public string? RequestBody { get; set; }
    public string? ResponseBody { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public Dictionary<string, string> RequestHeaders { get; set; } = new();

    /// <summary>Validates the log entry before storage</summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(Method) 
                && !string.IsNullOrWhiteSpace(Path) 
                && !string.IsNullOrWhiteSpace(TraceId);
    }

    /// <summary>Truncates body content if it exceeds max size</summary>
    public void TruncateBodyContent(int maxSize)
    {
        if (!string.IsNullOrEmpty(RequestBody) && RequestBody.Length > maxSize)
        {
            RequestBody = RequestBody.Substring(0, maxSize) + "...";
        }

        if (!string.IsNullOrEmpty(ResponseBody) && ResponseBody.Length > maxSize)
        {
            ResponseBody = ResponseBody.Substring(0, maxSize) + "...";
        }
    }
}
