using System.Text.Json;
using FluentAssertions;
using FrigateRelay.Host.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Host.Tests.Configuration;

[TestClass]
public sealed class ActionEntryJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new ActionEntryJsonConverter() },
    };

    [TestMethod]
    public void Read_StringForm_ReturnsActionEntry_WithPluginOnly()
    {
        var result = JsonSerializer.Deserialize<ActionEntry>("\"BlueIris\"", Options);

        Assert.IsNotNull(result);
        Assert.AreEqual("BlueIris", result.Plugin);
        Assert.IsNull(result.SnapshotProvider);
    }

    [TestMethod]
    public void Read_ObjectForm_ReturnsActionEntry_WithPluginAndProvider()
    {
        var json = """{"Plugin":"Pushover","SnapshotProvider":"Frigate"}""";
        var result = JsonSerializer.Deserialize<ActionEntry>(json, Options);

        Assert.IsNotNull(result);
        Assert.AreEqual("Pushover", result.Plugin);
        Assert.AreEqual("Frigate", result.SnapshotProvider);
    }

    [TestMethod]
    public void Read_ObjectForm_OmittedSnapshotProvider_DefaultsToNull()
    {
        var json = """{"Plugin":"BlueIris"}""";
        var result = JsonSerializer.Deserialize<ActionEntry>(json, Options);

        Assert.IsNotNull(result);
        Assert.AreEqual("BlueIris", result.Plugin);
        Assert.IsNull(result.SnapshotProvider);
    }

    [TestMethod]
    public void Read_InvalidToken_ThrowsJsonException()
    {
        // Number token is not a valid ActionEntry representation.
        var act = () => JsonSerializer.Deserialize<ActionEntry>("42", Options);
        act.Should().Throw<JsonException>();
    }

    [TestMethod]
    public void Write_ProducesObjectForm_RoundTripsCorrectly()
    {
        var entry = new ActionEntry("BlueIris", "Frigate");
        var json = JsonSerializer.Serialize(entry, Options);
        var result = JsonSerializer.Deserialize<ActionEntry>(json, Options);

        Assert.IsNotNull(result);
        Assert.AreEqual(entry.Plugin, result.Plugin);
        Assert.AreEqual(entry.SnapshotProvider, result.SnapshotProvider);
    }
}
