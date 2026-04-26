using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Host.Snapshots;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace FrigateRelay.Host.Tests.Snapshots;

[TestClass]
public sealed class SnapshotResolverTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static EventContext MakeContext(string eventId = "evt-1") => new()
    {
        EventId = eventId,
        Camera = "front",
        Label = "person",
        Zones = [],
        StartedAt = DateTimeOffset.UtcNow,
        RawPayload = "{}",
        SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null),
    };

    private static SnapshotResult MakeResult(string providerName) => new()
    {
        Bytes = [0xFF, 0xD8],
        ContentType = "image/jpeg",
        ProviderName = providerName,
    };

    private static ISnapshotProvider MakeProvider(string name, SnapshotResult? result = null)
    {
        var provider = Substitute.For<ISnapshotProvider>();
        provider.Name.Returns(name);
        var returnValue = result ?? MakeResult(name);
        provider.FetchAsync(Arg.Any<SnapshotRequest>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<SnapshotResult?>(returnValue));
        return provider;
    }

    private static SnapshotResolver MakeResolver(
        IEnumerable<ISnapshotProvider> providers,
        string? defaultProviderName,
        IMemoryCache? cache = null,
        ILogger<SnapshotResolver>? logger = null)
    {
        var options = Options.Create(new SnapshotResolverOptions
        {
            DefaultProviderName = defaultProviderName,
            CacheSlidingTtl = TimeSpan.FromSeconds(10),
        });
        return new SnapshotResolver(
            providers,
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            options,
            logger ?? new CapturingLogger<SnapshotResolver>());
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [TestMethod]
    public async Task PerAction_OverridesSubscription_OverridesGlobal()
    {
        var providerA = MakeProvider("A");
        var providerB = MakeProvider("B");
        var providerC = MakeProvider("C");
        var resolver = MakeResolver([providerA, providerB, providerC], defaultProviderName: "C");
        var ctx = MakeContext();

        var result = await resolver.ResolveAsync(ctx, perActionProviderName: "A", subscriptionDefaultProviderName: "B", CancellationToken.None);

        result.Should().NotBeNull();
        await providerA.Received(1).FetchAsync(Arg.Any<SnapshotRequest>(), Arg.Any<CancellationToken>());
        await providerB.DidNotReceive().FetchAsync(Arg.Any<SnapshotRequest>(), Arg.Any<CancellationToken>());
        await providerC.DidNotReceive().FetchAsync(Arg.Any<SnapshotRequest>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Subscription_Default_WinsOverGlobal()
    {
        var providerB = MakeProvider("B");
        var providerC = MakeProvider("C");
        var resolver = MakeResolver([providerB, providerC], defaultProviderName: "C");
        var ctx = MakeContext();

        var result = await resolver.ResolveAsync(ctx, perActionProviderName: null, subscriptionDefaultProviderName: "B", CancellationToken.None);

        result.Should().NotBeNull();
        await providerB.Received(1).FetchAsync(Arg.Any<SnapshotRequest>(), Arg.Any<CancellationToken>());
        await providerC.DidNotReceive().FetchAsync(Arg.Any<SnapshotRequest>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Global_Default_UsedWhenNoOverride()
    {
        var providerC = MakeProvider("C");
        var resolver = MakeResolver([providerC], defaultProviderName: "C");
        var ctx = MakeContext();

        var result = await resolver.ResolveAsync(ctx, perActionProviderName: null, subscriptionDefaultProviderName: null, CancellationToken.None);

        result.Should().NotBeNull();
        await providerC.Received(1).FetchAsync(Arg.Any<SnapshotRequest>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Cache_Hit_DoesNotCallProviderTwice()
    {
        var providerA = MakeProvider("A");
        var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = MakeResolver([providerA], defaultProviderName: "A", cache: cache);
        var ctx = MakeContext("evt-cache");

        // First call populates cache.
        var first = await resolver.ResolveAsync(ctx, perActionProviderName: "A", subscriptionDefaultProviderName: null, CancellationToken.None);
        // Second call should hit cache.
        var second = await resolver.ResolveAsync(ctx, perActionProviderName: "A", subscriptionDefaultProviderName: null, CancellationToken.None);

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        await providerA.Received(1).FetchAsync(Arg.Any<SnapshotRequest>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task UnknownProviderName_ReturnsNull_AndLogsWarning()
    {
        var logger = new CapturingLogger<SnapshotResolver>();
        var resolver = MakeResolver([], defaultProviderName: null, logger: logger);
        var ctx = MakeContext();

        var result = await resolver.ResolveAsync(ctx, perActionProviderName: "DoesNotExist", subscriptionDefaultProviderName: null, CancellationToken.None);

        result.Should().BeNull();
        logger.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("snapshot_provider_unknown", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task NoTierResolves_ReturnsNull_AndLogsWarning()
    {
        var logger = new CapturingLogger<SnapshotResolver>();
        var resolver = MakeResolver([], defaultProviderName: null, logger: logger);
        var ctx = MakeContext();

        var result = await resolver.ResolveAsync(ctx, perActionProviderName: null, subscriptionDefaultProviderName: null, CancellationToken.None);

        result.Should().BeNull();
        logger.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("snapshot_provider_unresolved", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Resolver_EmitsDebugLog_OnSuccess()
    {
        var logger = new CapturingLogger<SnapshotResolver>();
        var providerA = MakeProvider("A");
        var resolver = MakeResolver([providerA], defaultProviderName: "A", logger: logger);
        var ctx = MakeContext("evt-debug");

        var result = await resolver.ResolveAsync(ctx, perActionProviderName: "A", subscriptionDefaultProviderName: null, CancellationToken.None);

        result.Should().NotBeNull();
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Debug &&
            e.Message.Contains("snapshot_resolved", StringComparison.Ordinal) &&
            e.Message.Contains('A'));
    }
}
