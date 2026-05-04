using System.Collections.Frozen;
using FluentAssertions;
using FrigateRelay.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Abstractions.Tests;

[TestClass]
public class EventTokenTemplateTests
{
    private static EventContext NewCtx(
        string camera = "front",
        string label = "person",
        string eventId = "ev-1",
        IReadOnlyList<string>? zones = null,
        string? cameraShortName = null) => new()
    {
        EventId = eventId,
        Camera = camera,
        Label = label,
        Zones = zones ?? Array.Empty<string>(),
        StartedAt = DateTimeOffset.UnixEpoch,
        RawPayload = "{}",
        SnapshotFetcher = static _ => ValueTask.FromResult<byte[]?>(null),
        CameraShortName = cameraShortName,
    };

    [TestMethod]
    public void Parse_NullTemplate_ThrowsArgumentException()
    {
        var act = () => EventTokenTemplate.Parse(null!, "Caller=Test");
        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void Parse_WhitespaceTemplate_ThrowsArgumentException()
    {
        var act = () => EventTokenTemplate.Parse("   ", "Caller=Test");
        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void Parse_ScoreToken_ThrowsArgumentExceptionMentioningScoreAndCaller()
    {
        var act = () => EventTokenTemplate.Parse("https://x/{score}", "Caller=BlueIris");
        act.Should().Throw<ArgumentException>()
            .Which.Message.Should().ContainAll("{score}", "BlueIris");
    }

    [TestMethod]
    public void Parse_UnknownToken_ThrowsArgumentExceptionMentioningCaller()
    {
        var act = () => EventTokenTemplate.Parse("https://x/{nope}", "Caller=SomePlugin");
        act.Should().Throw<ArgumentException>()
            .Which.Message.Should().Contain("SomePlugin");
    }

    [TestMethod]
    public void Resolve_DefaultUrlEncoded_EscapesSpaces()
    {
        var tmpl = EventTokenTemplate.Parse("hi {label} on {camera}", "Caller=Test");
        var ctx = NewCtx(camera: "front cam", label: "front door");

        var result = tmpl.Resolve(ctx);

        result.Should().Contain("front%20door");
        result.Should().Contain("front%20cam");
    }

    [TestMethod]
    public void Resolve_RawMode_DoesNotEscape()
    {
        var tmpl = EventTokenTemplate.Parse("hi {label} on {camera}", "Caller=Test");
        var ctx = NewCtx(camera: "front cam", label: "front door");

        var result = tmpl.Resolve(ctx, urlEncode: false);

        result.Should().Contain("front door");
        result.Should().Contain("front cam");
    }

    [TestMethod]
    public void Resolve_EmptyZones_ReplacesZoneWithEmptyString()
    {
        var tmpl = EventTokenTemplate.Parse("{zone}", "Caller=Test");
        var ctx = NewCtx(zones: []);

        var result = tmpl.Resolve(ctx);

        result.Should().Be("");
    }

    [TestMethod]
    public void Resolve_FirstZoneUsed()
    {
        var tmpl = EventTokenTemplate.Parse("{zone}", "Caller=Test");
        var ctx = NewCtx(zones: ["driveway", "yard"]);

        var result = tmpl.Resolve(ctx, urlEncode: false);

        result.Should().Be("driveway");
    }

    // ---------- {camera_shortname} (#32) ----------

    [TestMethod]
    public void Parse_CameraShortnameToken_AcceptedWithoutThrowing()
    {
        var act = () => EventTokenTemplate.Parse(
            "http://bi/admin?camera={camera_shortname}", "Caller=BlueIris");
        act.Should().NotThrow();
    }

    [TestMethod]
    public void Resolve_CameraShortnameUnset_FallsThroughToCamera()
    {
        var tmpl = EventTokenTemplate.Parse("{camera_shortname}", "Caller=Test");
        var ctx = NewCtx(camera: "driveway", cameraShortName: null);

        var result = tmpl.Resolve(ctx, urlEncode: false);

        result.Should().Be("driveway",
            "operators whose Frigate id and BI shortname already match should keep " +
            "working without setting CameraShortName per subscription");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("\t")]
    public void Resolve_CameraShortnameBlankOrWhitespace_FallsThroughToCamera(string blankOverride)
    {
        // IConfiguration.Bind happily produces an empty string for "CameraShortName": "" or
        // for an env var like CAMERASHORTNAME= with no value. Plain `??` would let that empty
        // value through and we'd be back to the silent-no-op trap #32 was supposed to close.
        // Treat any blank/whitespace as unset.
        var tmpl = EventTokenTemplate.Parse("{camera_shortname}", "Caller=Test");
        var ctx = NewCtx(camera: "driveway", cameraShortName: blankOverride);

        var result = tmpl.Resolve(ctx, urlEncode: false);

        result.Should().Be("driveway",
            "blank/whitespace must fall through to Camera, otherwise BI gets " +
            "camera= and silently no-ops just like the unconfigured case");
    }

    [TestMethod]
    public void Resolve_CameraShortnameSet_UsesOverride()
    {
        var tmpl = EventTokenTemplate.Parse("{camera_shortname}", "Caller=Test");
        var ctx = NewCtx(camera: "driveway", cameraShortName: "DriveWayHD");

        var result = tmpl.Resolve(ctx, urlEncode: false);

        result.Should().Be("DriveWayHD",
            "the override is the whole point of #32 — Blue Iris's HTTP API silently " +
            "no-ops on unknown camera names, so the URL must carry the BI shortname");
    }

    [TestMethod]
    public void Resolve_CameraTokenAndShortnameToken_AreIndependent()
    {
        // {camera} continues to render Frigate's id even when CameraShortName is set —
        // important so operators who use {camera} in Pushover message templates don't
        // suddenly get BI shortname text in their phone notifications.
        var tmpl = EventTokenTemplate.Parse(
            "frigate={camera} bi={camera_shortname}", "Caller=Test");
        var ctx = NewCtx(camera: "driveway", cameraShortName: "DriveWayHD");

        var result = tmpl.Resolve(ctx, urlEncode: false);

        result.Should().Be("frigate=driveway bi=DriveWayHD");
    }

    [TestMethod]
    public void AllowedTokens_IncludesCameraShortname()
    {
        // Pins the public surface so a future refactor that drops {camera_shortname}
        // from AllowedTokens fails immediately, not silently.
        EventTokenTemplate.AllowedTokens.Should().Contain("camera_shortname");
    }

    [TestMethod]
    public void Parse_UnknownToken_ErrorMessageListsCameraShortname()
    {
        // Covers the modified allowed-placeholders text in the Parse error path.
        // Operators triaging a "{nope} unknown" config error see {camera_shortname} in
        // the suggestions list, which is itself the affordance that surfaces the new field.
        var act = () => EventTokenTemplate.Parse("https://x/{nope}", "Caller=Test");
        act.Should().Throw<ArgumentException>()
            .Which.Message.Should().Contain("{camera_shortname}");
    }

    [TestMethod]
    public void Resolve_CameraShortnameUrlEncoded()
    {
        var tmpl = EventTokenTemplate.Parse("{camera_shortname}", "Caller=Test");
        var ctx = NewCtx(camera: "driveway", cameraShortName: "Drive Way HD");

        var result = tmpl.Resolve(ctx);

        result.Should().Be("Drive%20Way%20HD",
            "the override flows through the same Uri.EscapeDataString path as {camera}");
    }

    // ---------- Canonical-set drift guard (post-#34 single source of truth) ----------

    private static readonly FrozenSet<string> _canonicalTokens =
        new[] { "camera", "camera_shortname", "label", "event_id", "zone" }
            .ToFrozenSet(StringComparer.Ordinal);

    [TestMethod]
    public void EventTokenTemplate_AllowedTokens_Canonical()
    {
        // Hardcoded expected set — NOT a self-comparison. Adding a future token (e.g. {score})
        // requires updating BOTH EventTokenTemplate.AllowedTokens AND this test, by design.
        EventTokenTemplate.AllowedTokens.SetEquals(_canonicalTokens).Should().BeTrue(
            because: "AllowedTokens is the single source of truth for templated event-context placeholders. " +
                     "Adding a token requires updating both EventTokenTemplate.AllowedTokens and this canonical-set test.");
    }
}
