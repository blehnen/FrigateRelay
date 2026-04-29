namespace FrigateRelay.MigrateConf;

internal static class Program
{
    internal static int Main(string[] args)
    {
        // Verb router. Wave 1 implements 'migrate' (default). Wave 3 PLAN-3.1 appends 'reconcile'.
        var verb = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? args[0] : "migrate";
        var verbArgs = verb == args.FirstOrDefault() ? args.Skip(1).ToArray() : args;

        return verb switch
        {
            "migrate" => RunMigrate(verbArgs),
            "reconcile" => RunReconcile(verbArgs),
            _ => Fail($"Unknown verb '{verb}'. Supported: migrate, reconcile.")
        };
    }

    internal static int RunMigrate(string[] args)
    {
        if (!TryGetArg(args, "--input", out var input))
        {
            return Fail("Usage: migrate-conf [migrate] --input <path-to-legacy.conf> [--output <path-to-appsettings.json>]");
        }

        if (!TryGetArg(args, "--output", out var output))
        {
            output = "appsettings.Local.json";
        }

        // Canonicalize CLI paths (AUDIT-12 A1 / ID-28). Tool is offline + runs as the operator,
        // so risk is operator-self-inflicted, but matches the ValidateSerilogPath precedent
        // (Phase 10 ID-21 close).
        input = Path.GetFullPath(input);
        output = Path.GetFullPath(output);

        var sections = IniReader.Read(input);
        var json = AppsettingsWriter.Build(sections);
        File.WriteAllText(output, json);
        Console.Out.WriteLine($"Wrote {output} ({new FileInfo(output).Length} bytes).");
        return 0;
    }

    internal static int RunReconcile(string[] args)
    {
        if (!TryGetArg(args, "--frigaterelay", out var ndjson) ||
            !TryGetArg(args, "--legacy", out var csv) ||
            !TryGetArg(args, "--output", out var output))
        {
            return Fail("Usage: migrate-conf reconcile --frigaterelay <ndjson-path> --legacy <csv-path> --output <md-path> [--bucket-seconds 60]");
        }

        // Canonicalize CLI paths (AUDIT-12 A1 / ID-28).
        ndjson = Path.GetFullPath(ndjson);
        csv = Path.GetFullPath(csv);
        output = Path.GetFullPath(output);

        var bucketSeconds = TryGetArg(args, "--bucket-seconds", out var bs) && int.TryParse(bs, out var b) ? b : 60;
        var bucket = TimeSpan.FromSeconds(bucketSeconds);

        var report = Reconciler.Reconcile(ndjson, csv, bucket);

        var allRows = Reconciler.ReadFrigateRelayNdjson(ndjson).Concat(Reconciler.ReadLegacyCsv(csv)).ToList();
        var windowStart = allRows.Count > 0 ? allRows.Min(r => r.Timestamp) : DateTimeOffset.MinValue;
        var windowEnd = allRows.Count > 0 ? allRows.Max(r => r.Timestamp) : DateTimeOffset.MaxValue;

        var md = Reconciler.RenderMarkdown(report, windowStart, windowEnd);
        File.WriteAllText(output, md);

        Console.Out.WriteLine(
            $"Reconcile complete. Legacy={report.LegacyCount} FrigateRelay={report.FrigateRelayCount} " +
            $"Matched={report.MatchedPairs} Missed={report.MissedAlerts.Count} Spurious={report.SpuriousAlerts.Count}");
        Console.Out.WriteLine($"Wrote {output}.");

        return report.MissedAlerts.Count == 0 && report.SpuriousAlerts.Count == 0 ? 0 : 2;
    }

    private static bool TryGetArg(string[] args, string name, out string value)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                value = args[i + 1];
                return true;
            }
        }
        value = "";
        return false;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
