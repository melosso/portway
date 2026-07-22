namespace PortwayApi.Endpoints;

using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using Serilog;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PortwayApi.Auth;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Interfaces;
using PortwayApi.Services;


public static partial class WebUiEndpointExtensions
{
    private static void MapDiagnosticsRoutes(WebApplication app)
    {
        app.MapPost("/ui/api/client-error", async (HttpContext context) =>
        {
            try
            {
                var body = await new System.IO.StreamReader(context.Request.Body).ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(body) && body.Length < 4096)
                {
                    Log.Warning("Client-side JS error from {IP}: {Body}",
                        context.Connection.RemoteIpAddress, body);
                }
            }
            catch { /* never let client errors throw */ }
            return Results.Ok();
        }).ExcludeFromDescription();

        app.MapGet("/ui/api/logs", async (HttpRequest request) =>
        {
            var limit       = int.TryParse(request.Query["limit"], out var l) ? Math.Min(l, 2000) : 200;
            var offset      = int.TryParse(request.Query["offset"], out var o) ? Math.Max(0, o) : 0;
            var filterLevel = (request.Query["level"].ToString() ?? "").ToUpperInvariant();
            try
            {
                var logDir = Path.Combine(Directory.GetCurrentDirectory(), "log");
                if (!Directory.Exists(logDir))
                    return Results.Json(new { file = "", lines = Array.Empty<object>(), total = 0, has_more = false });

                var logFiles = Directory.GetFiles(logDir, "portwayapi-*.log")
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .Take(3)
                    .ToList();

                if (logFiles.Count == 0)
                    return Results.Json(new { file = "", lines = Array.Empty<object>(), total = 0, has_more = false });

                // Serilog default file output template:
                // {Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}
                var logPattern = new System.Text.RegularExpressions.Regex(
                    @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[(\w{3,})\] (.*)$");

                var allEntries = new List<(string Timestamp, string Level, string Message)>();
                string? latestFile = null;

                foreach (var file in logFiles)
                {
                    try
                    {
                        string content;
                        // FileShare.ReadWrite so reads succeed while Serilog has the file open (buffered sink)
                        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var sr = new StreamReader(stream))
                            content = await sr.ReadToEndAsync();

                        var rawLines = content.Split('\n');
                        string? ts = null, lvl = null;
                        var msgParts = new List<string>();

                        foreach (var rawLine in rawLines)
                        {
                            var m = logPattern.Match(rawLine);
                            if (m.Success)
                            {
                                if (ts != null)
                                    allEntries.Add((ts, lvl ?? "INF", string.Join("\n", msgParts).TrimEnd()));
                                ts       = m.Groups[1].Value;
                                lvl      = m.Groups[2].Value.ToUpperInvariant();
                                msgParts = [m.Groups[3].Value];
                            }
                            else if (ts != null && !string.IsNullOrWhiteSpace(rawLine))
                            {
                                msgParts.Add(rawLine.TrimEnd());
                            }
                        }
                        if (ts != null)
                            allEntries.Add((ts, lvl ?? "INF", string.Join("\n", msgParts).TrimEnd()));

                        latestFile ??= file;
                        if (allEntries.Count >= limit * 5) break;
                    }
                    catch (Exception ex) { Log.Debug(ex, "Skipping inaccessible log file: {File}", file); }
                }

                // Newest first, sort by timestamp string (ISO format is lexicographically comparable)
                allEntries.Sort((a, b) => string.CompareOrdinal(b.Timestamp, a.Timestamp));

                var filtered = string.IsNullOrEmpty(filterLevel) || filterLevel == "ALL"
                    ? allEntries
                    : allEntries.Where(e => e.Level.StartsWith(
                        filterLevel[..Math.Min(3, filterLevel.Length)],
                        StringComparison.OrdinalIgnoreCase)).ToList();

                var hasMore = offset + limit < filtered.Count;
                var paged   = filtered.Skip(offset).Take(limit)
                    .Select(e => new { timestamp = e.Timestamp, level = e.Level, message = e.Message });

                return Results.Json(new { file = latestFile != null ? Path.GetFileName(latestFile) : "", lines = paged, total = filtered.Count, has_more = hasMore });
            }
            catch (Exception ex)
            {
                return Results.Json(new
                {
                    file     = "",
                    lines    = new[] { new { timestamp = "", level = "ERR", message = ex.Message } },
                    total    = 0,
                    has_more = false
                });
            }
        }).ExcludeFromDescription();

        app.MapGet("/ui/api/metrics", (HttpRequest request) =>
        {
            var period   = request.Query["period"].ToString();
            if (period != "7d" && period != "30d") period = "24h";
            var metrics  = app.Services.GetRequiredService<PortwayApi.Services.MetricsService>();
            var snapshot   = metrics.GetSnapshot(period);
            var cacheTotal = snapshot.CacheHits + snapshot.CacheMisses;
            static object BucketDto(TrafficBucket b) => new { label = b.Label, timestamp = b.Timestamp, count = b.Count };
            return Results.Json(new
            {
                period              = snapshot.Period,
                api_traffic         = snapshot.ApiTraffic.Select(BucketDto),
                ui_traffic          = snapshot.UiTraffic.Select(BucketDto),
                errors              = snapshot.Errors,
                total               = snapshot.Total,
                error_rate          = snapshot.ErrorRate,
                collecting_for_secs = snapshot.CollectingForSeconds,
                api_requests        = snapshot.ApiRequests,
                ui_requests         = snapshot.UiRequests,
                top_endpoints       = snapshot.TopEndpoints.Select(e => new { name = e.Name, count = e.Count }),
                cache = new
                {
                    hits     = snapshot.CacheHits,
                    misses   = snapshot.CacheMisses,
                    hit_rate = cacheTotal > 0 ? Math.Round((double)snapshot.CacheHits / cacheTotal, 4) : (double?)null
                }
            });
        }).ExcludeFromDescription();

    }
}
