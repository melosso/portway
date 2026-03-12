namespace PortwayApi.Services;

using System.Runtime.CompilerServices;
using System.Threading.Channels;

/// <summary>
/// Fan-out broadcaster for Server-Sent Events.
/// Each connected SSE client gets its own bounded channel.
/// If a client is slow, old events are silently dropped so it'll n ever blocks the broadcaster.
/// </summary>
public sealed class SseBroadcaster : IDisposable
{
    private readonly List<Channel<string>> _channels = [];
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Returns an async sequence of SSE-formatted strings for one client.
    /// The channel is automatically removed when the client disconnects.
    /// </summary>
    public IAsyncEnumerable<string> SubscribeAsync(CancellationToken ct)
    {
        var ch = Channel.CreateBounded<string>(new BoundedChannelOptions(32)
        {
            FullMode    = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
        lock (_lock) _channels.Add(ch);
        return ReadAndRemove(ch, ct);
    }

    private async IAsyncEnumerable<string> ReadAndRemove(
        Channel<string> ch,
        [EnumeratorCancellation] CancellationToken ct)
    {
        try
        {
            await foreach (var msg in ch.Reader.ReadAllAsync(ct))
                yield return msg;
        }
        finally
        {
            lock (_lock) _channels.Remove(ch);
        }
    }

    /// <summary>Sends an SSE event to every currently connected client.</summary>
    public void Broadcast(string eventType, string json)
    {
        if (_disposed) return;
        lock (_lock)
            foreach (var ch in _channels)
                ch.Writer.TryWrite($"event: {eventType}\ndata: {json}\n\n");
    }

    public void Dispose()
    {
        _disposed = true;
        lock (_lock)
        {
            foreach (var ch in _channels)
                ch.Writer.TryComplete();
            _channels.Clear();
        }
    }
}
