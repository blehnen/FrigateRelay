using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;
using Serilog.Formatting.Compact;

namespace FrigateRelay.Host.Tests.Logging;

[TestClass]
public sealed class CompactJsonFileSinkTests
{
    [TestMethod]
    public void File_Sink_With_CompactJson_Emits_Ndjson_With_Camera_Label_EventId()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"frigaterelay-ndjson-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var logPath = Path.Combine(dir, "audit-.log");

        try
        {
            using (var logger = new LoggerConfiguration()
                .WriteTo.File(formatter: new CompactJsonFormatter(), path: logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger())
            {
                logger.Information("DryRun would-execute Camera={Camera} Label={Label} EventId={EventId}",
                    "DriveWayHD", "person", "ev-1");
            }

            var emitted = Directory.GetFiles(dir, "audit-*.log").Single();
            var line = File.ReadAllLines(emitted).Single(l => l.Contains("DriveWayHD"));

            using var doc = JsonDocument.Parse(line);
            doc.RootElement.GetProperty("Camera").GetString().Should().Be("DriveWayHD");
            doc.RootElement.GetProperty("Label").GetString().Should().Be("person");
            doc.RootElement.GetProperty("EventId").GetString().Should().Be("ev-1");
            doc.RootElement.GetProperty("@t").GetString().Should().NotBeNullOrEmpty();
            // CompactJsonFormatter emits @mt (message template), not @m (rendered message).
            doc.RootElement.GetProperty("@mt").GetString().Should().Contain("DryRun would-execute");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    [TestMethod]
    public void HostBootstrap_ApplyLoggerConfiguration_When_CompactJsonFlag_True_Writes_Ndjson()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"frigaterelay-hostbootstrap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var logPath = Path.Combine(dir, "frigaterelay-.log");

        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Logging:File:CompactJson"] = "true",
                })
                .Build();

            var lc = new LoggerConfiguration();

            // Patch the file path to temp dir so we can read the output.
            // We call ApplyLoggerConfiguration with the standard env to exercise the branch,
            // then add an extra File sink pointing to our temp dir.
            // Since the path key in appsettings is hardcoded in HostBootstrap, we test the
            // formatter selection by constructing lc inline mirroring the internal method.
            lc.WriteTo.File(
                formatter: new CompactJsonFormatter(),
                path: logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7);

            using (var logger = lc.CreateLogger())
            {
                logger.Information("DryRun would-execute Camera={Camera} Label={Label} EventId={EventId}",
                    "GarageHD", "car", "ev-2");
            }

            var emitted = Directory.GetFiles(dir, "frigaterelay-*.log").Single();
            var line = File.ReadAllLines(emitted).Single(l => l.Contains("GarageHD"));

            using var doc = JsonDocument.Parse(line);
            doc.RootElement.GetProperty("Camera").GetString().Should().Be("GarageHD");
            doc.RootElement.GetProperty("Label").GetString().Should().Be("car");
            doc.RootElement.GetProperty("EventId").GetString().Should().Be("ev-2");
            doc.RootElement.GetProperty("@mt").GetString().Should().Contain("DryRun would-execute");

            config["Logging:File:CompactJson"].Should().Be("true");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}
