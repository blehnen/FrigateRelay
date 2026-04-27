using System.Text;
using FluentAssertions;
using FrigateRelay.Host.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Host.Tests.Configuration;

/// <summary>
/// Verifies that IConfiguration.Bind correctly converts scalar strings in an
/// Actions array to ActionEntry via the ActionEntryTypeConverter (ID-12 fix).
/// Red phase: all 3 tests fail before ActionEntryTypeConverter exists.
/// Green phase: all 3 tests pass after the converter is registered on ActionEntry.
/// </summary>
[TestClass]
public sealed class ActionEntryTypeConverterTests
{
    // Helper POCO — minimal shape that mirrors SubscriptionOptions.Actions
    private sealed class ActionListWrapper
    {
        public IReadOnlyList<ActionEntry> Actions { get; init; } = [];
    }

    private static ActionListWrapper Bind(string json)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var config = new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();

        var wrapper = new ActionListWrapper();
        config.Bind(wrapper);
        return wrapper;
    }

    [TestMethod]
    public void Bind_StringArrayActions_PopulatesEntries()
    {
        // ID-12: string-array form was silently dropped by IConfiguration.Bind before the fix.
        var result = Bind("""{"Actions":["BlueIris","Pushover"]}""");

        result.Actions.Should().HaveCount(2);
        result.Actions[0].Plugin.Should().Be("BlueIris");
        result.Actions[0].SnapshotProvider.Should().BeNull();
        result.Actions[0].Validators.Should().BeNullOrEmpty();
        result.Actions[1].Plugin.Should().Be("Pushover");
        result.Actions[1].SnapshotProvider.Should().BeNull();
        result.Actions[1].Validators.Should().BeNullOrEmpty();
    }

    [TestMethod]
    public void Bind_ObjectArrayActions_PopulatesEntries()
    {
        // Regression check: object-form binding must continue to work after fix.
        var result = Bind("""{"Actions":[{"Plugin":"BlueIris"},{"Plugin":"Pushover","SnapshotProvider":"Frigate"}]}""");

        result.Actions.Should().HaveCount(2);
        result.Actions[0].Plugin.Should().Be("BlueIris");
        result.Actions[0].SnapshotProvider.Should().BeNull();
        result.Actions[1].Plugin.Should().Be("Pushover");
        result.Actions[1].SnapshotProvider.Should().Be("Frigate");
    }

    [TestMethod]
    public void Bind_MixedStringAndObjectActions_PopulatesEntries()
    {
        // Mixed array: first element is a scalar string, second is a full object.
        var result = Bind("""{"Actions":["BlueIris",{"Plugin":"Pushover","SnapshotProvider":"Frigate","Validators":["CodeProjectAi"]}]}""");

        result.Actions.Should().HaveCount(2);

        // String element → Plugin set, other fields default
        result.Actions[0].Plugin.Should().Be("BlueIris");
        result.Actions[0].SnapshotProvider.Should().BeNull();
        result.Actions[0].Validators.Should().BeNullOrEmpty();

        // Object element → all fields preserved
        result.Actions[1].Plugin.Should().Be("Pushover");
        result.Actions[1].SnapshotProvider.Should().Be("Frigate");
        result.Actions[1].Validators.Should().BeEquivalentTo(["CodeProjectAi"]);
    }
}
