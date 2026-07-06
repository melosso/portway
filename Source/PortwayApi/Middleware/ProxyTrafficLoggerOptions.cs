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

/// <summary>Enhanced configuration options for proxy traffic logging with validation</summary>
public class ProxyTrafficLoggerOptions
{
    public bool Enabled { get; set; } = false;
    public int QueueCapacity { get; set; } = 10000;
    public string StorageType { get; set; } = "file";
    public string SqlitePath { get; set; } = "log/traffic_logs.db";
    public string LogDirectory { get; set; } = "log/traffic";
    public int MaxFileSizeMB { get; set; } = 50;
    public int MaxFileCount { get; set; } = 10;
    public string FilePrefix { get; set; } = "proxy_traffic_";
    public int BatchSize { get; set; } = 100;
    public int FlushIntervalMs { get; set; } = 1000;
    public bool IncludeRequestBodies { get; set; } = false;
    public bool IncludeResponseBodies { get; set; } = false;
    public int MaxBodyCaptureSizeBytes { get; set; } = 4096;
    public bool CaptureHeaders { get; set; } = true;
    public bool EnableInfoLogging { get; set; } = true;
}
