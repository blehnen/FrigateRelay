using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Host.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace FrigateRelay.Host.Tests.Configuration;

[TestClass]
public sealed class StartupValidationValidatorsTests
{
    [TestMethod]
    public void ValidateValidators_UndefinedKey_Throws()
    {
        // Subscription references "nonexistent" but no validator is registered under that key.
        var subs = new[]
        {
            new SubscriptionOptions
            {
                Name = "FrontDoor",
                Camera = "front",
                Label = "person",
                Actions =
                [
                    new ActionEntry("Pushover", null, ["nonexistent"]),
                ],
            },
        };

        var services = new ServiceCollection().BuildServiceProvider();
        var errors = new List<string>();

        StartupValidation.ValidateValidators(subs, services, errors);

        errors.Should().ContainSingle()
            .Which.Should().Contain("'nonexistent'").And.Contain("Subscription[0].Actions[0]");
    }

    [TestMethod]
    public void ValidateValidators_UnknownType_Throws()
    {
        // The chain: "weird" key references a top-level Validators instance with Type=MysteryAi.
        // No registrar claims MysteryAi → no keyed singleton → ValidateValidators sees null
        // and treats it the same as "undefined key". Operator-facing error tells them to ensure
        // each instance has a recognized Type.
        var subs = new[]
        {
            new SubscriptionOptions
            {
                Name = "FrontDoor",
                Camera = "front",
                Label = "person",
                Actions =
                [
                    new ActionEntry("Pushover", null, ["weird"]),
                ],
            },
        };

        var services = new ServiceCollection().BuildServiceProvider();
        var errors = new List<string>();

        StartupValidation.ValidateValidators(subs, services, errors);

        errors.Should().ContainSingle()
            .Which.Should().Contain("'weird'").And.Contain("recognized Type");
    }

    [TestMethod]
    public void ValidateValidators_AllKeysResolve_DoesNotThrow()
    {
        var registered = Substitute.For<IValidationPlugin>();
        registered.Name.Returns("strict-person");

        var services = new ServiceCollection()
            .AddKeyedSingleton<IValidationPlugin>("strict-person", (_, _) => registered)
            .BuildServiceProvider();

        var subs = new[]
        {
            new SubscriptionOptions
            {
                Name = "FrontDoor",
                Camera = "front",
                Label = "person",
                Actions =
                [
                    new ActionEntry("Pushover", null, ["strict-person"]),
                ],
            },
        };

        var errors = new List<string>();
        StartupValidation.ValidateValidators(subs, services, errors);

        errors.Should().BeEmpty();
    }
}
