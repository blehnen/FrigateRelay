using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Host;
using FrigateRelay.Host.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Host.Tests.Configuration;

/// <summary>
/// Unit tests for <see cref="StartupValidation.ValidateNames"/> (ID-19 closure, PLAN-1.1 Task 2 Part A).
/// Enforces the D1 permissive-printable regex <c>^[A-Za-z0-9_. -]+$</c> across all four
/// name kinds: subscription names, profile keys, plugin names, and validator instance keys.
/// </summary>
[TestClass]
public sealed class StartupValidationNameAllowlistTests
{
    // Helper: build minimal options with one subscription whose Name triggers the check.
    private static HostSubscriptionsOptions OptionsWithSubscriptionName(string name) =>
        new()
        {
            Subscriptions =
            [
                new SubscriptionOptions
                {
                    Name = name,
                    Camera = "driveway",
                    Label = "person",
                    Actions = [new ActionEntry("Dummy")]
                }
            ]
        };

    // -----------------------------------------------------------------------
    // Test 1: subscription name containing '\n' → rejected with "subscription" in message
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ValidateNames_SubscriptionNameWithNewline_RejectsWithSubscriptionInMessage()
    {
        var options = OptionsWithSubscriptionName("Bad\nName");
        var errors = new List<string>();

        StartupValidation.ValidateNames(options, errors);

        errors.Should().ContainSingle("exactly one name error for a newline in subscription name");
        errors[0].Should().ContainEquivalentOf("subscription",
            "error message must identify the kind of name that was rejected");
    }

    // -----------------------------------------------------------------------
    // Test 2: profile key containing '/' → rejected with "profile" in message
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ValidateNames_ProfileKeyWithSlash_RejectsWithProfileInMessage()
    {
        var options = new HostSubscriptionsOptions
        {
            Profiles = new Dictionary<string, ProfileOptions>
            {
                ["Bad/Profile"] = new ProfileOptions { Actions = [new ActionEntry("Dummy")] }
            },
            Subscriptions = []
        };
        var errors = new List<string>();

        StartupValidation.ValidateNames(options, errors);

        errors.Should().ContainSingle("exactly one name error for a slash in profile key");
        errors[0].Should().ContainEquivalentOf("profile",
            "error message must identify the kind of name that was rejected");
    }

    // -----------------------------------------------------------------------
    // Test 3: plugin name containing ':' → rejected with "plugin" in message
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ValidateNames_PluginNameWithColon_RejectsWithPluginInMessage()
    {
        var options = new HostSubscriptionsOptions
        {
            Subscriptions =
            [
                new SubscriptionOptions
                {
                    Name = "GoodSub",
                    Camera = "driveway",
                    Label = "person",
                    Actions = [new ActionEntry("Bad:Plugin")]
                }
            ]
        };
        var errors = new List<string>();

        StartupValidation.ValidateNames(options, errors);

        errors.Should().ContainSingle("exactly one name error for a colon in plugin name");
        errors[0].Should().ContainEquivalentOf("plugin",
            "error message must identify the kind of name that was rejected");
    }

    // -----------------------------------------------------------------------
    // Test 4: spaced subscription name "DriveWay Person" → accepted (zero errors)
    //         Confirms D1 permissive regex preserves the existing appsettings.Example.json shape.
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ValidateNames_SpacedSubscriptionName_Accepted()
    {
        var options = OptionsWithSubscriptionName("DriveWay Person");
        var errors = new List<string>();

        StartupValidation.ValidateNames(options, errors);

        errors.Should().BeEmpty(
            "the D1 permissive-printable regex allows spaces; 'DriveWay Person' must not be rejected");
    }

    // -----------------------------------------------------------------------
    // Test 5 (CodeRabbit-flagged regression): whitespace-only subscription name → rejected
    //         Space is in the allowlist [A-Za-z0-9_. -], so without an explicit IsNullOrWhiteSpace
    //         guard, "   " would silently pass NameAllowlist.IsMatch and reach ProfileResolver.
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ValidateNames_WhitespaceOnlySubscriptionName_Rejected()
    {
        var options = OptionsWithSubscriptionName("   ");
        var errors = new List<string>();

        StartupValidation.ValidateNames(options, errors);

        errors.Should().ContainSingle("whitespace-only name must not bypass the allowlist");
        errors[0].Should().ContainEquivalentOf("subscription",
            "error message must identify the kind of name that was rejected");
    }
}
