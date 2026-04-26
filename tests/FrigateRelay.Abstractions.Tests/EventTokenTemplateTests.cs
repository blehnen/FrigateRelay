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
        IReadOnlyList<string>? zones = null) => new()
    {
        EventId = eventId,
        Camera = camera,
        Label = label,
        Zones = zones ?? Array.Empty<string>(),
        StartedAt = DateTimeOffset.UnixEpoch,
        RawPayload = "{}",
        SnapshotFetcher = static _ => ValueTask.FromResult<byte[]?>(null),
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
}
