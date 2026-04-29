using FrigateRelay.Abstractions;
using FrigateRelay.Host.Configuration;
using Microsoft.Extensions.Caching.Memory;

namespace FrigateRelay.Host.Matching;

/// <summary>
/// Prevents repeated firing of the same (subscription, camera, label) combination within the
/// configured cooldown window. Each matching subscription has its own independent dedupe bucket,
/// so two subscriptions that match the same event each have their own TTL — consistent with D1.
/// </summary>
/// <remarks>
/// <para>
/// Key shape: <c>"{sub.Name}|{ctx.Camera}|{ctx.Label}"</c> (lowercased). The event-id is
/// intentionally excluded: dedupe is per detection-class (camera + label), not per event
/// instance. Multiple <c>update</c> messages for the same object are thus correctly suppressed
/// within the cooldown window.
/// </para>
/// <para>
/// Callers should pre-filter the subscription list using <see cref="SubscriptionMatcher.Match"/>
/// before calling <see cref="TryEnter"/>.
/// </para>
/// <para>
/// The injected <see cref="IMemoryCache"/> should be a dedicated instance (keyed singleton
/// registered as <c>"frigate-mqtt"</c> by PLAN-3.1). Using a shared/global cache would cause
/// key-space collisions across plugins.
/// </para>
/// </remarks>
internal sealed class DedupeCache
{
    private readonly IMemoryCache _cache;
    private readonly object _writeLock = new();

    /// <summary>
    /// Initialises a new <see cref="DedupeCache"/> backed by the supplied <paramref name="cache"/>.
    /// </summary>
    /// <param name="cache">
    /// A dedicated <see cref="IMemoryCache"/> instance. In production this is registered as a
    /// keyed singleton (<c>"frigate-mqtt"</c>) by PLAN-3.1. In tests, pass
    /// <c>new MemoryCache(new MemoryCacheOptions())</c> directly.
    /// </param>
    public DedupeCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// Returns <see langword="true"/> if the (subscription, camera, label) combination is not
    /// currently in the cooldown window, and inserts an entry with the subscription's TTL so
    /// subsequent calls within the window return <see langword="false"/>.
    /// Returns <see langword="false"/> when the entry already exists (i.e. within the cooldown).
    /// <para>
    /// Atomic against concurrent callers: the check-and-insert pair runs under a single lock so
    /// two concurrent events for the same (sub, camera, label) cannot both see a cache miss and
    /// both return <see langword="true"/>.
    /// </para>
    /// <para>
    /// Why a lock and not a lock-free path: <c>IMemoryCache.GetOrCreate</c> is documented as
    /// non-atomic (the factory can run twice for concurrent misses on the same key), and
    /// <c>TryGetValue</c> + <c>Set</c> is a TOCTOU race. The two lock-free alternatives
    /// (per-key <c>SemaphoreSlim</c> in a <c>ConcurrentDictionary</c>, or a separate
    /// <c>ConcurrentDictionary&lt;string, DateTimeOffset&gt;</c> for cooldown tracking) both
    /// add complexity that doesn't pay off at the actual workload: a busy NVR sees a few
    /// events/second at peak, the critical section is a hash lookup plus a cache write, and
    /// contention is dominated by IMemoryCache's own internal synchronization either way.
    /// </para>
    /// </summary>
    /// <param name="sub">The matching subscription whose <see cref="SubscriptionOptions.CooldownSeconds"/> sets the TTL.</param>
    /// <param name="ctx">The event context providing <c>Camera</c> and <c>Label</c> for the key.</param>
    public bool TryEnter(SubscriptionOptions sub, EventContext ctx)
    {
        // CooldownSeconds <= 0 means "no dedupe": every event passes through.
        // Avoids ArgumentOutOfRangeException from MemoryCache's TimeSpan.Zero rejection
        // (Phase 9 simplifier finding — was a sharp edge in test fixtures).
        if (sub.CooldownSeconds <= 0)
            return true;

        var key = $"{sub.Name}|{ctx.Camera}|{ctx.Label}".ToLowerInvariant();

        lock (_writeLock)
        {
            if (_cache.TryGetValue(key, out _))
                return false;

            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(sub.CooldownSeconds),
            };
            _cache.Set(key, true, options);
            return true;
        }
    }
}
