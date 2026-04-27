using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Host.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace FrigateRelay.Host.Tests.Configuration;

/// <summary>
/// Tests for profile expansion (D1, D5, D7) and collect-all startup validation (PLAN-2.1).
/// All tests use the <c>ProfileResolver</c> directly or <c>StartupValidation.ValidateAll</c>
/// against a stub <see cref="IServiceProvider"/>.
/// </summary>
[TestClass]
public sealed class ProfileResolutionTests
{
    // ─── helpers ───────────────────────────────────────────────────────────────

    private static HostSubscriptionsOptions BuildOptions(
        Dictionary<string, ProfileOptions>? profiles = null,
        IEnumerable<SubscriptionOptions>? subscriptions = null)
    {
        return new HostSubscriptionsOptions
        {
            Profiles = profiles ?? new Dictionary<string, ProfileOptions>(),
            Subscriptions = (subscriptions ?? Array.Empty<SubscriptionOptions>()).ToArray(),
        };
    }

    private static IActionPlugin ActionPlugin(string name)
    {
        var p = Substitute.For<IActionPlugin>();
        p.Name.Returns(name);
        return p;
    }

    private static ISnapshotProvider SnapshotProvider(string name)
    {
        var p = Substitute.For<ISnapshotProvider>();
        p.Name.Returns(name);
        return p;
    }

    private static IValidationPlugin ValidationPlugin(string key)
    {
        var p = Substitute.For<IValidationPlugin>();
        p.Name.Returns(key);
        return p;
    }

    // ─── Task 2 tests ──────────────────────────────────────────────────────────

    /// <summary>Test 1 — profile-only subscription uses the profile's actions.</summary>
    [TestMethod]
    public void Resolve_ProfileOnlySubscription_UsesProfileActions()
    {
        var profileActions = new[] { new ActionEntry("BlueIris"), new ActionEntry("Pushover") };
        var options = BuildOptions(
            profiles: new Dictionary<string, ProfileOptions>
            {
                ["Standard"] = new ProfileOptions { Actions = profileActions },
            },
            subscriptions: new[]
            {
                new SubscriptionOptions { Name = "FrontDoor", Camera = "front", Label = "person", Profile = "Standard" },
            });

        var errors = new List<string>();
        var resolved = ProfileResolver.Resolve(options, errors);

        errors.Should().BeEmpty();
        resolved.Should().HaveCount(1);
        resolved[0].Actions.Should().BeEquivalentTo(profileActions);
        resolved[0].Profile.Should().BeNull("profile field is cleared after expansion");
    }

    /// <summary>Test 2 — inline-only subscription preserves its inline actions.</summary>
    [TestMethod]
    public void Resolve_InlineOnlySubscription_UsesInlineActions()
    {
        var inlineActions = new[] { new ActionEntry("Pushover") };
        var options = BuildOptions(
            subscriptions: new[]
            {
                new SubscriptionOptions { Name = "Driveway", Camera = "drive", Label = "car", Actions = inlineActions },
            });

        var errors = new List<string>();
        var resolved = ProfileResolver.Resolve(options, errors);

        errors.Should().BeEmpty();
        resolved.Should().HaveCount(1);
        resolved[0].Actions.Should().BeEquivalentTo(inlineActions);
    }

