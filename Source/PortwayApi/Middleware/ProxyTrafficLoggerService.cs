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

/// <summary>Background service that processes log entries from the queue</summary>
public class ProxyTrafficLoggerService : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly System.Threading.Channels.Channel<ProxyTrafficLogEntry> _logChannel;
    private readonly ProxyTrafficLoggerOptions _options;
    private readonly ITrafficLogStorage _logStorage;

    public ProxyTrafficLoggerService(
        System.Threading.Channels.Channel<ProxyTrafficLogEntry> logChannel, 
        IOptions<ProxyTrafficLoggerOptions> options,
        ITrafficLogStorage logStorage)
    {
        _logChannel = logChannel;
        _options = options.Value;
        _logStorage = logStorage;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Serilog.Log.Debug("Proxy Traffic Logger Service started");

        try
        {
            Serilog.Log.Warning($"Traffic tracing enabled with storage type: {_options.StorageType}. This comes with a significant performance overhead.");

            await _logStorage.InitializeAsync();

            var batch = new List<ProxyTrafficLogEntry>(_options.BatchSize);
            var flushInterval = TimeSpan.FromMilliseconds(_options.FlushIntervalMs);

            while (!stoppingToken.IsCancellationRequested)
            {
                bool itemsProcessed = false;
                
                // Read up to BatchSize items without blocking
                while (batch.Count < _options.BatchSize)
                {
                    if (_logChannel.Reader.TryRead(out var logEntry) && logEntry != null)
                    {
                        batch.Add(logEntry);
                    }
                    else
                    {
                        // No more items in the queue
                        break;
                    }
                }

                // If we have items, process them
                if (batch.Count > 0)
                {
                    await _logStorage.SaveLogsAsync(batch);
                    Serilog.Log.Debug($"Processed {batch.Count} traffic log entries");
                    batch.Clear();
                    itemsProcessed = true;
                }
                
                // If no items were processed, wait for more data or the flush interval
                if (!itemsProcessed)
                {
                    try
                    {
                        // Use a cancellation token source with timeout
                        using var timeoutCts = new CancellationTokenSource(flushInterval);
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, stoppingToken);
                        
                        // Wait for an item or timeout
                        await _logChannel.Reader.WaitToReadAsync(linkedCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // This is expected - either timeout or cancellation
                        if (stoppingToken.IsCancellationRequested)
                        {
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error in Proxy Traffic Logger Service");
        }
        finally
        {
            Serilog.Log.Information("Proxy Traffic Logger Service stopping...");
            
            // Flush any remaining logs before shutdown
            try
            {
                List<ProxyTrafficLogEntry> remainingLogs = new();
                while (_logChannel.Reader.TryRead(out var log))
                {
                    remainingLogs.Add(log);
                }
                
                if (remainingLogs.Count > 0)
                {
                    await _logStorage.SaveLogsAsync(remainingLogs);
                    Serilog.Log.Information($"Flushed {remainingLogs.Count} remaining traffic log entries on shutdown");
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Error flushing traffic logs on shutdown");
            }
        }
    }
}
