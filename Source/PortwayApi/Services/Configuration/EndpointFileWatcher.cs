using System.Text.Json;
using Microsoft.Extensions.Options;
using Serilog;
using PortwayApi.Classes;
using PortwayApi.Services;
using PortwayApi.Services.Mcp;

namespace PortwayApi.Services.Configuration;

/// <summary>Monitors the endpoints folder for changes and invalidates endpoint/metadata caches</summary>
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
        _reloadTracker  = reloadTracker;
        _broadcaster    = broadcaster;
        _mcpRegistry    = mcpRegistry;
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

            // Extract endpoint name from file path
            var endpointName = ExtractEndpointName(filePath);
            if (string.IsNullOrEmpty(endpointName))
            {
                Log.Debug("Could not determine endpoint name from path: {Path}", filePath);
                return Task.CompletedTask;
            }

            // Reload endpoint definitions
            EndpointHandler.ReloadEndpointType(endpointType.Value);

            // Invalidate SQL metadata cache if it's a SQL endpoint
            if (endpointType == EndpointType.SQL)
            {
                _sqlMetadataService.ClearEndpointMetadata(endpointName);
                Log.Debug("SQL metadata cleared for endpoint '{Endpoint}'", endpointName);
            }

            var ns = ExtractNamespace(filePath);
            if (ns != null)
                Log.Information("Endpoint '{Name}' ({Type}, namespace: {Namespace}) changed, will reload on next request", endpointName, endpointType, ns);
            else
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

    private string? ExtractEndpointName(string filePath)
    {
        try
        {
            // For entity.json files, the endpoint name is the parent directory
            if (Path.GetFileName(filePath).Equals("entity.json", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileName(Path.GetDirectoryName(filePath));
            }

            // For other JSON files, use the filename without extension
            return Path.GetFileNameWithoutExtension(filePath);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error extracting endpoint name from {Path}", filePath);
        }

        return null;
    }

    private string? ExtractNamespace(string filePath)
    {
        try
        {
            if (!Path.GetFileName(filePath).Equals("entity.json", StringComparison.OrdinalIgnoreCase))
                return null;

            // .../endpoints/{Type}/{Namespace?}/{EndpointName}/entity.json
            var endpointDir  = Path.GetDirectoryName(filePath);          // EndpointName dir
            var namespaceDir = Path.GetDirectoryName(endpointDir);       // Namespace or Type dir
            var namespaceName = Path.GetFileName(namespaceDir);

            return namespaceName == null || _typeFolderNames.Contains(namespaceName) ? null : namespaceName;
        }
        catch
        {
            return null;
        }
    }
}
