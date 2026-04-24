using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Host.Configuration;
using FrigateRelay.Host.Matching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Host.Tests;

[TestClass]
public sealed class SubscriptionMatcherTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static EventContext MakeContext(
        string camera,
        string label,
        IReadOnlyList<string>? zones = null) =>
        new()
        {
            EventId = "test-event-id",
            Camera = camera,
            Label = label,
            Zones = zones ?? Array.Empty<string>(),
            StartedAt = DateTimeOffset.UtcNow,
            RawPayload = "{}",
            SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null),
        };

    private static SubscriptionOptions MakeSub(
        string name,
        string camera,
        string label,
        string? zone = null) =>
        new()
        {
            Name = name,
            Camera = camera,
            Label = label,
            Zone = zone,
        };

    // ---------------------------------------------------------------------------
    // Test 1: single matching sub returns exactly one result
    // ---------------------------------------------------------------------------

    [TestMethod]
    public void Match_SingleMatchingSubscription_ReturnsOneResult()
    {
        var subs = new[]
        {
            MakeSub("alert-person", "front_door", "person"),
        };
        var ctx = MakeContext("front_door", "person");

        var result = SubscriptionMatcher.Match(ctx, subs);

        result.Should().ContainSingle()
            .Which.Name.Should().Be("alert-person");
    }

    // ---------------------------------------------------------------------------
    // Test 2: D1 — both subscriptions matching the same event must BOTH be returned
    // ---------------------------------------------------------------------------

    [TestMethod]
    public void Match_TwoMatchingSubscriptions_ReturnsBoth_D1()
    {
        var subs = new[]
        {
            MakeSub("alert-1", "front_door", "person"),
            MakeSub("alert-2", "front_door", "person"),
        };
        var ctx = MakeContext("front_door", "person");

        var result = SubscriptionMatcher.Match(ctx, subs);

        result.Should().HaveCount(2, "D1: all matching subscriptions must fire, not just the first");
        result.Select(s => s.Name).Should().BeEquivalentTo(["alert-1", "alert-2"]);
    }

    // ---------------------------------------------------------------------------
    // Test 3: Camera match is case-insensitive
    // ---------------------------------------------------------------------------

    [TestMethod]
    public void Match_CameraMatchIsCaseInsensitive()
    {
        var subs = new[]
        {
            MakeSub("alert-door", "Front_Door", "person"),
        };
        var ctx = MakeContext("front_door", "person");

        var result = SubscriptionMatcher.Match(ctx, subs);

        result.Should().ContainSingle("camera comparison must be case-insensitive");
    }

    // ---------------------------------------------------------------------------
    // Test 4: Zone null/empty => match-any (even when EventContext.Zones is empty)
    // ---------------------------------------------------------------------------

    [TestMethod]
    public void Match_ZoneNull_MatchesEventWithNoZones()
    {
        var subs = new[]
        {
            MakeSub("alert-any-zone", "front_door", "person", zone: null),
        };
        var ctx = MakeContext("front_door", "person", zones: Array.Empty<string>());

        var result = SubscriptionMatcher.Match(ctx, subs);

        result.Should().ContainSingle("null Zone means match-any; empty Zones list must still match");
    }

    // ---------------------------------------------------------------------------
    // Test 5: Zone match is case-insensitive
    // ---------------------------------------------------------------------------

    [TestMethod]
    public void Match_ZoneMatchIsCaseInsensitive()
    {
        var subs = new[]
        {
            MakeSub("alert-driveway", "front_door", "person", zone: "driveway"),
        };
        var ctx = MakeContext("front_door", "person", zones: ["Driveway"]);

        var result = SubscriptionMatcher.Match(ctx, subs);

        result.Should().ContainSingle("zone comparison must be case-insensitive");
    }

    // ---------------------------------------------------------------------------
    // Test 6: Non-matching zone returns empty
    // ---------------------------------------------------------------------------

    [TestMethod]
    public void Match_ZoneNotInEventZones_ReturnsEmpty()
    {
        var subs = new[]
        {
            MakeSub("alert-porch", "front_door", "person", zone: "porch"),
        };
        var ctx = MakeContext("front_door", "person", zones: ["driveway"]);

        var result = SubscriptionMatcher.Match(ctx, subs);

        result.Should().BeEmpty("zone 'porch' is not in the event's zones list ['driveway']");
    }
}
