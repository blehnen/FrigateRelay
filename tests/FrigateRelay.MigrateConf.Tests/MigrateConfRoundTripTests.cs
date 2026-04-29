using System.Text.Json;
using FrigateRelay.MigrateConf;
using Microsoft.Extensions.Configuration;

namespace FrigateRelay.MigrateConf.Tests;

[TestClass]
public sealed class MigrateConfRoundTripTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy.conf");

    [TestMethod]
    public void IniReader_LegacyConf_Yields_OneServerOnePushoverNineSubscriptions()
    {
        File.Exists(FixturePath).Should().BeTrue($"fixture must be linked at {FixturePath}");

        var sections = IniReader.Read(FixturePath);

        sections.Count(s => s.Name == "ServerSettings").Should().Be(1);
        sections.Count(s => s.Name == "PushoverSettings").Should().Be(1);
        sections.Count(s => s.Name == "SubscriptionSettings").Should().Be(9);
    }

    [TestMethod]
    public void RunMigrate_LegacyConf_ProducesValidJsonWithNineSubscriptions()
    {
        var output = Path.Combine(Path.GetTempPath(), $"frigaterelay-migrate-{Guid.NewGuid():N}.json");
        try
        {
            var rc = Program.RunMigrate(["--input", FixturePath, "--output", output]);
            rc.Should().Be(0);

            File.Exists(output).Should().BeTrue();

            using var doc = JsonDocument.Parse(File.ReadAllText(output));
            doc.RootElement.GetProperty("Subscriptions").GetArrayLength().Should().Be(9);
            doc.RootElement.GetProperty("Profiles").GetProperty("Standard").GetProperty("Actions").GetArrayLength().Should().Be(2);
            doc.RootElement.GetProperty("Pushover").GetProperty("AppToken").GetString().Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [TestMethod]
    public void RunMigrate_LegacyConf_OutputSizeRatioBelowSeventy()
    {
        var output = Path.Combine(Path.GetTempPath(), $"frigaterelay-migrate-{Guid.NewGuid():N}.json");
        try
        {
            Program.RunMigrate(["--input", FixturePath, "--output", output]).Should().Be(0);

            var iniLength = new FileInfo(FixturePath).Length;
            var jsonLength = new FileInfo(output).Length;
            var ratio = (double)jsonLength / iniLength;

            // MigrateConf emits a complete appsettings (FrigateMqtt + BlueIris + Pushover +
            // Profiles + Subscriptions). The ≤60% gate applies only to the Profiles+Subscriptions
            // example JSON (Phase 8). A full migration with connection settings achieves ≤70%.
            ratio.Should().BeLessThanOrEqualTo(0.70d, "MigrateConf compact output must be well below the raw INI size");
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [TestMethod]
    public void RunMigrate_LegacyConf_OutputBindsAsConfiguration()
    {
        var output = Path.Combine(Path.GetTempPath(), $"frigaterelay-migrate-{Guid.NewGuid():N}.json");
        try
        {
            Program.RunMigrate(["--input", FixturePath, "--output", output]).Should().Be(0);

            var config = new ConfigurationBuilder().AddJsonFile(output, optional: false).Build();
            config["FrigateMqtt:Server"].Should().NotBeNullOrEmpty();
            config.GetSection("Subscriptions").GetChildren().Should().HaveCount(9);
            config.GetSection("Profiles:Standard:Actions").GetChildren().Should().HaveCount(2);
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }
}
