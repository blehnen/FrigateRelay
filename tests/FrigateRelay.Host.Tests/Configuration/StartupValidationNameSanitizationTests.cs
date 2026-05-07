using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Host;
using FrigateRelay.Host.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace FrigateRelay.Host.Tests.Configuration;

/// <summary>
/// Tests that operator-controlled strings containing newline / carriage-return bytes
/// are escaped (not stripped) before they appear in <see cref="StartupValidation"/>
/// aggregated error messages (ID-13 closure, PLAN-1.1 Task 1).
/// </summary>
[TestClass]
public sealed class StartupValidationNameSanitizationTests
{
    // Minimal ServiceProvider that satisfies ValidateAll passes 2–4 without noise.
    private static ServiceProvider BuildMinimalServices()
    {
        var actionPlugin = Substitute.For<IActionPlugin>();
        actionPlugin.Name.Returns("Dummy");

        var snapshotProvider = Substitute.For<ISnapshotProvider>();
        snapshotProvider.Name.Returns("Dummy");

        return new ServiceCollection()
            .AddSingleton<IActionPlugin>(actionPlugin)
            .AddSingleton<ISnapshotProvider>(snapshotProvider)
            .BuildServiceProvider();
    }

    // -----------------------------------------------------------------------
    // Test 1: subscription Name containing \n — error message must NOT contain
    //         a literal newline byte (the injection surface is escaped).
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ValidateAll_SubscriptionNameWithNewline_ErrorMessageDoesNotContainRawNewline()
    {
        var services = BuildMinimalServices();

        // A subscription that is also broken (no Profile, no Actions) so ValidateAll
        // will definitely emit an error referencing sub.Name.
        var options = new HostSubscriptionsOptions
        {
            Subscriptions =
            [
                new SubscriptionOptions
                {
                    Name = "Bad\nName",
                    Camera = "driveway",
                    Label = "person",
                    // Neither Profile nor Actions — triggers the mutex error through ProfileResolver.
                }
            ]
        };

        var act = () => StartupValidation.ValidateAll(services, options);

        var ex = act.Should().Throw<InvalidOperationException>()
            .Which;

        // The raw '\n' byte must NOT appear inside the operator-controlled portion of the
        // message (the sanitized form "\\n" may appear, but not the control character).
        // We strip the fixed header "Startup configuration invalid:\n  - " before checking
        // so the collect-all separator newlines do not trigger a false positive.
        var messageAfterHeader = ex.Message.Replace("Startup configuration invalid:", "")
                                           .Replace("\n  - ", " | ");

        messageAfterHeader.Should().NotContain("\n",
            "operator-controlled newlines must be escaped to the literal '\\n' sequence, " +
            "not passed through as raw bytes");

        // Confirm the escaped form is present so we know the value was preserved (not stripped).
        ex.Message.Should().Contain(@"\n",
            "the escaped form '\\n' must appear in the error so operators can see what they typed");
    }

    // -----------------------------------------------------------------------
    // Test 2: Validators instance key containing \r — diagnostic must escape it.
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ValidateAll_ValidatorKeyWithCarriageReturn_ErrorMessageDoesNotContainRawCr()
    {
        var actionPlugin = Substitute.For<IActionPlugin>();
        actionPlugin.Name.Returns("Dummy");

        var snapshotProvider = Substitute.For<ISnapshotProvider>();
        snapshotProvider.Name.Returns("Dummy");

        // No IValidationPlugin registered under key "Bad\rKey" → ValidateValidators fires.
        var services = new ServiceCollection()
            .AddSingleton<IActionPlugin>(actionPlugin)
            .AddSingleton<ISnapshotProvider>(snapshotProvider)
            .BuildServiceProvider();

        var options = new HostSubscriptionsOptions
        {
            Subscriptions =
            [
                new SubscriptionOptions
                {
                    Name = "TestSub",
                    Camera = "driveway",
                    Label = "person",
                    Actions =
                    [
                        new ActionEntry("Dummy")
                        {
                            Validators = ["Bad\rKey"]
                        }
                    ]
                }
            ]
        };

        var act = () => StartupValidation.ValidateAll(services, options);

        var ex = act.Should().Throw<InvalidOperationException>()
            .Which;

        var messageAfterHeader = ex.Message.Replace("Startup configuration invalid:", "")
                                           .Replace("\n  - ", " | ");

        messageAfterHeader.Should().NotContain("\r",
            "operator-controlled carriage-returns must be escaped to '\\r', not passed raw");

        ex.Message.Should().Contain(@"\r",
            "the escaped form '\\r' must appear in the error so operators can see what they typed");
    }
}
