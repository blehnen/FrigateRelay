using FluentAssertions;
using NSubstitute;

namespace FrigateRelay.Abstractions.Tests;

[TestClass]
public sealed class SnapshotContextTests
{
    private static EventContext MakeEvent(string id = "evt-1") => new()
    {
        EventId = id,
        Camera = "front",
        Label = "person",
        Zones = Array.Empty<string>(),
        StartedAt = DateTimeOffset.UtcNow,
        RawPayload = "{}",
        SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null),
    };

    [TestMethod]
    public async Task ResolveAsync_DefaultStruct_ReturnsNull_WithoutInvokingResolver()
    {
        var ctx = default(SnapshotContext);
        var evt = MakeEvent();

        var result = await ctx.ResolveAsync(evt, CancellationToken.None);

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task ResolveAsync_WithResolver_PassesProviderNamesThrough()
    {
        var resolver = Substitute.For<ISnapshotResolver>();
        var ctx = new SnapshotContext(resolver, perActionProviderName: "BlueIris", subscriptionDefaultProviderName: "Frigate");
        var evt = MakeEvent();

        await ctx.ResolveAsync(evt, CancellationToken.None);

        await resolver.Received(1).ResolveAsync(evt, "BlueIris", "Frigate", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ResolveAsync_WithResolverAndNullNames_PassesNullsThrough()
    {
        var resolver = Substitute.For<ISnapshotResolver>();
        var ctx = new SnapshotContext(resolver, perActionProviderName: null, subscriptionDefaultProviderName: null);
        var evt = MakeEvent();

        await ctx.ResolveAsync(evt, CancellationToken.None);

        await resolver.Received(1).ResolveAsync(evt, null, null, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void Properties_ExposeConstructorArguments()
    {
        var resolver = Substitute.For<ISnapshotResolver>();
        var ctx = new SnapshotContext(resolver, "perAction", "subDefault");

        ctx.PerActionProviderName.Should().Be("perAction");
        ctx.SubscriptionDefaultProviderName.Should().Be("subDefault");
    }

    [TestMethod]
    public async Task ResolveAsync_ReturnsResolverResult()
    {
        var expected = new SnapshotResult { Bytes = [0xFF, 0xD8], ContentType = "image/jpeg", ProviderName = "BlueIris" };
        var resolver = new StubResolver(expected);

        var ctx = new SnapshotContext(resolver, "BlueIris", null);
        var evt = MakeEvent();

        var result = await ctx.ResolveAsync(evt, CancellationToken.None);

        result.Should().BeSameAs(expected);
    }

    private sealed class StubResolver(SnapshotResult? toReturn) : ISnapshotResolver
    {
        public ValueTask<SnapshotResult?> ResolveAsync(EventContext context, string? perActionProviderName, string? subscriptionDefaultProviderName, CancellationToken cancellationToken) =>
            ValueTask.FromResult(toReturn);
    }
}
