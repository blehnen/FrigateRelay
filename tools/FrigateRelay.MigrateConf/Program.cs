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

        var sections = IniReader.Read(input);
        var json = AppsettingsWriter.Build(sections);
        File.WriteAllText(output, json);
        Console.Out.WriteLine($"Wrote {output} ({new FileInfo(output).Length} bytes).");
        return 0;
    }

    // Wave 1 stub. PLAN-3.1 replaces with real reconcile logic.
    internal static int RunReconcile(string[] args)
        => Fail("reconcile verb is not yet implemented (Phase 12 Wave 3 PLAN-3.1).");

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
