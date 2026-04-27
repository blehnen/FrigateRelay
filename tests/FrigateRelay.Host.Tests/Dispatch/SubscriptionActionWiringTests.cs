using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Host.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Host.Tests.Dispatch;

[TestClass]
public sealed class SubscriptionActionWiringTests
{
    [TestMethod]
    public void ValidateActions_WithUnknownActionName_ThrowsInvalidOperationException_ListingNameAndRegisteredPlugins()
    {
        var subs = new[]
        {
            new SubscriptionOptions { Name = "FrontDoor", Camera = "front", Label = "person", Actions = [new ActionEntry("DoesNotExist")] },
        };
        var plugins = new IActionPlugin[] { new StubPlugin("BlueIris") };
        var errors = new List<string>();

        StartupValidation.ValidateActions(subs, plugins, errors);

        errors.Should().ContainSingle()
            .Which.Should().Match("*FrontDoor*DoesNotExist*BlueIris*");
    }

    [TestMethod]
    public void ValidateActions_WithKnownActionName_ReturnsWithoutThrowing()
    {
        var subs = new[]
        {
            new SubscriptionOptions { Name = "FrontDoor", Camera = "front", Label = "person", Actions = [new ActionEntry("BlueIris")] },
        };
        var plugins = new IActionPlugin[] { new StubPlugin("BlueIris") };
        var errors = new List<string>();

        StartupValidation.ValidateActions(subs, plugins, errors);

        errors.Should().BeEmpty();
    }

    [TestMethod]
    public void ValidateActions_CaseInsensitiveOrdinalMatch_AcceptsMixedCase()
    {
        var subs = new[]
        {
            new SubscriptionOptions { Name = "FrontDoor", Camera = "front", Label = "person", Actions = [new ActionEntry("blueiris")] },
        };
        var plugins = new IActionPlugin[] { new StubPlugin("BlueIris") };
        var errors = new List<string>();

        StartupValidation.ValidateActions(subs, plugins, errors);

        errors.Should().BeEmpty();
    }

    [TestMethod]
    public void ValidateActions_WithEmptyActions_DoesNothing()
    {
        var subs = new[]
        {
            new SubscriptionOptions { Name = "FrontDoor", Camera = "front", Label = "person" },
        };
        var errors = new List<string>();

        StartupValidation.ValidateActions(subs, Array.Empty<IActionPlugin>(), errors);

        errors.Should().BeEmpty();
    }

    private sealed class StubPlugin(string name) : IActionPlugin
    {
        public string Name { get; } = name;
        public Task ExecuteAsync(EventContext ctx, SnapshotContext snapshot, CancellationToken ct) => Task.CompletedTask;
    }
}
