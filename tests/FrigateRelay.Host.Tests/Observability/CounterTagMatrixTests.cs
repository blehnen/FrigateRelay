using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Host.Dispatch;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace FrigateRelay.Host.Tests.Observability;

/// <summary>
/// Structural tag-matrix tests for <see cref="DispatcherDiagnostics"/> helper methods (Phase 13, PR #35).
/// Each test directly invokes one <c>Increment*</c> helper with sentinel values and uses
/// <see cref="MeterListener"/> to capture the emitted tags.
/// Assertions cover:
/// <list type="bullet">
///   <item>The exact tag key set (no extra or missing keys).</item>
///   <item>Each tag value equals the sentinel passed into the helper.</item>
///   <item><c>event_id</c> is NOT present (cardinality-bomb tripwire per CONTEXT-13).</item>
/// </list>
/// </summary>
[TestClass]
public sealed class CounterTagMatrixTests
{
    // Expected tag-key arrays — static readonly per CA1861.
    private static readonly string[] TagsCameraLabel = ["camera", "label"];
    private static readonly string[] TagsSubscriptionCameraLabel = ["subscription", "camera", "label"];
    private static readonly string[] TagsSubscriptionCameraAction = ["subscription", "camera", "action"];
    private static readonly string[] TagsSubscriptionCameraValidatorAction = ["subscription", "camera", "validator", "action"];
    private static readonly string[] TagsSubscriptionCameraReason = ["subscription", "camera", "reason"];
    private static readonly string[] TagsComponent = ["component"];
    // -----------------------------------------------------------------------
    // Test 1 — frigaterelay.events.received: tags = {camera, label}
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IncrementEventsReceived_Tags_AreCameraAndLabel()
    {
        var captured = new List<KeyValuePair<string, object?>[]>();
        using var listener = BuildListener("frigaterelay.events.received", captured);

        var ctx = MakeContext(camera: "front_door", label: "person");
        DispatcherDiagnostics.IncrementEventsReceived(ctx);

        captured.Should().HaveCount(1, "one call → one measurement");
        var tags = captured[0];
        TagKeys(tags).Should().BeEquivalentTo(TagsCameraLabel,
            "events.received must carry exactly camera and label");
        TagValue(tags, "camera").Should().Be("front_door");
        TagValue(tags, "label").Should().Be("person");
        tags.Should().NotContain(t => t.Key == "event_id", "event_id is a forbidden high-cardinality tag");
    }

    // -----------------------------------------------------------------------
    // Test 2 — frigaterelay.events.matched: tags = {subscription, camera, label}
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IncrementEventsMatched_Tags_AreSubscriptionCameraLabel()
    {
        var captured = new List<KeyValuePair<string, object?>[]>();
        using var listener = BuildListener("frigaterelay.events.matched", captured);

        var ctx = MakeContext(camera: "front_door", label: "person");
        DispatcherDiagnostics.IncrementEventsMatched(ctx, subscription: "kitchen-cam");

        captured.Should().HaveCount(1);
        var tags = captured[0];
        TagKeys(tags).Should().BeEquivalentTo(TagsSubscriptionCameraLabel,
            "events.matched must carry subscription, camera, and label");
        TagValue(tags, "subscription").Should().Be("kitchen-cam");
        TagValue(tags, "camera").Should().Be("front_door");
        TagValue(tags, "label").Should().Be("person");
        tags.Should().NotContain(t => t.Key == "event_id", "event_id is a forbidden high-cardinality tag");
    }

    // -----------------------------------------------------------------------
    // Test 3 — frigaterelay.actions.dispatched: tags = {subscription, camera, action}
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IncrementActionsDispatched_Tags_AreSubscriptionCameraAction()
    {
        var captured = new List<KeyValuePair<string, object?>[]>();
        using var listener = BuildListener("frigaterelay.actions.dispatched", captured);

        var item = MakeDispatchItem(camera: "front_door", subscription: "kitchen-cam", pluginName: "BlueIris");
        DispatcherDiagnostics.IncrementActionsDispatched(item);

        captured.Should().HaveCount(1);
        var tags = captured[0];
        TagKeys(tags).Should().BeEquivalentTo(TagsSubscriptionCameraAction,
            "actions.dispatched must carry subscription, camera, and action");
        TagValue(tags, "subscription").Should().Be("kitchen-cam");
        TagValue(tags, "camera").Should().Be("front_door");
        TagValue(tags, "action").Should().Be("BlueIris");
        tags.Should().NotContain(t => t.Key == "event_id", "event_id is a forbidden high-cardinality tag");
    }

