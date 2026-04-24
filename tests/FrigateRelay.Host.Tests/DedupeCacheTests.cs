using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Host.Configuration;
using FrigateRelay.Host.Matching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Host.Tests;

[TestClass]
public sealed class DedupeCacheTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static MemoryCache MakeCache() =>
        new(new MemoryCacheOptions());

    private static EventContext MakeContext(string camera = "front_door", string label = "person") =>
        new()
        {
            EventId = "test-event-id",
            Camera = camera,
            Label = label,
            Zones = Array.Empty<string>(),
            StartedAt = DateTimeOffset.UtcNow,
            RawPayload = "{}",
            SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null),
        };

    private static SubscriptionOptions MakeSub(string name = "alert", int cooldownSeconds = 60) =>
        new()
        {
            Name = name,
            Camera = "front_door",
            Label = "person",
            CooldownSeconds = cooldownSeconds,
        };

    // ---------------------------------------------------------------------------
    // Test 1: first call (cache miss) returns true
    // ---------------------------------------------------------------------------

    [TestMethod]
    public void TryEnter_FirstCall_ReturnsTrueOnMiss()
    {
        var cache = new DedupeCache(MakeCache());
        var sub = MakeSub();
        var ctx = MakeContext();

        var result = cache.TryEnter(sub, ctx);

        result.Should().BeTrue("first call for a new (sub, camera, label) triple must be a cache miss");
    }

    // ---------------------------------------------------------------------------
    // Test 2: immediate second call (cache hit) returns false
    // ---------------------------------------------------------------------------

    [TestMethod]
    public void TryEnter_SecondCallWithinCooldown_ReturnsFalseOnHit()
    {
        var cache = new DedupeCache(MakeCache());
        var sub = MakeSub();
        var ctx = MakeContext();

        var first = cache.TryEnter(sub, ctx);
        var second = cache.TryEnter(sub, ctx);

        first.Should().BeTrue("first call must succeed");
        second.Should().BeFalse("second call within cooldown window must be suppressed (cache hit)");
    }

    // ---------------------------------------------------------------------------
    // Test 3: after TTL expiry, next call returns true again
    // ---------------------------------------------------------------------------

    [TestMethod]
    public async Task TryEnter_AfterTtlExpiry_ReturnsTrueAgain()
    {
        // Use a 1-second cooldown so the test can verify expiry without a long wait.
        var cache = new DedupeCache(MakeCache());
        var sub = MakeSub(cooldownSeconds: 1);
        var ctx = MakeContext();

        var first = cache.TryEnter(sub, ctx);
        first.Should().BeTrue("first call must succeed");

        // Wait for the 1-second TTL to expire with a small buffer.
        await Task.Delay(TimeSpan.FromMilliseconds(1200));

        var afterExpiry = cache.TryEnter(sub, ctx);
        afterExpiry.Should().BeTrue("after the cooldown TTL expires the entry must be gone and TryEnter must return true");
    }
}
