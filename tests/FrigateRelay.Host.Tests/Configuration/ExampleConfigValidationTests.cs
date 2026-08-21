using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Host.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace FrigateRelay.Host.Tests.Configuration;

/// <summary>
/// Guards the shipped <c>config/appsettings.Example.json</c>: it must bind to
/// <see cref="HostSubscriptionsOptions"/> and pass <see cref="StartupValidation.ValidateAll"/>.
/// The example is the first thing a new operator copies, so a profile name, plugin name, or
/// snapshot provider name that drifts out of sync with the code should fail the build here
/// rather than at their first startup.
/// </summary>
[TestClass]
public sealed class ExampleConfigValidationTests
{
    [TestMethod]
    public void ExampleConfig_BindsAndPassesStartupValidation()
    {
        var jsonPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "appsettings.Example.json");

        File.Exists(jsonPath).Should().BeTrue(
            $"the example config must be copied to the test output at {jsonPath}");

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(jsonPath, optional: false)
            .Build();

        var options = new HostSubscriptionsOptions();
        configuration.Bind(options);

        // Stub IServiceProvider with the plugins named in the example:
        // BlueIris + Pushover as action plugins; Frigate as snapshot provider;
        // CodeProjectAi as keyed validation plugin.
        var services = new ServiceCollection()
            .AddSingleton<IEnumerable<IActionPlugin>>(
                [ActionPlugin("BlueIris"), ActionPlugin("Pushover")])
            .AddSingleton<IEnumerable<ISnapshotProvider>>([SnapshotProvider("Frigate")])
            .AddKeyedSingleton("CodeProjectAi", ValidationPlugin("CodeProjectAi"))
            .AddOptions()
            .BuildServiceProvider();

        // This must not throw — any structural error in the example config surfaces here.
        var act = () => StartupValidation.ValidateAll(services, options);
        act.Should().NotThrow(
            "appsettings.Example.json must be structurally valid — if this fails, " +
            "a profile name, plugin name, or snapshot provider name in the example " +
            "does not match the registered stubs.");
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

    private static IValidationPlugin ValidationPlugin(string name)
    {
        var p = Substitute.For<IValidationPlugin>();
        p.Name.Returns(name);
        return p;
    }
}
