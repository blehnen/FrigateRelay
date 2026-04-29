using FluentAssertions;
using FrigateRelay.Sources.FrigateMqtt;
using FrigateRelay.Sources.FrigateMqtt.Payloads;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Sources.FrigateMqtt.Tests;

/// <summary>
/// Unit tests for <see cref="EventContextProjector.TryProject"/>.
/// Covers zone-union aggregation (OQ4), RawPayload round-trip, SnapshotFetcher contract,
/// and D5 stationary/false-positive skip rules.
/// </summary>
[TestClass]
public sealed class EventContextProjectorTests
{
    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static FrigateEvent NewEvent(
        string type = "new",
        string id = "event-id-001",
        string camera = "front_door",
        string label = "person",
        double startTime = 1714000001.0,
        bool stationary = false,
        bool falsePositive = false,
        string[]? beforeCurrentZones = null,
        string[]? beforeEnteredZones = null,
        string[]? afterCurrentZones = null,
        string[]? afterEnteredZones = null)
    {
        return new FrigateEvent
        {
            Type = type,
            Before = new FrigateEventObject
            {
                Id = id,
                Camera = camera,
                Label = label,
                StartTime = startTime,
                Stationary = stationary,
                FalsePositive = falsePositive,
                CurrentZones = beforeCurrentZones ?? [],
                EnteredZones = beforeEnteredZones ?? [],
            },
            After = new FrigateEventObject
            {
                Id = id,
                Camera = camera,
                Label = label,
                StartTime = startTime,
                Stationary = stationary,
                FalsePositive = falsePositive,
                CurrentZones = afterCurrentZones ?? [],
                EnteredZones = afterEnteredZones ?? [],
            },
        };
    }

    // ------------------------------------------------------------------
    // Test 1: Zone deduplication — after has duplicate across current+entered
    // ------------------------------------------------------------------

    /// <summary>
    /// A "new" event where after.current_zones and after.entered_zones both contain "driveway",
    /// and before arrays are empty — the projected Zones list contains "driveway" exactly once.
    /// </summary>
    [TestMethod]
    public void TryProject_NewEvent_ZonesAreDeduplicatedFromAfterArrays()
    {
        var evt = NewEvent(
            type: "new",
            afterCurrentZones: ["driveway"],
            afterEnteredZones: ["driveway"]);

        var result = EventContextProjector.TryProject(evt, "raw", out var ctx);

        result.Should().BeTrue();
        ctx.Zones.Should().ContainSingle().Which.Should().Be("driveway");
    }

    // ------------------------------------------------------------------
    // Test 2: Zone union — disjoint zones across all four arrays
    // ------------------------------------------------------------------

    /// <summary>
    /// A "new" event with distinct zone names in each of the four zone arrays produces
    /// a Zones list that is the full union of all four, with no duplicates.
    /// </summary>
    [TestMethod]
    public void TryProject_NewEvent_ZoneUnionSpansAllFourArrays()
    {
        var evt = NewEvent(
            type: "new",
            beforeCurrentZones: ["zone-a"],
            beforeEnteredZones: ["zone-b"],
            afterCurrentZones: ["zone-c"],
            afterEnteredZones: ["zone-d"]);

        var result = EventContextProjector.TryProject(evt, "raw", out var ctx);

        result.Should().BeTrue();
        ctx.Zones.Should().BeEquivalentTo(["zone-a", "zone-b", "zone-c", "zone-d"],
            because: "all four zone arrays must be unioned into EventContext.Zones");
    }

    // ------------------------------------------------------------------
    // Test 3: RawPayload round-trip
    // ------------------------------------------------------------------

    /// <summary>
    /// The raw payload string passed to TryProject is preserved verbatim in EventContext.RawPayload.
    /// </summary>
    [TestMethod]
    public void TryProject_RawPayload_RoundTripsExactInputString()
    {
        const string raw = """{"type":"new","before":null,"after":null}""";
        var evt = NewEvent(type: "new");

        var result = EventContextProjector.TryProject(evt, raw, out var ctx);

        result.Should().BeTrue();
        ctx.RawPayload.Should().Be(raw);
    }

    // ------------------------------------------------------------------
    // Test 4: SnapshotFetcher returns null
    // ------------------------------------------------------------------

    /// <summary>
    /// The SnapshotFetcher delegate on the projected EventContext always returns null
    /// (D3 revised — thumbnail is always null in frigate/events MQTT messages).
    /// </summary>
    [TestMethod]
    public async Task TryProject_SnapshotFetcher_ReturnsNull()
    {
        var evt = NewEvent(type: "new");

        var result = EventContextProjector.TryProject(evt, "raw", out var ctx);

        result.Should().BeTrue();
        var snapshot = await ctx.SnapshotFetcher(CancellationToken.None);
        snapshot.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // Test 5: D5 — update + stationary → skip
    // ------------------------------------------------------------------

    /// <summary>
    /// An "update" event with after.stationary == true must return false (D5 guard).
    /// The out parameter is not populated.
    /// </summary>
    [TestMethod]
    public void TryProject_UpdateEvent_StationaryTrue_ReturnsFalse()
    {
        var evt = NewEvent(type: "update", stationary: true);

        var result = EventContextProjector.TryProject(evt, "raw", out _);

        result.Should().BeFalse("D5: update+stationary events must be skipped");
    }

    // ------------------------------------------------------------------
    // Test 6: D5 — update + false_positive → skip
    // ------------------------------------------------------------------

    /// <summary>
    /// An "update" event with after.false_positive == true must return false (D5 guard).
    /// </summary>
    [TestMethod]
    public void TryProject_UpdateEvent_FalsePositiveTrue_ReturnsFalse()
    {
        var evt = NewEvent(type: "update", falsePositive: true);

        var result = EventContextProjector.TryProject(evt, "raw", out _);

        result.Should().BeFalse("D5: update+false_positive events must be skipped");
    }

    // ------------------------------------------------------------------
    // Test 7: D5 does NOT fire on "new" — stationary=true on new proceeds
    // ------------------------------------------------------------------

    /// <summary>
    /// A "new" event with after.stationary == true MUST NOT be skipped.
    /// D5 only fires on type ∈ {update, end}. Legacy parity: "new" always proceeds.
    /// </summary>
    [TestMethod]
    public void TryProject_NewEvent_StationaryTrue_StillProjects()
    {
        var evt = NewEvent(type: "new", stationary: true);

        var result = EventContextProjector.TryProject(evt, "raw", out var ctx);

        result.Should().BeTrue("D5 only applies to update/end events; new always proceeds");
        ctx.Camera.Should().Be("front_door");
    }
}
