using System.Text.Json;
using FrigateRelay.MigrateConf;

namespace FrigateRelay.MigrateConf.Tests;

[TestClass]
public sealed class ReconcilerTests
{
    private static string WriteTemp(string content, string suffix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"reconcile-{Guid.NewGuid():N}.{suffix}");
        File.WriteAllText(path, content);
        return path;
    }

    // Real NDJSON shape: @mt contains the message template (e.g., "BlueIris DryRun would-execute ...").
    // @i is a hex Murmur3 hash of the template and is NEVER the action name.
    // Camera, Label, FrigateEventId are top-level string properties from LoggerMessage.Define named params.

    [TestMethod]
    public void Reconcile_PerfectMatch_ReturnsZeroMissedAndZeroSpurious()
    {
        var ndjson = WriteTemp(
            """
            {"@t":"2026-04-29T12:00:05Z","@mt":"BlueIris DryRun would-execute for camera={Camera} label={Label} event_id={FrigateEventId}","@i":"a1b2c3d4","Camera":"DriveWayHD","Label":"person","FrigateEventId":"ev-1"}
            {"@t":"2026-04-29T12:00:05Z","@mt":"Pushover DryRun would-execute for camera={Camera} label={Label} event_id={FrigateEventId}","@i":"e5f6a7b8","Camera":"DriveWayHD","Label":"person","FrigateEventId":"ev-1"}
            """, "log");
        var csv = WriteTemp(
            """
            timestamp,camera,label,action,outcome
            2026-04-29T12:00:05Z,DriveWayHD,person,BlueIris,success
            2026-04-29T12:00:05Z,DriveWayHD,person,Pushover,success
            """, "csv");

        var r = Reconciler.Reconcile(ndjson, csv, TimeSpan.FromSeconds(60));

        r.LegacyCount.Should().Be(2);
        r.FrigateRelayCount.Should().Be(2);
        r.MatchedPairs.Should().Be(2);
        r.MissedAlerts.Should().BeEmpty();
        r.SpuriousAlerts.Should().BeEmpty();
    }

    [TestMethod]
    public void Reconcile_LegacyOnlyAction_ReportsMissedAlert()
    {
        var ndjson = WriteTemp("", "log");
        var csv = WriteTemp(
            """
            timestamp,camera,label,action,outcome
            2026-04-29T12:00:05Z,DriveWayHD,person,BlueIris,success
            """, "csv");

        var r = Reconciler.Reconcile(ndjson, csv, TimeSpan.FromSeconds(60));

        r.MissedAlerts.Should().HaveCount(1);
        r.MissedAlerts[0].Camera.Should().Be("DriveWayHD");
        r.MissedAlerts[0].Action.Should().Be("BlueIris");
        r.SpuriousAlerts.Should().BeEmpty();
    }

    [TestMethod]
    public void Reconcile_FrigateRelayOnlyAction_ReportsSpuriousAlert()
    {
        var ndjson = WriteTemp(
            """
            {"@t":"2026-04-29T12:00:05Z","@mt":"BlueIris DryRun would-execute for camera={Camera} label={Label} event_id={FrigateEventId}","@i":"a1b2c3d4","Camera":"BackYard","Label":"car","FrigateEventId":"ev-9"}
            """, "log");
        var csv = WriteTemp("timestamp,camera,label,action,outcome", "csv");

        var r = Reconciler.Reconcile(ndjson, csv, TimeSpan.FromSeconds(60));

        r.SpuriousAlerts.Should().HaveCount(1);
        r.SpuriousAlerts[0].Camera.Should().Be("BackYard");
        r.MissedAlerts.Should().BeEmpty();
    }

    [TestMethod]
    public void Reconcile_DifferentMinuteBucket_TreatedAsDistinctEvents()
    {
        var ndjson = WriteTemp(
            """
            {"@t":"2026-04-29T12:00:05Z","@mt":"BlueIris DryRun would-execute for camera={Camera} label={Label} event_id={FrigateEventId}","@i":"a1b2c3d4","Camera":"DriveWayHD","Label":"person","FrigateEventId":"ev-1"}
            """, "log");
        var csv = WriteTemp(
            """
            timestamp,camera,label,action,outcome
            2026-04-29T12:01:30Z,DriveWayHD,person,BlueIris,success
            """, "csv");

        var r = Reconciler.Reconcile(ndjson, csv, TimeSpan.FromSeconds(60));

        // 12:00:05 and 12:01:30 are 85s apart → different buckets → not matched.
        r.MissedAlerts.Should().HaveCount(1);
        r.SpuriousAlerts.Should().HaveCount(1);
        r.MatchedPairs.Should().Be(0);
    }

    [TestMethod]
    public void Reconcile_LineWithUnrecognizedTemplate_IsSkipped()
    {
        // Lines with @mt that doesn't match either DryRun template should be ignored.
        var ndjson = WriteTemp(
            """
            {"@t":"2026-04-29T12:00:05Z","@mt":"some other log message","@i":"deadbeef","Camera":"DriveWayHD","Label":"person","FrigateEventId":"ev-x"}
            """, "log");
        var csv = WriteTemp("timestamp,camera,label,action,outcome", "csv");

        var r = Reconciler.Reconcile(ndjson, csv, TimeSpan.FromSeconds(60));

        r.FrigateRelayCount.Should().Be(0);
        r.SpuriousAlerts.Should().BeEmpty();
    }

    [TestMethod]
    public void RenderMarkdown_ReportWithMissedAndSpurious_ContainsBothTables()
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        var report = new Reconciler.ReconcileReport(
            LegacyCount: 1,
            FrigateRelayCount: 1,
            MissedAlerts: new[] { new Reconciler.ActionRow(DateTimeOffset.Parse("2026-04-29T12:00:00Z", ic), "Cam1", "person", "BlueIris") },
            SpuriousAlerts: new[] { new Reconciler.ActionRow(DateTimeOffset.Parse("2026-04-29T13:00:00Z", ic), "Cam2", "car", "Pushover") },
            MatchedPairs: 0);

        var md = Reconciler.RenderMarkdown(report,
            DateTimeOffset.Parse("2026-04-29T12:00:00Z", ic),
            DateTimeOffset.Parse("2026-04-29T13:00:00Z", ic));

        md.Should().Contain("# FrigateRelay v1.0.0 — Parity Report");
        md.Should().Contain("## Missed alerts");
        md.Should().Contain("Cam1");
        md.Should().Contain("## Spurious alerts");
        md.Should().Contain("Cam2");
    }
}
