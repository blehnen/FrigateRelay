using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FrigateRelay.Host.Dispatch;

/// <summary>
/// Shared observability primitives for the dispatcher pipeline.
/// All telemetry in FrigateRelay uses the single <c>"FrigateRelay"</c> meter and activity source
/// per the CLAUDE.md observability invariant (<c>frigaterelay.*</c> counter prefix).
/// </summary>
internal static class DispatcherDiagnostics
{
    /// <summary>
    /// The shared meter for all FrigateRelay metrics. Named <c>"FrigateRelay"</c> per CLAUDE.md.
    /// </summary>
    internal static readonly Meter Meter = new("FrigateRelay");

    /// <summary>
    /// The shared activity source for FrigateRelay distributed tracing. Named <c>"FrigateRelay"</c> per CLAUDE.md.
    /// </summary>
    internal static readonly ActivitySource ActivitySource = new("FrigateRelay");

    /// <summary>
    /// Incremented when a <see cref="DispatchItem"/> is evicted from a full channel (drop-oldest).
    /// Tagged with <c>action</c> (plugin name) at emit time.
    /// </summary>
    internal static readonly Counter<long> Drops =
        Meter.CreateCounter<long>("frigaterelay.dispatch.drops");

    /// <summary>
    /// Incremented when the Polly resilience pipeline exhausts all retries for a plugin invocation.
    /// Tagged with <c>action</c> (plugin name) at emit time.
    /// Emitted by PLAN-2.1 when the retry pipeline surfaces its final exception; declared here
    /// for shared ownership alongside <see cref="Drops"/>.
    /// </summary>
    internal static readonly Counter<long> Exhausted =
        Meter.CreateCounter<long>("frigaterelay.dispatch.exhausted");
}
