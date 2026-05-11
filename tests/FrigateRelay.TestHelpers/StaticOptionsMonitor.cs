using Microsoft.Extensions.Options;

namespace FrigateRelay.TestHelpers;

/// <summary>
/// Test-double <see cref="IOptionsMonitor{TOptions}"/> that returns a single fixed value
/// and never raises change notifications. Replaces six per-file copies previously
/// scattered across the Host test suite as <c>StaticMonitor&lt;T&gt;</c> /
/// <c>StaticOptionsMonitor&lt;T&gt;</c> (CodeRabbit PR #46 dedupe).
/// </summary>
public sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; } = value;
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
