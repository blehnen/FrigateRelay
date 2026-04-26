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

        var act = () => StartupValidation.ValidateActions(subs, plugins);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*FrontDoor*DoesNotExist*BlueIris*");
    }

    [TestMethod]
    public void ValidateActions_WithKnownActionName_ReturnsWithoutThrowing()
    {
        var subs = new[]
        {
            new SubscriptionOptions { Name = "FrontDoor", Camera = "front", Label = "person", Actions = [new ActionEntry("BlueIris")] },
        };
        var plugins = new IActionPlugin[] { new StubPlugin("BlueIris") };

        var act = () => StartupValidation.ValidateActions(subs, plugins);

        act.Should().NotThrow();
    }

    [TestMethod]
    public void ValidateActions_CaseInsensitiveOrdinalMatch_AcceptsMixedCase()
    {
        var subs = new[]
        {
            new SubscriptionOptions { Name = "FrontDoor", Camera = "front", Label = "person", Actions = [new ActionEntry("blueiris")] },
        };
        var plugins = new IActionPlugin[] { new StubPlugin("BlueIris") };

        var act = () => StartupValidation.ValidateActions(subs, plugins);

        act.Should().NotThrow();
    }

    [TestMethod]
    public void ValidateActions_WithEmptyActions_DoesNothing()
    {
        var subs = new[]
        {
            new SubscriptionOptions { Name = "FrontDoor", Camera = "front", Label = "person" },
        };

        var act = () => StartupValidation.ValidateActions(subs, Array.Empty<IActionPlugin>());

        act.Should().NotThrow();
    }

    private sealed class StubPlugin(string name) : IActionPlugin
    {
        public string Name { get; } = name;
        public Task ExecuteAsync(EventContext ctx, CancellationToken ct) => Task.CompletedTask;
    }
}
