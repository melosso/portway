namespace PortwayApi.Tests.Support;

using Microsoft.Extensions.Options;

/// <summary>IOptionsMonitor over a fixed value, for services that only read CurrentValue</summary>
public sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; } = value;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