    /// <summary>Test 3 — mixed subscriptions all resolve independently in one pass.</summary>
    [TestMethod]
    public void Resolve_MixedSubscriptions_ResolveIndependently()
    {
        var stdActions = new[] { new ActionEntry("BlueIris") };
        var valActions = new[] { new ActionEntry("Pushover", null, ["CpAi"]) };
        var inlineActions = new[] { new ActionEntry("Pushover") };

        var options = BuildOptions(
            profiles: new Dictionary<string, ProfileOptions>
            {
                ["Standard"] = new ProfileOptions { Actions = stdActions },
                ["Validated"] = new ProfileOptions { Actions = valActions },
            },
            subscriptions: new[]
            {
                new SubscriptionOptions { Name = "Sub1", Camera = "c1", Label = "person", Profile = "Standard" },
                new SubscriptionOptions { Name = "Sub2", Camera = "c2", Label = "car", Actions = inlineActions },
                new SubscriptionOptions { Name = "Sub3", Camera = "c3", Label = "person", Profile = "Validated" },
            });

        var errors = new List<string>();
        var resolved = ProfileResolver.Resolve(options, errors);

        errors.Should().BeEmpty();
        resolved.Should().HaveCount(3);
        resolved[0].Actions.Should().BeEquivalentTo(stdActions);
        resolved[1].Actions.Should().BeEquivalentTo(inlineActions);
        resolved[2].Actions.Should().BeEquivalentTo(valActions);
    }

    /// <summary>Test 4 — D1 mutex: both Profile and Actions set reports error.</summary>
    [TestMethod]
    public void Resolve_BothProfileAndActionsSet_ReportsMutexError()
    {
        var options = BuildOptions(
            profiles: new Dictionary<string, ProfileOptions>
            {
                ["Standard"] = new ProfileOptions { Actions = [new ActionEntry("BlueIris")] },
            },
            subscriptions: new[]
            {
                new SubscriptionOptions
                {
                    Name = "Bad",
                    Camera = "c1",
                    Label = "person",
                    Profile = "Standard",
                    Actions = [new ActionEntry("Pushover")],
                },
            });

        var errors = new List<string>();
        var resolved = ProfileResolver.Resolve(options, errors);

        errors.Should().ContainSingle();
        errors[0].Should().Contain("'Bad'");
        errors[0].Should().Contain("may declare either 'Profile' or 'Actions', not both");
        resolved.Should().BeEmpty("errored subscriptions are not emitted");
    }

    /// <summary>Test 5 — D1 mutex: neither Profile nor Actions set reports error.</summary>
    [TestMethod]
    public void Resolve_NeitherProfileNorActions_ReportsMissingError()
    {
        var options = BuildOptions(
            subscriptions: new[]
            {
                new SubscriptionOptions { Name = "Empty", Camera = "c1", Label = "person" },
            });

        var errors = new List<string>();
        ProfileResolver.Resolve(options, errors);

        errors.Should().ContainSingle();
        errors[0].Should().Contain("'Empty'");
        errors[0].Should().Contain("must declare either 'Profile' or 'Actions'");
    }

    /// <summary>Test 6 — undefined profile reference reports error.</summary>
    [TestMethod]
    public void Resolve_UndefinedProfileReference_ReportsUndefinedError()
    {
        var options = BuildOptions(
            profiles: new Dictionary<string, ProfileOptions>
            {
                ["Real"] = new ProfileOptions { Actions = [new ActionEntry("BlueIris")] },
            },
            subscriptions: new[]
            {
                new SubscriptionOptions { Name = "Sub1", Camera = "c1", Label = "person", Profile = "Ghost" },
            });

        var errors = new List<string>();
        ProfileResolver.Resolve(options, errors);

        errors.Should().ContainSingle();
        errors[0].Should().Contain("references undefined profile 'Ghost'");
    }

