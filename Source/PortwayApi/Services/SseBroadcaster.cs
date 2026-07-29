namespace PortwayApi.Services;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

/// <summary>Fan-out broadcaster for Server-Sent Events. Each connected SSE client gets its own bounded channel. If a client is slow, old events are silently dropped so it'll n ever blocks the broadcaster</summary>
public sealed class SseBroadcaster : IDisposable
{
    private readonly ConcurrentDictionary<Channel<string>, byte> _channels = new();
    private volatile bool _disposed;

    /// <summary>Returns an async sequence of SSE-formatted strings for one client. The channel is automatically removed when the client disconnects</summary>
    public IAsyncEnumerable<string> SubscribeAsync(CancellationToken ct)
    {
        var ch = Channel.CreateBounded<string>(new BoundedChannelOptions(32)
        {
            FullMode    = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
        _channels[ch] = 0;
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
            _channels.TryRemove(ch, out _);
        }
    }

    /// <summary>Sends an SSE event to every currently connected client.</summary>
    public void Broadcast(string eventType, string json)
    {
        if (_disposed) return;

        foreach (var ch in _channels.Keys)
            ch.Writer.TryWrite($"event: {eventType}\ndata: {json}\n\n");
    }

    public void Dispose()
    {
        _disposed = true;

        foreach (var ch in _channels.Keys)
            ch.Writer.TryComplete();

        _channels.Clear();
    }
}
