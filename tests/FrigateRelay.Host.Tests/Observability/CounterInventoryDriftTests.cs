using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using FrigateRelay.Host.Dispatch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Host.Tests.Observability;

/// <summary>
/// Drift test: verifies that the counter inventory table in <c>docs/observability.md</c>
/// matches the set of <see cref="Counter{T}"/> fields declared in <see cref="DispatcherDiagnostics"/>.
/// Per CONTEXT-13 D2 — if a contributor adds a counter to <see cref="DispatcherDiagnostics"/>
/// without updating <c>docs/observability.md</c> (or vice versa), this test fails with a clear
/// diff of which side is missing the entry.
/// </summary>
[TestClass]
public sealed class CounterInventoryDriftTests
{
    [TestMethod]
    public void CounterInventory_DocAndMeter_AreInSync()
    {
        // Arrange — parse docs table and reflect over live counters in parallel
        var docPath = LocateDocsObservabilityMd();
        var docMetricNames = ParseMetricNamesFromMarkdownTable(docPath);
        var liveMetricNames = EnumerateFrigateRelayCounterNames();

        // Assert — both sets must be identical
        docMetricNames.Should().BeEquivalentTo(
            liveMetricNames,
            because: $"docs/observability.md counter inventory must stay in sync with " +
                     $"DispatcherDiagnostics. " +
                     $"Doc declares: [{string.Join(", ", docMetricNames.OrderBy(x => x))}]. " +
                     $"Live meter declares: [{string.Join(", ", liveMetricNames.OrderBy(x => x))}].");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Walks upward from <see cref="AppContext.BaseDirectory"/> until it finds a directory
    /// containing both <c>FrigateRelay.sln</c> and <c>docs/observability.md</c>.
    /// This is resilient to changes in the test binary output path (net10.0, net9.0, etc.).
    /// </summary>
    private static string LocateDocsObservabilityMd()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var slnPath = Path.Combine(dir.FullName, "FrigateRelay.sln");
            var docPath = Path.Combine(dir.FullName, "docs", "observability.md");
            if (File.Exists(slnPath) && File.Exists(docPath))
                return docPath;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate docs/observability.md by walking up from AppContext.BaseDirectory. " +
            $"Started at: {AppContext.BaseDirectory}");
    }

    /// <summary>
    /// Reads <paramref name="docPath"/>, locates the counter inventory markdown pipe table
    /// (identified by the header row containing "Metric" and "Tags"), and extracts the
    /// backtick-wrapped metric name from the first column of each data row.
    /// </summary>
    private static HashSet<string> ParseMetricNamesFromMarkdownTable(string docPath)
    {
        var lines = File.ReadAllLines(docPath);
        var metricNames = new HashSet<string>(StringComparer.Ordinal);

        // Find the header row for the counter inventory table
        var headerIndex = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line == "| Metric | Tags | Description |")
            {
                headerIndex = i;
                break;
            }
        }

        if (headerIndex < 0)
            throw new InvalidOperationException(
                $"Could not find the counter inventory table header row (containing 'Metric' and 'Tags') in {docPath}");

        // Skip the separator row (|---|---|---|) and parse data rows
        var metricPattern = new Regex(@"`(frigaterelay\.[^`]+)`", RegexOptions.Compiled);

        for (var i = headerIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Stop at blank line or non-table line
            if (!line.StartsWith('|'))
                break;

            // Skip the separator row (all dashes)
            if (line.Replace("|", "").Replace("-", "").Replace(" ", "").Length == 0)
                continue;

            // Extract the first column content (between first and second pipe)
            var parts = line.Split('|');
            if (parts.Length < 2)
                continue;

            var firstCol = parts[1];
            var match = metricPattern.Match(firstCol);
            if (match.Success)
                metricNames.Add(match.Groups[1].Value);
        }

        if (metricNames.Count == 0)
            throw new InvalidOperationException(
                $"No metric names matching `frigaterelay.*` were found in the counter inventory table of {docPath}. " +
                "Check that the table format has not been changed.");

        return metricNames;
    }

    /// <summary>
    /// Reflects over <see cref="DispatcherDiagnostics"/> and returns the names of all
    /// <see cref="Counter{T}"/> static fields where <c>T == long</c>.
    /// </summary>
    private static HashSet<string> EnumerateFrigateRelayCounterNames()
    {
        var counterType = typeof(Counter<long>);
        var fields = typeof(DispatcherDiagnostics)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f.FieldType == counterType)
            .ToList();

        if (fields.Count == 0)
            throw new InvalidOperationException(
                "No Counter<long> static fields found on DispatcherDiagnostics via reflection. " +
                "Ensure InternalsVisibleTo is configured for FrigateRelay.Host.Tests.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var counter = (Counter<long>?)field.GetValue(null);
            if (counter is not null)
                names.Add(counter.Name);
        }

        return names;
    }
}
