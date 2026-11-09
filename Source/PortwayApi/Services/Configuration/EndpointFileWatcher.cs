using System.Text.Json;
using Microsoft.Extensions.Options;
using Serilog;
using PortwayApi.Classes;
using PortwayApi.Services.Mcp;

namespace PortwayApi.Services.Configuration;

/// <summary>
/// Monitors the endpoints folder for changes and invalidates endpoint/metadata caches
/// </summary>
public class EndpointFileWatcher : FileWatchPump
{
    private readonly SqlMetadataService _sqlMetadataService;
    private readonly IOptionsMonitor<EndpointReloadingOptions> _optionsMonitor;
    private readonly SseBroadcaster? _broadcaster;
    private readonly ReloadTracker _reloadTracker;
    private readonly McpEndpointRegistry? _mcpRegistry;

    public EndpointFileWatcher(
        SqlMetadataService sqlMetadataService,
        IOptionsMonitor<EndpointReloadingOptions> optionsMonitor,
        ReloadTracker reloadTracker,
        SseBroadcaster? broadcaster = null,
        McpEndpointRegistry? mcpRegistry = null)
        : base(Path.Combine(Directory.GetCurrentDirectory(), "endpoints"), "Endpoint")
    {
        _sqlMetadataService = sqlMetadataService;
        _optionsMonitor = optionsMonitor;
        _reloadTracker = reloadTracker;
        _broadcaster = broadcaster;
        _mcpRegistry = mcpRegistry;
    }

    protected override TimeSpan DebounceTime => TimeSpan.FromMilliseconds(_optionsMonitor.CurrentValue.DebounceMs);

    protected override bool ShouldStart()
    {
        if (_optionsMonitor.CurrentValue.Enabled)
            return true;

        Log.Information("Endpoint hot-reload is DISABLED via configuration");
        return false;
    }

    protected override Task HandleFileChangeAsync(string filePath, WatcherChangeTypes changeType)
    {
        try
        {
            // Extract endpoint type from path
            var endpointType = EndpointHandler.GetEndpointTypeFromPath(filePath);
            if (endpointType == null)
            {
                Log.Debug("Could not determine endpoint type from path: {Path}", filePath);
                return Task.CompletedTask;
            }

            var endpointName = DescribeEndpoint(filePath);
            if (endpointName is null)
            {
                Log.Debug("Could not determine endpoint name from path: {Path}", filePath);
                return Task.CompletedTask;
            }

            // Reload endpoint definitions
            EndpointHandler.ReloadEndpointType(endpointType.Value);

            if (endpointType == EndpointType.SQL)
                _sqlMetadataService.QueueRefresh();

            Log.Information("Endpoint '{Name}' ({Type}) changed, will reload on next request", endpointName, endpointType);

            // Re-populate the MCP registry so IsMcpExposed changes take effect without restart
            _mcpRegistry?.Refresh();

            _reloadTracker.RecordEndpointReload();
            _broadcaster?.Broadcast("reload", JsonSerializer.Serialize(new { type = "endpoints" }));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error handling endpoint file change for {Path}", filePath);
        }

        return Task.CompletedTask;
    }

    private static readonly HashSet<string> _typeFolderNames = new(StringComparer.OrdinalIgnoreCase)
        { "SQL", "Proxy", "Static", "Files", "Webhooks" };

    /// <summary>
    /// Endpoint identity, e.g. CRM/Accounts@v2
    /// </summary>
    internal static string? DescribeEndpoint(string filePath)
    {
        var segments = filePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return null;

        segments = segments[^1].Equals("entity.json", StringComparison.OrdinalIgnoreCase)
            ? segments[..^1]
            : [.. segments[..^1], Path.GetFileNameWithoutExtension(segments[^1])];
        var root = Array.FindLastIndex(segments, s => s.Equals("endpoints", StringComparison.OrdinalIgnoreCase));
        if (root < 0 || root + 2 >= segments.Length || !_typeFolderNames.Contains(segments[root + 1]))
            return null;

        var parts = segments[(root + 2)..];
        return parts.Length > 1 && EndpointVersion.TryParse(parts[^1], out var version)
            ? string.Join('/', parts[..^1]) + EndpointVersion.Suffix(version)
            : string.Join('/', parts);
    }
}
