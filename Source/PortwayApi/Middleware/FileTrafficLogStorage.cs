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

/// <summary>File-based implementation of log storage</summary>
public class FileTrafficLogStorage : ITrafficLogStorage
{
    private readonly ProxyTrafficLoggerOptions _options;
    private string _currentLogFile;
    private long _currentFileSize;
    private readonly object _fileLock = new object();

    public FileTrafficLogStorage(IOptions<ProxyTrafficLoggerOptions> options)
    {
        _options = options.Value;
        _currentLogFile = GenerateLogFileName();
        _currentFileSize = 0;
    }

    public Task InitializeAsync()
    {
        try
        {
            // Ensure log directory exists
            if (!Directory.Exists(_options.LogDirectory))
            {
                Directory.CreateDirectory(_options.LogDirectory);
                Serilog.Log.Information("Created traffic log directory: {Directory}", _options.LogDirectory);
            }

            // Check if the current log file exists and get its size
            if (File.Exists(_currentLogFile))
            {
                var fileInfo = new FileInfo(_currentLogFile);
                _currentFileSize = fileInfo.Length;
            }

            // Delete old log files if needed
            CleanupOldLogFiles();

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error initializing file storage for traffic logs");
            throw;
        }
    }

    public Task SaveLogsAsync(IEnumerable<ProxyTrafficLogEntry> logs)
    {
        try
        {
            var linesToWrite = new List<string>();
            
            foreach (var log in logs)
            {
                // Use JSON format for all details
                string logJson = JsonSerializer.Serialize(log, new JsonSerializerOptions
                {
                    WriteIndented = false
                });
                
                linesToWrite.Add(logJson);
            }

            // Write to file with lock to prevent concurrent access issues
            lock (_fileLock)
            {
                // Check if we need to roll over to a new file
                CheckRolloverNeeded(linesToWrite);
                
                // Append to the current log file
                File.AppendAllLines(_currentLogFile, linesToWrite);
                
                // Update current file size
                _currentFileSize += linesToWrite.Sum(l => Encoding.UTF8.GetByteCount(l) + Environment.NewLine.Length);
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error saving traffic logs to file");
            throw;
        }
    }

    private void CheckRolloverNeeded(List<string> linesToWrite)
    {
        // Calculate the size of the lines we're about to write
        long batchSize = linesToWrite.Sum(l => Encoding.UTF8.GetByteCount(l) + Environment.NewLine.Length);
        
        // Check if adding these lines would exceed the max file size
        if (_currentFileSize + batchSize > _options.MaxFileSizeMB * 1024 * 1024)
        {
            // Roll over to a new file
            _currentLogFile = GenerateLogFileName();
            _currentFileSize = 0;
            
            // Clean up old files
            CleanupOldLogFiles();
        }
    }

    private string GenerateLogFileName()
    {
        return Path.Combine(
            _options.LogDirectory,
            $"{_options.FilePrefix}{DateTime.UtcNow:yyyyMMdd_HHmmss}.json"
        );
    }

    private void CleanupOldLogFiles()
    {
        try
        {
            var logFiles = Directory.GetFiles(_options.LogDirectory, $"{_options.FilePrefix}*.json")
                .OrderByDescending(f => f)
                .ToList();
            
            // Keep only the most recent MaxFileCount files
            if (logFiles.Count > _options.MaxFileCount)
            {
                foreach (var file in logFiles.Skip(_options.MaxFileCount))
                {
                    File.Delete(file);
                    Serilog.Log.Debug("Deleted old traffic log file: {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error cleaning up old traffic log files");
        }
    }
}