    // -----------------------------------------------------------------------
    // Test 4 — frigaterelay.actions.succeeded: tags = {subscription, camera, action}
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IncrementActionsSucceeded_Tags_AreSubscriptionCameraAction()
    {
        var captured = new List<KeyValuePair<string, object?>[]>();
        using var listener = BuildListener("frigaterelay.actions.succeeded", captured);

        var item = MakeDispatchItem(camera: "front_door", subscription: "kitchen-cam", pluginName: "BlueIris");
        DispatcherDiagnostics.IncrementActionsSucceeded(item);

        captured.Should().HaveCount(1);
        var tags = captured[0];
        TagKeys(tags).Should().BeEquivalentTo(TagsSubscriptionCameraAction,
            "actions.succeeded must carry subscription, camera, and action");
        TagValue(tags, "subscription").Should().Be("kitchen-cam");
        TagValue(tags, "camera").Should().Be("front_door");
        TagValue(tags, "action").Should().Be("BlueIris");
        tags.Should().NotContain(t => t.Key == "event_id", "event_id is a forbidden high-cardinality tag");
    }

    // -----------------------------------------------------------------------
    // Test 5 — frigaterelay.actions.failed: tags = {subscription, camera, action}
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IncrementActionsFailed_Tags_AreSubscriptionCameraAction()
    {
        var captured = new List<KeyValuePair<string, object?>[]>();
        using var listener = BuildListener("frigaterelay.actions.failed", captured);

        var item = MakeDispatchItem(camera: "front_door", subscription: "kitchen-cam", pluginName: "Pushover");
        DispatcherDiagnostics.IncrementActionsFailed(item);

        captured.Should().HaveCount(1);
        var tags = captured[0];
        TagKeys(tags).Should().BeEquivalentTo(TagsSubscriptionCameraAction,
            "actions.failed must carry subscription, camera, and action");
        TagValue(tags, "subscription").Should().Be("kitchen-cam");
        TagValue(tags, "camera").Should().Be("front_door");
        TagValue(tags, "action").Should().Be("Pushover");
        tags.Should().NotContain(t => t.Key == "event_id", "event_id is a forbidden high-cardinality tag");
    }

    // -----------------------------------------------------------------------
    // Test 6 — frigaterelay.validators.passed: tags = {subscription, camera, validator, action}
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IncrementValidatorsPassed_Tags_AreSubscriptionCameraValidatorAction()
    {
        var captured = new List<KeyValuePair<string, object?>[]>();
        using var listener = BuildListener("frigaterelay.validators.passed", captured);

        var item = MakeDispatchItem(camera: "front_door", subscription: "kitchen-cam", pluginName: "Pushover");
        DispatcherDiagnostics.IncrementValidatorsPassed(item, validatorName: "strict-person");

        captured.Should().HaveCount(1);
        var tags = captured[0];
        TagKeys(tags).Should().BeEquivalentTo(TagsSubscriptionCameraValidatorAction,
            "validators.passed must carry subscription, camera, validator, and action");
        TagValue(tags, "subscription").Should().Be("kitchen-cam");
        TagValue(tags, "camera").Should().Be("front_door");
        TagValue(tags, "validator").Should().Be("strict-person");
        TagValue(tags, "action").Should().Be("Pushover");
        tags.Should().NotContain(t => t.Key == "event_id", "event_id is a forbidden high-cardinality tag");
    }

    // -----------------------------------------------------------------------
    // Test 7 — frigaterelay.validators.rejected: tags = {subscription, camera, validator, action}
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IncrementValidatorsRejected_Tags_AreSubscriptionCameraValidatorAction()
    {
        var captured = new List<KeyValuePair<string, object?>[]>();
        using var listener = BuildListener("frigaterelay.validators.rejected", captured);

        var item = MakeDispatchItem(camera: "front_door", subscription: "kitchen-cam", pluginName: "Pushover");
        DispatcherDiagnostics.IncrementValidatorsRejected(item, validatorName: "strict-person");

        captured.Should().HaveCount(1);
        var tags = captured[0];
        TagKeys(tags).Should().BeEquivalentTo(TagsSubscriptionCameraValidatorAction,
            "validators.rejected must carry subscription, camera, validator, and action");
        TagValue(tags, "subscription").Should().Be("kitchen-cam");
        TagValue(tags, "camera").Should().Be("front_door");
        TagValue(tags, "validator").Should().Be("strict-person");
        TagValue(tags, "action").Should().Be("Pushover");
        tags.Should().NotContain(t => t.Key == "event_id", "event_id is a forbidden high-cardinality tag");
    }

