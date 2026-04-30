using FluentAssertions;
using FrigateRelay.Host.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Host.Tests.Configuration;

/// <summary>
/// Regression tests for issue #20 — confirms <c>HostBootstrap.ConfigureServices</c>
/// registers an <c>IPostConfigureOptions&lt;HostSubscriptionsOptions&gt;</c> that runs
/// <c>ProfileResolver.Resolve</c> so runtime consumers (EventPump) see expanded
/// <see cref="SubscriptionOptions.Actions"/> on profile-based subscriptions.
/// </summary>
/// <remarks>
/// Pre-fix behavior (rc1, rc2): every profile-based subscription matched events correctly
/// but iterated an empty <c>Actions</c> list in the dispatch loop, silently dropping all
/// actions. The defect was missed because <see cref="ProfileResolver"/> was already covered
/// by unit tests in isolation, and integration tests used inline-Actions configs that
/// never exercised the profile-resolution path through DI.
/// </remarks>
[TestClass]
public sealed class ProfileResolutionPostConfigureTests
{
    [TestMethod]
    public void HostBootstrap_ProfileBasedSubscription_ExpandsToConcreteActions()
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FrigateMqtt:Server"] = "localhost",
            ["Profiles:Standard:Actions:0:Plugin"] = "BlueIris",
            ["Profiles:Standard:Actions:1:Plugin"] = "Pushover",
            ["Subscriptions:0:Name"] = "Drive",
            ["Subscriptions:0:Camera"] = "driveway",
            ["Subscriptions:0:Label"] = "person",
            ["Subscriptions:0:Profile"] = "Standard",
        });

        HostBootstrap.ConfigureServices(builder);
        using var app = builder.Build();

        var monitor = app.Services.GetRequiredService<IOptionsMonitor<HostSubscriptionsOptions>>();
        var sub = monitor.CurrentValue.Subscriptions.Should().ContainSingle().Which;

        sub.Profile.Should().BeNull("ProfileResolver clears Profile after expansion");
        sub.Actions.Should().HaveCount(2);
        sub.Actions.Select(a => a.Plugin).Should().Equal("BlueIris", "Pushover");
    }

    [TestMethod]
    public void HostBootstrap_InlineActionsSubscription_PreservedUnchanged()
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FrigateMqtt:Server"] = "localhost",
            ["Subscriptions:0:Name"] = "Drive",
            ["Subscriptions:0:Camera"] = "driveway",
            ["Subscriptions:0:Label"] = "person",
            ["Subscriptions:0:Actions:0:Plugin"] = "BlueIris",
        });

        HostBootstrap.ConfigureServices(builder);
        using var app = builder.Build();

        var monitor = app.Services.GetRequiredService<IOptionsMonitor<HostSubscriptionsOptions>>();
        var sub = monitor.CurrentValue.Subscriptions.Should().ContainSingle().Which;

        sub.Actions.Should().ContainSingle().Which.Plugin.Should().Be("BlueIris");
    }
}
