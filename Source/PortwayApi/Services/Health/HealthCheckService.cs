using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;

namespace PortwayApi.Services;

/// <summary>
/// Service that caches health check results to avoid frequent executions
/// </summary>
public class HealthCheckService
{
    private readonly Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService _healthCheckService;
    private readonly TimeSpan _cacheTime;
    private DateTime _lastCheckTime = DateTime.MinValue;
    private HealthReport? _cachedReport;
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly Dictionary<string, (string Url, HashSet<string> Methods, bool IsPrivate, string Type)>? _endpointMap;

    public HealthCheckService(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService healthCheckService, 
        TimeSpan cacheTime, 
        IHttpClientFactory? httpClientFactory = null, 
        Dictionary<string, (string Url, HashSet<string> Methods, bool IsPrivate, string Type)>? endpointMap = null)
    {
        _healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
        _cacheTime = cacheTime;
        _httpClientFactory = httpClientFactory;
        _endpointMap = endpointMap;
    }

    /// <summary>
    /// Gets a health report, using a cached version if available and not expired
    /// </summary>
    public async Task<HealthReport> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (IsCacheValid())
        {
            return _cachedReport!;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (IsCacheValid())
            {
                return _cachedReport!;
            }

            var baseReport = await _healthCheckService.CheckHealthAsync(cancellationToken);
            var entries = new Dictionary<string, HealthReportEntry>(baseReport.Entries)
            {
                ["Diskspace"] = CheckDiskSpace()
            };

            if (_httpClientFactory != null && _endpointMap != null)
            {
                entries["ProxyEndpoints"] = await CheckProxyEndpointsAsync(cancellationToken);
            }

            var status = DetermineOverallStatus(entries.Values);
            _cachedReport = new HealthReport(entries, status, baseReport.TotalDuration);
            _lastCheckTime = DateTime.UtcNow;

            Log.Debug("Health check cache refreshed. Status: {Status}", status);

            return _cachedReport;
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool IsCacheValid()
    {
        return _cachedReport != null && (DateTime.UtcNow - _lastCheckTime) < _cacheTime;
    }

    private HealthStatus DetermineOverallStatus(IEnumerable<HealthReportEntry> entries)
    {
        var status = HealthStatus.Healthy;

        foreach (var entry in entries)
        {
            if (entry.Status == HealthStatus.Unhealthy)
            {
                Log.Warning("Health check failed: {Description}", entry.Description);
                return HealthStatus.Unhealthy;
            }

            if (entry.Status == HealthStatus.Degraded && status == HealthStatus.Healthy)
            {
                status = HealthStatus.Degraded;
            }
        }

        return status;
    }

    private HealthReportEntry CheckDiskSpace()
    {
        try
        {
            var startTime = DateTime.UtcNow;
            var driveInfo = new DriveInfo(Path.GetPathRoot(Directory.GetCurrentDirectory())!);
            var totalSpaceGB = driveInfo.TotalSize / 1_073_741_824.0; // Convert bytes to GB
            var freeSpaceGB = driveInfo.AvailableFreeSpace / 1_073_741_824.0;
            var percentFree = (freeSpaceGB / totalSpaceGB) * 100;
            var percentFreeRounded = Math.Round(percentFree / 5) * 5;

            var status = percentFreeRounded switch
            {
                <= 5 => HealthStatus.Unhealthy,
                <= 15 => HealthStatus.Degraded,
                _ => HealthStatus.Healthy
            };

            var description = status switch
            {
                HealthStatus.Unhealthy => $"Critical: Only {percentFreeRounded:F0}% disk space remaining",
                HealthStatus.Degraded => $"Low disk space: {percentFreeRounded:F0}% remaining",
                _ => $"Disk space: {percentFreeRounded:F0}% remaining"
            };

            return new HealthReportEntry(
                status, 
                description, 
                DateTime.UtcNow - startTime, 
                null, 
                new Dictionary<string, object> { ["PercentFree"] = $"{percentFreeRounded:F0}%" },
                new[] { "storage", "system" }
            );
        }
        catch (Exception ex)
        {
            return new HealthReportEntry(
                HealthStatus.Unhealthy, 
                $"Error checking disk space: {ex.Message}", 
                TimeSpan.Zero, 
                ex, 
                null, 
                new[] { "storage", "system" }
            );
        }
    }

    private async Task<HealthReportEntry> CheckProxyEndpointsAsync(CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var results = new Dictionary<string, object>();
        var status = HealthStatus.Healthy;

        var handler = new HttpClientHandler
        {
            UseDefaultCredentials = true,
            PreAuthenticate = true
        };
        if (_httpClientFactory != null)
        {
            handler.UseProxy = true;
            handler.UseCookies = false;
        }
        var client = new HttpClient(handler);
        var unhealthyEndpoints = new List<string>();

        try
        {
            var endpointsToCheck = _endpointMap!
                .Where(e => !e.Value.IsPrivate && e.Value.Methods.Contains("GET"))
                .OrderBy(_ => Guid.NewGuid())
                .Take(3)
                .ToList();

            Log.Debug("Checking proxy endpoints: {Endpoints}", 
                string.Join(", ", endpointsToCheck.Select(e => $"{e.Key} ({e.Value.Url})")));

            if (!endpointsToCheck.Any())
            {
                return CreateHealthReportEntry(
                    HealthStatus.Healthy, 
                    "No endpoints configured for health check", 
                    startTime, 
                    results
                );
            }

            foreach (var endpoint in endpointsToCheck)
            {
                await CheckEndpointAsync(client, endpoint, results, unhealthyEndpoints, cancellationToken);
            }

            status = unhealthyEndpoints.Any() ? HealthStatus.Unhealthy : HealthStatus.Healthy;
            var description = status == HealthStatus.Healthy 
                ? "All proxy services are responding" 
                : "One or more proxy services are not responding properly";

            if (unhealthyEndpoints.Any())
            {
                Log.Warning("Unhealthy proxy endpoints detected: {UnhealthyEndpoints}", 
                    string.Join(", ", unhealthyEndpoints));
            }

            return CreateHealthReportEntry(status, description, startTime, results);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking proxy endpoints");
            return CreateHealthReportEntry(
                HealthStatus.Unhealthy, 
                $"Error checking proxy endpoints: {ex.Message}", 
                startTime, 
                results, 
                ex
            );
        }
    }

    private async Task CheckEndpointAsync(
        HttpClient client, 
        KeyValuePair<string, (string Url, HashSet<string> Methods, bool IsPrivate, string Type)> endpoint, 
        Dictionary<string, object> results, 
        List<string> unhealthyEndpoints, 
        CancellationToken cancellationToken)
    {
        try
        {
            var url = endpoint.Value.Url;
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            // Add headers
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            Log.Debug("Sending request to endpoint {Endpoint} with URL: {Url}", endpoint.Key, url);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

            // Send the request and capture the first response
            var response = await client.SendAsync(request, linkedCts.Token);

            Log.Debug("Received response from endpoint {Endpoint}. StatusCode: {StatusCode}, ReasonPhrase: {ReasonPhrase}", 
                endpoint.Key, (int)response.StatusCode, response.ReasonPhrase);

            // If the first response is 401, treat it as healthy and stop further processing
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                results[endpoint.Key] = new 
                { 
                    Status = "Healthy", 
                    StatusCode = 401, 
                    ReasonPhrase = "Unauthorized - Marked as Healthy" 
                };
                return;
            }

            // Process subsequent responses if the first response is not 401
            var content = await response.Content.ReadAsStringAsync();
            var isHealthy = (int)response.StatusCode >= 200 && (int)response.StatusCode <= 299 
                            || content.Contains("Failed to login to Globe");

            if (!isHealthy)
            {
                if (response.Content.Headers.ContentType?.MediaType == "application/xml" && content.Contains("<m:error"))
                {
                    Log.Warning("Endpoint {Endpoint} returned an XML error response: {Content}", endpoint.Key, content);
                }

                Log.Warning("Endpoint {Endpoint} returned status code {StatusCode} ({ReasonPhrase})", 
                    endpoint.Key, (int)response.StatusCode, response.ReasonPhrase);
                unhealthyEndpoints.Add(endpoint.Key);
            }

            results[endpoint.Key] = new 
            { 
                Status = content.Contains("Failed to login to Globe") ? "Healthy" : (isHealthy ? "Healthy" : "Unhealthy"),
                StatusCode = content.Contains("Failed to login to Globe") ? 401 : (int)response.StatusCode,
                ReasonPhrase = content.Contains("Failed to login to Globe") 
                    ? "Failed to login to Globe - Marked as Healthy" 
                    : response.ReasonPhrase
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking endpoint {Endpoint}. URL: {Url}", endpoint.Key, endpoint.Value.Url);
            results[endpoint.Key] = new 
            { 
                Status = "Unhealthy", 
                Error = ex.Message 
            };
            unhealthyEndpoints.Add(endpoint.Key);
        }
    }

    private HealthReportEntry CreateHealthReportEntry(
        HealthStatus status, 
        string description, 
        DateTime startTime, 
        Dictionary<string, object> results, 
        Exception? exception = null)
    {
        return new HealthReportEntry(
            status, 
            description, 
            DateTime.UtcNow - startTime, 
            exception, 
            results, 
            new[] { "proxies", "external", "readiness" }
        );
    }
}