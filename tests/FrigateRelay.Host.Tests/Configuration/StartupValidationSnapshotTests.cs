using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Host.Configuration;
using NSubstitute;

namespace FrigateRelay.Host.Tests.Configuration;

[TestClass]
public sealed class StartupValidationSnapshotTests
{
    private static ISnapshotProvider Provider(string name)
    {
        var p = Substitute.For<ISnapshotProvider>();
        p.Name.Returns(name);
        return p;
    }

    [TestMethod]
    public void ValidateSnapshotProviders_WithUnknownGlobalDefault_Throws()
    {
        var subs = Array.Empty<SubscriptionOptions>();
        var providers = new[] { Provider("BlueIris") };
        var errors = new List<string>();

        StartupValidation.ValidateSnapshotProviders(subs, "Frigate", providers, errors);

        errors.Should().ContainSingle()
            .Which.Should().Match("*Global Snapshots:DefaultProviderName*Frigate*BlueIris*");
    }

    [TestMethod]
    public void ValidateSnapshotProviders_WithUnknownSubscriptionDefault_Throws()
    {
        var subs = new[]
        {
            new SubscriptionOptions
            {
                Name = "FrontDoor",
                Camera = "front",
                Label = "person",
                DefaultSnapshotProvider = "DoesNotExist",
            },
        };
        var providers = new[] { Provider("BlueIris") };
        var errors = new List<string>();

        StartupValidation.ValidateSnapshotProviders(subs, globalDefaultProviderName: null, providers, errors);

        errors.Should().ContainSingle()
            .Which.Should().Match("*FrontDoor*DoesNotExist*BlueIris*");
    }

    [TestMethod]
    public void ValidateSnapshotProviders_WithUnknownPerActionOverride_Throws()
    {
        var subs = new[]
        {
            new SubscriptionOptions
            {
                Name = "FrontDoor",
                Camera = "front",
                Label = "person",
                Actions = [new ActionEntry("BlueIris", SnapshotProvider: "Mystery")],
            },
        };
        var providers = new[] { Provider("BlueIris"), Provider("Frigate") };
        var errors = new List<string>();

        StartupValidation.ValidateSnapshotProviders(subs, globalDefaultProviderName: null, providers, errors);

        errors.Should().ContainSingle()
            .Which.Should().Match("*FrontDoor*BlueIris*Mystery*Frigate*");
    }

    [TestMethod]
    public void ValidateSnapshotProviders_WithAllReferencesValid_DoesNotThrow()
    {
        var subs = new[]
        {
            new SubscriptionOptions
            {
                Name = "FrontDoor",
                Camera = "front",
                Label = "person",
                DefaultSnapshotProvider = "BlueIris",
                Actions = [new ActionEntry("BlueIris", SnapshotProvider: "Frigate")],
            },
        };
        var providers = new[] { Provider("BlueIris"), Provider("Frigate") };
        var errors = new List<string>();

        StartupValidation.ValidateSnapshotProviders(subs, globalDefaultProviderName: "BlueIris", providers, errors);

        errors.Should().BeEmpty();
    }
}
