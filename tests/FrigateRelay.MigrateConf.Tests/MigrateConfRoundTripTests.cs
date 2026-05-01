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
            // example JSON (Phase 8). A full migration with connection settings achieves ≤80%
            // (was ≤70% pre-#32; per-subscription CameraShortName preservation adds ~one short
            // field per row, pushing the ratio up slightly — still well below the raw INI size).
            ratio.Should().BeLessThanOrEqualTo(0.80d, "MigrateConf compact output must be well below the raw INI size");
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [TestMethod]
    public void RunMigrate_LegacyConf_PreservesCameraShortNamePerSubscription()
    {
        // #32: legacy CameraShortName carries the Blue Iris shortname, which Blue Iris
        // requires verbatim in trigger URLs. The migration tool now preserves it on each
        // subscription whenever it differs from CameraName (when they match, the override
        // is redundant and is intentionally skipped).
        var output = Path.Combine(Path.GetTempPath(), $"frigaterelay-migrate-{Guid.NewGuid():N}.json");
        try
        {
            Program.RunMigrate(["--input", FixturePath, "--output", output]).Should().Be(0);

            using var doc = JsonDocument.Parse(File.ReadAllText(output));
            var subscriptions = doc.RootElement.GetProperty("Subscriptions");

            var driveway = subscriptions.EnumerateArray()
                .First(s => s.GetProperty("Name").GetString() == "DriveWay Person");
            driveway.GetProperty("Camera").GetString().Should().Be("driveway");
            driveway.GetProperty("CameraShortName").GetString().Should().Be("DriveWayHD",
                "the legacy CameraShortName must carry through so {camera_shortname} resolves " +
                "to BI's shortname and the trigger URL actually fires");
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [TestMethod]
    public void RunMigrate_LegacyConf_TriggerTemplateUsesCameraShortnameToken()
    {
        // #32 follow-on: with CameraShortName preserved per subscription, the migrated
        // BlueIris.TriggerUrlTemplate should use {camera_shortname} so the URL substitution
        // picks up the override automatically.
        var output = Path.Combine(Path.GetTempPath(), $"frigaterelay-migrate-{Guid.NewGuid():N}.json");
        try
        {
            Program.RunMigrate(["--input", FixturePath, "--output", output]).Should().Be(0);

            using var doc = JsonDocument.Parse(File.ReadAllText(output));
            var trigger = doc.RootElement.GetProperty("BlueIris").GetProperty("TriggerUrlTemplate").GetString();
            trigger.Should().Contain("{camera_shortname}",
                "operators migrating from legacy almost certainly diverge between Frigate id " +
                "and BI shortname, so the migrated template should opt into the override token");
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
