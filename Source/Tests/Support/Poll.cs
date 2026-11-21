using System.Diagnostics;

namespace PortwayApi.Tests.Support;

/// <summary>
/// Waits for state written by background work: file watchers, hosted services, reconnecting clients
/// </summary>
public static class Poll
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

    public static Task<bool> UntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken cancellationToken) =>
        UntilAsync(() => Task.FromResult(condition()), timeout, cancellationToken);

    public static async Task<bool> UntilAsync(Func<Task<bool>> condition, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        using var timer = new PeriodicTimer(Interval);
        while (!await condition())
        {
            if (Stopwatch.GetElapsedTime(started) >= timeout)
                return false;
            await timer.WaitForNextTickAsync(cancellationToken);
        }
        return true;
    }
}
