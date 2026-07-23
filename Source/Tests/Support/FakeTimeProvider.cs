namespace PortwayApi.Tests.Support;

/// <summary>Manually advanced clock for deterministic rate limit tests</summary>
public class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}
