using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Host.Configuration;
using FrigateRelay.Host.Snapshots;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace FrigateRelay.Host.Tests.Configuration;

/// <summary>
/// Phase 8 Success Criterion #2: the Profiles-shaped JSON example must be
/// meaningfully shorter than the legacy INI it replaces.
/// Gate: JSON char count &lt;= 60% of INI char count (D3, D9).
/// </summary>
[TestClass]
public sealed class ConfigSizeParityTest
{
    /// <summary>
    /// Verifies that <c>appsettings.Example.json</c> is at most 60% the character
    /// count of <c>legacy.conf</c>, proving the Profiles shape eliminates INI repetition.
    /// Per D9: hard-fails (no skip) when the INI fixture is absent.
    /// Also binds + validates the example JSON to prove it is structurally correct,
    /// not merely short.
    /// </summary>
    [TestMethod]
    public void Json_Is_At_Most_60_Percent_Of_Ini_Character_Count()
    {
        var iniPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy.conf");
        var jsonPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "appsettings.Example.json");

        // D9: hard fail — no Assert.Inconclusive, no environment branch.
        if (!File.Exists(iniPath))
        {
            Assert.Fail(
                "legacy.conf fixture missing at " + iniPath + ". " +
                "Sanitize your real FrigateMQTTProcessingService.conf per " +
                ".shipyard/phases/8/SANITIZATION-CHECKLIST.md and place the " +
                "redacted result at the path above. This test cannot run without it.");
        }

        // D3: raw char count, no whitespace stripping or normalization.
        var iniLength = File.ReadAllText(iniPath).Length;
        var jsonLength = File.ReadAllText(jsonPath).Length;
        var ratio = (double)jsonLength / iniLength;

        ratio.Should().BeLessOrEqualTo(0.60,
            $"JSON ({jsonLength} chars) must be <= 60% of INI ({iniLength} chars); ratio was {ratio:P1}");

        // Sub-assertion: example JSON must also bind and validate successfully.
        // Proves the example stays structurally correct, not just short.
        Json_Binds_And_Validates_Successfully(jsonPath);
    }

    private static void Json_Binds_And_Validates_Successfully(string jsonPath)
    {
        // Build IConfiguration from the example file only.
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(jsonPath, optional: false)
            .Build();

        var options = new HostSubscriptionsOptions();
        configuration.Bind(options);

        // Stub IServiceProvider with the plugins named in the example:
        // BlueIris + Pushover as action plugins; Frigate as snapshot provider;
        // CodeProjectAi as keyed validation plugin.
        var blueIris = ActionPlugin("BlueIris");
        var pushover = ActionPlugin("Pushover");
        var frigate = SnapshotProvider("Frigate");
        var cpAi = ValidationPlugin("CodeProjectAi");

        var services = new ServiceCollection()
            .AddSingleton<IEnumerable<IActionPlugin>>(new[] { blueIris, pushover })
            .AddSingleton<IEnumerable<ISnapshotProvider>>(new[] { frigate })
            .AddKeyedSingleton<IValidationPlugin>("CodeProjectAi", cpAi)
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

    private static IValidationPlugin ValidationPlugin(string key)
    {
        var p = Substitute.For<IValidationPlugin>();
        p.Name.Returns(key);
        return p;
    }
}
