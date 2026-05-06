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

    // -------------------------------------------------------------------------
    // ParallelValidators round-trip tests (PLAN-3.1 Task 1, REVIEW-3.1 gap-fill)
    //
    // The 6 tests in ActionEntryJsonConverterTests cover the JsonSerializer.Deserialize
    // path. These three cover the operator-facing IConfiguration.Bind path — which is the
    // path that actually loads appsettings.json at startup. CONTEXT-14 D5 requires
    // ParallelValidators to default to false on every binding shape so existing v1.0/v1.1
    // configs upgrade unchanged.
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Bind_ObjectForm_WithParallelValidatorsTrue_BindsCorrectly()
    {
        // Operator opts in to parallel mode on a single action via object form.
        var result = Bind("""{"Actions":[{"Plugin":"Pushover","Validators":["cpai","roboflow"],"ParallelValidators":true}]}""");

        result.Actions.Should().HaveCount(1);
        result.Actions[0].Plugin.Should().Be("Pushover");
        result.Actions[0].Validators.Should().BeEquivalentTo(["cpai", "roboflow"]);
        result.Actions[0].ParallelValidators.Should().BeTrue("operator wrote ParallelValidators=true");
    }

    [TestMethod]
    public void Bind_ObjectForm_WithoutParallelValidators_DefaultsFalse()
    {
        // Backward-compat invariant: existing v1.1 object-form config without the new field
        // must continue to bind with ParallelValidators=false (CONTEXT-14 D5).
        var result = Bind("""{"Actions":[{"Plugin":"Pushover","Validators":["cpai"]}]}""");

        result.Actions.Should().HaveCount(1);
        result.Actions[0].ParallelValidators.Should().BeFalse(
            "missing field must default to false for backward compat with v1.0/v1.1 configs");
    }

    [TestMethod]
    public void Bind_StringShorthand_ParallelValidatorsDefaultsFalse()
    {
        // Backward-compat invariant: existing v1.0 string-shorthand config must continue to
        // bind with ParallelValidators=false (the TypeConverter constructs `new ActionEntry(name)`
        // and relies on the record's positional defaults for the new field).
        var result = Bind("""{"Actions":["BlueIris"]}""");

        result.Actions.Should().HaveCount(1);
        result.Actions[0].Plugin.Should().Be("BlueIris");
        result.Actions[0].ParallelValidators.Should().BeFalse(
            "string-shorthand → TypeConverter → new ActionEntry(name) must use positional defaults");
    }
}
