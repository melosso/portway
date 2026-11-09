namespace PortwayApi.Auth;

using System.Threading.Channels;
using Serilog;

/// <summary>
/// Batched auth failure audit writer.
/// </summary>
public sealed class AuthFailureAuditQueue : BackgroundService
{
    public const string FailedAuth = "FailedAuth";
    public const string AuthorizationFailed = "AuthorizationFailed";
    internal const int BatchSize = 50;
    internal static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopes;
    private readonly Channel<AuthTokenAudit> _channel;
    private long _dropped;

    public AuthFailureAuditQueue(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
        _channel = Channel.CreateBounded<AuthTokenAudit>(
            new BoundedChannelOptions(BatchSize * 10) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true },
            _ => Interlocked.Increment(ref _dropped));
    }

    public void Enqueue(AuthTokenAudit audit) => _channel.Writer.TryWrite(audit);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<AuthTokenAudit>(BatchSize);
        try
        {
            while (await _channel.Reader.WaitToReadAsync(stoppingToken))
            {
                await WriteBatchAsync(batch, stoppingToken);
                await Task.Delay(FlushInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await WriteBatchAsync(batch, CancellationToken.None);
        }
    }

    private async Task WriteBatchAsync(List<AuthTokenAudit> batch, CancellationToken ct)
    {
        batch.Clear();
        while (batch.Count < BatchSize && _channel.Reader.TryRead(out var audit))
            batch.Add(audit);

        var dropped = Interlocked.Exchange(ref _dropped, 0);
        if (dropped > 0)
            Log.Warning("Dropped {Count} auth failure audits over the limit of {Limit} per {Interval}", dropped, BatchSize, FlushInterval);

        if (batch.Count == 0)
            return;

        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            db.TokenAudits.AddRange(batch);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "Failed to write {Count} auth failure audits", batch.Count);
        }
    }
}