    // -----------------------------------------------------------------------
    // Test 8 — frigaterelay.dispatch.drops: tags = {subscription, camera, reason}
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IncrementDrops_Tags_AreSubscriptionCameraReason()
    {
        var captured = new List<KeyValuePair<string, object?>[]>();
        using var listener = BuildListener("frigaterelay.dispatch.drops", captured);

        var item = MakeDispatchItem(camera: "front_door", subscription: "kitchen-cam", pluginName: "BlueIris");
        DispatcherDiagnostics.IncrementDrops(item, reason: "channel_full");

        captured.Should().HaveCount(1);
        var tags = captured[0];
        TagKeys(tags).Should().BeEquivalentTo(TagsSubscriptionCameraReason,
            "dispatch.drops must carry subscription, camera, and reason");
        TagValue(tags, "subscription").Should().Be("kitchen-cam");
        TagValue(tags, "camera").Should().Be("front_door");
        TagValue(tags, "reason").Should().Be("channel_full");
        tags.Should().NotContain(t => t.Key == "event_id", "event_id is a forbidden high-cardinality tag");
    }

    // -----------------------------------------------------------------------
    // Test 9 — frigaterelay.dispatch.exhausted: tags = {subscription, camera, action}
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IncrementExhausted_Tags_AreSubscriptionCameraAction()
    {
        var captured = new List<KeyValuePair<string, object?>[]>();
        using var listener = BuildListener("frigaterelay.dispatch.exhausted", captured);

        var item = MakeDispatchItem(camera: "front_door", subscription: "kitchen-cam", pluginName: "BlueIris");
        DispatcherDiagnostics.IncrementExhausted(item);

        captured.Should().HaveCount(1);
        var tags = captured[0];
        TagKeys(tags).Should().BeEquivalentTo(TagsSubscriptionCameraAction,
            "dispatch.exhausted must carry subscription, camera, and action");
        TagValue(tags, "subscription").Should().Be("kitchen-cam");
        TagValue(tags, "camera").Should().Be("front_door");
        TagValue(tags, "action").Should().Be("BlueIris");
        tags.Should().NotContain(t => t.Key == "event_id", "event_id is a forbidden high-cardinality tag");
    }

    // -----------------------------------------------------------------------
    // Test 10 — frigaterelay.errors.unhandled: tags = {component}
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IncrementErrorsUnhandled_Tags_AreComponent()
    {
        var captured = new List<KeyValuePair<string, object?>[]>();
        using var listener = BuildListener("frigaterelay.errors.unhandled", captured);

        DispatcherDiagnostics.IncrementErrorsUnhandled(component: "EventPump");

        captured.Should().HaveCount(1);
        var tags = captured[0];
        TagKeys(tags).Should().BeEquivalentTo(TagsComponent,
            "errors.unhandled must carry exactly the component tag");
        TagValue(tags, "component").Should().Be("EventPump");
        tags.Should().NotContain(t => t.Key == "event_id", "event_id is a forbidden high-cardinality tag");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="MeterListener"/> that captures tag arrays from the named
    /// instrument on the <c>"FrigateRelay"</c> meter. Dispose after the test.
    /// </summary>
    private static MeterListener BuildListener(string instrumentName, List<KeyValuePair<string, object?>[]> captured)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "FrigateRelay" && instrument.Name == instrumentName)
                    l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => captured.Add(tags.ToArray()));
        listener.Start();
        return listener;
    }

    private static string[] TagKeys(KeyValuePair<string, object?>[] tags) =>
        tags.Select(t => t.Key).ToArray();

    private static string? TagValue(KeyValuePair<string, object?>[] tags, string key) =>
        tags.FirstOrDefault(t => t.Key == key).Value?.ToString();

    private static EventContext MakeContext(string camera = "front_door", string label = "person") =>
        new()
        {
            EventId = "evt-sentinel",
            Camera = camera,
            Label = label,
            Zones = Array.Empty<string>(),
            StartedAt = DateTimeOffset.UtcNow,
            RawPayload = "{}",
            SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null),
        };

    /// <summary>
    /// Creates a <see cref="DispatchItem"/> with the specified sentinel values.
    /// Uses NSubstitute for <see cref="IActionPlugin"/> so <c>.Name</c> returns the supplied plugin name.
    /// </summary>
    private static DispatchItem MakeDispatchItem(
        string camera = "front_door",
        string subscription = "kitchen-cam",
        string pluginName = "BlueIris")
    {
        var plugin = Substitute.For<IActionPlugin>();
        plugin.Name.Returns(pluginName);

        return new DispatchItem(
            Context: MakeContext(camera: camera),
            Plugin: plugin,
            Validators: Array.Empty<IValidationPlugin>(),
            ParentContext: default(ActivityContext),
            Subscription: subscription);
    }
}
