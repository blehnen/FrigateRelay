using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Plugins.BlueIris;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Plugins.BlueIris.Tests;

[TestClass]
public class BlueIrisUrlTemplateTests
{
    private static EventContext NewCtx(string camera = "front", string label = "person",
        string eventId = "ev-1", IReadOnlyList<string>? zones = null,
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

    // ---------- {camera_shortname} regression for v1.0.2 → v1.0.3 (#32 follow-up) ----------
    //
    // PR #33 added {camera_shortname} to EventTokenTemplate but missed BlueIrisUrlTemplate's
    // own AllowedTokens list. Result: v1.0.2 host crashed at startup with
    // "BlueIris.TriggerUrlTemplate contains unknown placeholder '{camera_shortname}'".
    // The README + migration tool both pointed operators at the new token, so any operator
    // who upgraded was crash-looped on first launch. These tests pin both the parse and
    // resolve paths so the next release that touches BlueIrisUrlTemplate cannot regress.

    [TestMethod]
    public void Parse_CameraShortnameToken_AcceptedWithoutThrowing()
    {
        var act = () => BlueIrisUrlTemplate.Parse(
            "http://bi/admin?trigger&camera={camera_shortname}");
        act.Should().NotThrow();
    }

    [TestMethod]
    public void Resolve_CameraShortnameSet_UsesOverrideInUrl()
    {
        var tmpl = BlueIrisUrlTemplate.Parse(
            "http://bi/admin?trigger&camera={camera_shortname}");
        var ctx = NewCtx(camera: "driveway", cameraShortName: "DriveWayHD");

        var url = tmpl.Resolve(ctx);

        url.Should().Be("http://bi/admin?trigger&camera=DriveWayHD",
            "Blue Iris's HTTP API silently no-ops on unknown camera names — the URL " +
            "must carry the BI shortname, not Frigate's id");
    }

    [TestMethod]
    public void Resolve_CameraShortnameUnsetOrBlank_FallsThroughToCamera()
    {
        var tmpl = BlueIrisUrlTemplate.Parse(
            "http://bi/admin?trigger&camera={camera_shortname}");
        // Operators whose Frigate id and BI shortname already match leave CameraShortName
        // unset; substituting an empty string here would produce "camera=" and BI would
        // silently no-op exactly like the unconfigured-Frigate-id case.
        var ctx = NewCtx(camera: "driveway", cameraShortName: null);

        var url = tmpl.Resolve(ctx);

        url.Should().Be("http://bi/admin?trigger&camera=driveway");
    }

    [TestMethod]
    public void Parse_UnknownToken_ErrorMessageListsCameraShortname()
    {
        var act = () => BlueIrisUrlTemplate.Parse("http://bi/admin?trigger&camera={nope}");
        act.Should().Throw<ArgumentException>()
            .Which.Message.Should().Contain("{camera_shortname}",
                "operators triaging an unknown-placeholder error need to see the new token " +
                "in the suggestions list, otherwise they retry with {camera} and stay broken");
    }

    [TestMethod]
    public void Parse_WithKnownPlaceholders_ReturnsInstance()
    {
        var template = "https://x/{camera}?l={label}&e={event_id}&z={zone}";
        var result = BlueIrisUrlTemplate.Parse(template);
        result.Should().NotBeNull();
    }

    [TestMethod]
    public void Parse_WithUnknownPlaceholder_ThrowsArgumentException_WithDiagnosticMessage()
    {
        var act = () => BlueIrisUrlTemplate.Parse("https://x/{nope}");
        act.Should().Throw<ArgumentException>()
            .WithMessage("*'{nope}'*")
            .And.Message.Should().Contain("Allowed placeholders:");
    }

    [TestMethod]
    public void Parse_WithScorePlaceholder_ThrowsBecauseScoreIsNotInAllowlist()
    {
        // Encodes the Q1 architectural decision: {score} was deferred because EventContext
        // carries no Score property. If Score is later added to EventContext, this test
        // AND the FrozenSet in BlueIrisUrlTemplate must be updated in the same commit.
        var act = () => BlueIrisUrlTemplate.Parse("https://x/{score}");
        act.Should().Throw<ArgumentException>()
            .WithMessage("*'{score}'*")
            .And.Message.Should().Contain("Allowed placeholders:");
    }

    [TestMethod]
    public void Parse_WithEmptyTemplate_Throws()
    {
        var actEmpty = () => BlueIrisUrlTemplate.Parse("");
        actEmpty.Should().Throw<ArgumentException>();

        var actWhitespace = () => BlueIrisUrlTemplate.Parse("   ");
        actWhitespace.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void Resolve_UrlEncodesValues()
    {
        var tmpl = BlueIrisUrlTemplate.Parse("x?c={camera}&l={label}&e={event_id}&z={zone}");
        var ctx = NewCtx(camera: "front door", label: "person", eventId: "ev-1", zones: ["drive way"]);

        var result = tmpl.Resolve(ctx);

        result.Should().Be("x?c=front%20door&l=person&e=ev-1&z=drive%20way");
    }

    [TestMethod]
    public void Resolve_WithEmptyZones_SubstitutesEmptyString()
    {
        var tmpl = BlueIrisUrlTemplate.Parse("x?z={zone}");
        var ctx = NewCtx(zones: []);

        var result = tmpl.Resolve(ctx);

        result.Should().Be("x?z=");
    }

    [TestMethod]
    public void Resolve_WithMultipleZones_UsesFirstZone()
    {
        var tmpl = BlueIrisUrlTemplate.Parse("x?z={zone}");
        var ctx = NewCtx(zones: ["a", "b"]);

        var result = tmpl.Resolve(ctx);

        result.Should().Be("x?z=a");
    }
}
