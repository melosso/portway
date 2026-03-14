using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PortwayApi.Classes;
using PortwayApi.Interfaces;
using PortwayApi.Services.Providers;
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
    private readonly IEnvironmentSettingsProvider? _environmentSettingsProvider;
    private readonly EnvironmentSettings? _environmentSettings;
    private readonly ISqlProviderFactory? _sqlProviderFactory;

    public HealthCheckService(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService healthCheckService,
        TimeSpan cacheTime,
        IHttpClientFactory? httpClientFactory = null,
        IEnvironmentSettingsProvider? environmentSettingsProvider = null,
        EnvironmentSettings? environmentSettings = null,
        ISqlProviderFactory? sqlProviderFactory = null)
    {
        _healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
        _cacheTime = cacheTime;
        _httpClientFactory = httpClientFactory;
        _environmentSettingsProvider = environmentSettingsProvider;
        _environmentSettings = environmentSettings;
        _sqlProviderFactory = sqlProviderFactory;
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

        // Non-blocking try: if a background refresh is already holding the lock, return the
        // stale cache immediately so the caller (and the UI) is never blocked.
        if (!await _lock.WaitAsync(0, cancellationToken))
        {
            return _cachedReport ?? CreateCheckingReport();
        }

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

            if (_httpClientFactory != null)
            {
                entries["ProxyEndpoints"] = await CheckProxyEndpointsAsync(cancellationToken);
            }

            if (_environmentSettingsProvider != null && _environmentSettings != null)
            {
                entries["SqlConnectivity"] = await CheckSqlConnectivityAsync(cancellationToken);
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

    private static HealthReport CreateCheckingReport() => new(
        new Dictionary<string, HealthReportEntry>
        {
            ["Status"] = new(HealthStatus.Healthy, "Health check initialising…", TimeSpan.Zero, null, null)
        },
        HealthStatus.Healthy,
        TimeSpan.Zero);

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
                HealthStatus.Unhealthy => $"Health check status: Critical: Only {percentFreeRounded:F0}% disk space remaining",
                HealthStatus.Degraded => $"Health check status: Low disk space: {percentFreeRounded:F0}% remaining",
                _ => $"Health check status: Disk space: {percentFreeRounded:F0}% remaining"
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
            // Lazy load endpoints - ensures we always check current endpoint definitions
            var proxyEndpoints = EndpointHandler.GetProxyEndpoints();
            var endpointsToCheck = proxyEndpoints
                .Where(e => !e.Value.IsPrivate && e.Value.Methods.Contains("GET"))
                .OrderBy(_ => Guid.NewGuid())
                .Take(3)
                .Select(e => new KeyValuePair<string, (string Url, HashSet<string> Methods, bool IsPrivate, string Type)>(
                    e.Key,
                    (e.Value.Url, new HashSet<string>(e.Value.Methods), e.Value.IsPrivate, e.Value.Type.ToString())))
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
                ? "Health check status: All proxy services are responding" 
                : "Health check status: One or more proxy services are not responding properly";

            if (unhealthyEndpoints.Any())
            {
                Log.Warning("Health check status: Unhealthy proxy endpoints detected ({UnhealthyEndpoints})", 
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

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
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
            var errorMsg = ex.InnerException?.Message ?? ex.Message;
            Log.Error("Error checking endpoint {Endpoint} ({ExceptionType}: {ErrorMessage}). URL: {Url}",
                endpoint.Key, ex.GetType().Name, errorMsg, endpoint.Value.Url);
            results[endpoint.Key] = new
            {
                Status = "Unhealthy",
                Error = errorMsg
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

    private async Task<HealthReportEntry> CheckSqlConnectivityAsync(CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var results = new Dictionary<string, object>();
        var unhealthyEnvironments = new List<string>();

        var environments = _environmentSettings!.AllowedEnvironments;

        if (!environments.Any())
        {
            return new HealthReportEntry(
                HealthStatus.Healthy,
                "No SQL environments configured",
                DateTime.UtcNow - startTime,
                null,
                results,
                new[] { "sql", "database", "readiness" }
            );
        }

        await Task.WhenAll(environments.Select(async env =>
        {
            try
            {
                var (connectionString, _, _) = await _environmentSettingsProvider!.LoadEnvironmentOrThrowAsync(env);

                if (string.IsNullOrEmpty(connectionString))
                {
                    Log.Debug("Skipping SQL health check for environment {Environment}: no connection string configured", env);
                    lock (results) results[env] = new { Status = "NotConfigured", Note = "No SQL connection string" };
                    return;
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

                DbConnection connection;
                string healthQuery;
                if (_sqlProviderFactory != null)
                {
                    var provider = _sqlProviderFactory.GetProvider(connectionString);
                    connection = provider.CreateConnection(connectionString);
                    healthQuery = provider.HealthCheckQuery;
                }
                else
                {
                    connection = new SqlConnection(connectionString);
                    healthQuery = "SELECT 1";
                }

                await using (connection)
                {
                    await connection.OpenAsync(linkedCts.Token);

                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = healthQuery;
                    cmd.CommandTimeout = 5;
                    await cmd.ExecuteScalarAsync(linkedCts.Token);
                }

                lock (results) results[env] = new { Status = "Healthy" };
                Log.Debug("SQL connectivity check passed for environment: {Environment}", env);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                const string timeoutMsg = "Connection timed out after 5 seconds";
                Log.Error("SQL connectivity check failed for environment {Environment}: {Error}", env, timeoutMsg);
                lock (results) results[env] = new { Status = "Unhealthy", Error = timeoutMsg };
                lock (unhealthyEnvironments) unhealthyEnvironments.Add(env);
            }
            catch (Exception ex)
            {
                var errorMsg = ex is SqlException sql
                    ? $"SQL error {sql.Number}: {sql.Message}"
                    : ex is DbException dbEx
                        ? $"DB error: {dbEx.Message}"
                        : ex.InnerException?.Message ?? ex.Message;
                Log.Error("SQL connectivity check failed for environment {Environment}: {Error}", env, errorMsg);
                lock (results) results[env] = new { Status = "Unhealthy", Error = errorMsg };
                lock (unhealthyEnvironments) unhealthyEnvironments.Add(env);
            }
        }));

        var status = unhealthyEnvironments.Any() ? HealthStatus.Unhealthy : HealthStatus.Healthy;
        var description = status == HealthStatus.Healthy
            ? "All SQL environments are reachable"
            : $"SQL connectivity failed for: {string.Join(", ", unhealthyEnvironments)}";

        if (unhealthyEnvironments.Any())
            Log.Warning("Health check status: Unhealthy SQL environments detected ({Environments})", string.Join(", ", unhealthyEnvironments));

        return new HealthReportEntry(
            status,
            description,
            DateTime.UtcNow - startTime,
            null,
            results,
            new[] { "sql", "database", "readiness" }
        );
    }
}