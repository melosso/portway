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

/// <summary>Interface for log storage providers</summary>
public interface ITrafficLogStorage
{
    Task InitializeAsync();
    Task SaveLogsAsync(IEnumerable<ProxyTrafficLogEntry> logs);
}