    /// <summary>Test 7 — profile action with unknown plugin name produces plugin error in ValidateAll.</summary>
    [TestMethod]
    public void ValidateAll_ProfileActionUnknownPlugin_ReportsPluginError()
    {
        var options = BuildOptions(
            profiles: new Dictionary<string, ProfileOptions>
            {
                ["Standard"] = new ProfileOptions { Actions = [new ActionEntry("Bogus")] },
            },
            subscriptions: new[]
            {
                new SubscriptionOptions { Name = "Sub1", Camera = "c1", Label = "person", Profile = "Standard" },
            });

        // No IActionPlugin named "Bogus" registered.
        var services = new ServiceCollection()
            .AddSingleton<IEnumerable<IActionPlugin>>(Array.Empty<IActionPlugin>())
            .AddSingleton<IEnumerable<ISnapshotProvider>>(Array.Empty<ISnapshotProvider>())
            .BuildServiceProvider();

        var act = () => StartupValidation.ValidateAll(services, options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*unknown action plugin 'Bogus'*");
    }

    /// <summary>Test 8 — profile action with unknown validator key produces validator error in ValidateAll.</summary>
    [TestMethod]
    public void ValidateAll_ProfileActionUnknownValidator_ReportsValidatorError()
    {
        var options = BuildOptions(
            profiles: new Dictionary<string, ProfileOptions>
            {
                ["Validated"] = new ProfileOptions
                {
                    Actions = [new ActionEntry("BlueIris", null, ["Bogus"])],
                },
            },
            subscriptions: new[]
            {
                new SubscriptionOptions { Name = "Sub1", Camera = "c1", Label = "person", Profile = "Validated" },
            });

        var blueIris = ActionPlugin("BlueIris");
        var services = new ServiceCollection()
            .AddSingleton<IEnumerable<IActionPlugin>>(new[] { blueIris })
            .AddSingleton<IEnumerable<ISnapshotProvider>>(Array.Empty<ISnapshotProvider>())
            .BuildServiceProvider();

        var act = () => StartupValidation.ValidateAll(services, options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'Bogus'*");
    }

    /// <summary>Test 9 — profile action with unknown snapshot provider name produces provider error in ValidateAll.</summary>
    [TestMethod]
    public void ValidateAll_ProfileActionUnknownSnapshotProvider_ReportsProviderError()
    {
        var options = BuildOptions(
            profiles: new Dictionary<string, ProfileOptions>
            {
                ["Standard"] = new ProfileOptions
                {
                    Actions = [new ActionEntry("BlueIris", SnapshotProvider: "Bogus")],
                },
            },
            subscriptions: new[]
            {
                new SubscriptionOptions { Name = "Sub1", Camera = "c1", Label = "person", Profile = "Standard" },
            });

        var blueIris = ActionPlugin("BlueIris");
        var services = new ServiceCollection()
            .AddSingleton<IEnumerable<IActionPlugin>>(new[] { blueIris })
            .AddSingleton<IEnumerable<ISnapshotProvider>>(Array.Empty<ISnapshotProvider>())
            .BuildServiceProvider();

        var act = () => StartupValidation.ValidateAll(services, options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Bogus*");
    }

    /// <summary>Test 10 — multiple errors are aggregated into a single exception.</summary>
    [TestMethod]
    public void ValidateAll_MultipleErrors_AggregatesAllInOneException()
    {
        // Three errors:
        // 1. Sub2 references missing profile "Ghost" (profile resolution error)
        // 2. Sub1's profile action references unknown plugin "MissingPlugin" (action validation)
        // 3. Sub1's profile action references unknown validator "MissingValidator" (validator validation)
        var options = BuildOptions(
            profiles: new Dictionary<string, ProfileOptions>
            {
                ["Standard"] = new ProfileOptions
                {
                    Actions = [new ActionEntry("MissingPlugin", null, ["MissingValidator"])],
                },
            },
            subscriptions: new[]
            {
                new SubscriptionOptions { Name = "Sub1", Camera = "c1", Label = "person", Profile = "Standard" },
                new SubscriptionOptions { Name = "Sub2", Camera = "c2", Label = "car", Profile = "Ghost" },
            });

        // No plugins, providers, or validators registered.
        var services = new ServiceCollection()
            .AddSingleton<IEnumerable<IActionPlugin>>(Array.Empty<IActionPlugin>())
            .AddSingleton<IEnumerable<ISnapshotProvider>>(Array.Empty<ISnapshotProvider>())
            .BuildServiceProvider();

        var act = () => StartupValidation.ValidateAll(services, options);

        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().StartWith("Startup configuration invalid:");
        ex.Message.Should().Contain("Ghost");
        ex.Message.Should().Contain("MissingPlugin");
        ex.Message.Should().Contain("MissingValidator");
    }
}
