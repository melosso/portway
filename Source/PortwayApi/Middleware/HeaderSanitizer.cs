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

/// <summary>Utility for handling sensitive headers</summary>
public static class HeaderSanitizer
{
    // Use a concurrent dictionary for thread-safe, performant lookups
    private static readonly ConcurrentDictionary<string, bool> _sensitiveHeaders = 
        new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
    {
        ["Authorization"] = true,
        ["Cookie"] = true,
        ["X-API-Key"] = true,
        ["API-Key"] = true,
        ["Password"] = true,
        ["X-Auth-Token"] = true,
        ["Token"] = true,
        ["Secret"] = true,
        ["Credential"] = true,
        ["Access-Token"] = true,
        ["X-Access-Token"] = true
    };

    /// <summary>Sanitizes header value if it's a sensitive header</summary>
    public static string SanitizeHeaderValue(string headerName, string headerValue)
    {
        return _sensitiveHeaders.ContainsKey(headerName) ? "[REDACTED]" : headerValue;
    }

    /// <summary>Checks if a header is considered sensitive</summary>
    public static bool IsSensitiveHeader(string headerName)
    {
        return _sensitiveHeaders.ContainsKey(headerName);
    }
}
