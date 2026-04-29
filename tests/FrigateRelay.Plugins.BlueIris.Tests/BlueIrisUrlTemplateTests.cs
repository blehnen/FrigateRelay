using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Plugins.BlueIris;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Plugins.BlueIris.Tests;

[TestClass]
public class BlueIrisUrlTemplateTests
{
    private static EventContext NewCtx(string camera = "front", string label = "person",
        string eventId = "ev-1", IReadOnlyList<string>? zones = null) => new()
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
