---
phase: 12-parity-cutover
plan: 3.1
wave: 3
dependencies: [1.3, 1.5, 2.1]

# WAVE-1 REVIEWER HEADS-UP (REVIEW-1.5 finding F-1, medium severity, 2026-04-28):
# This plan's reconciler spec uses `@i` as a discriminator expecting values
# like "BlueIrisDryRun" / "PushoverDryRun". That assumption is WRONG.
# Serilog's CompactJsonFormatter emits `@i` as a hex Murmur3 hash of the
# message template — NEVER an action-name string. The synthetic example
# fixtures embedded in this plan (lines 264-265, 107) showing
# `"@i":"BlueIrisDryRun"` will never appear in real output.
#
# CORRECT field for action-name discrimination: read the structured property
# emitted by `LoggerMessage.Define`'s named EventId. PLAN-1.1 emits
# `EventId.Name = "BlueIrisDryRun"` (top-level "EventId" property in the
# CompactJson output is an object containing `{ "Id": 203, "Name": "BlueIrisDryRun" }`
# OR the renderer may flatten this — Wave-3 builder MUST verify against an
# actual NDJSON sample emitted by the host running with `Logging:File:CompactJson=true`.
#
# Builder action: replace all references to `@i` (frontmatter, examples, code spec)
# with reads from the actual top-level "EventId" property's "Name" sub-property,
# OR a different structured property the LoggerMessage.Define emission produces.
# Generate a real NDJSON sample first; let the format dictate the parser.


must_haves:
  - tools/FrigateRelay.MigrateConf reconcile subcommand reads NDJSON + legacy CSV, writes parity-report.md
  - Reconciler pairs (camera, label, timestamp-bucket) tuples and lists missed/spurious actions
  - tests/FrigateRelay.MigrateConf.Tests covers reconcile against synthetic NDJSON + CSV pair
  - docs/parity-report.md template exists (filled by operator after 48h window OR by Wave 3 builder if operator artifacts are present)
files_touched:
  - tools/FrigateRelay.MigrateConf/Program.cs
  - tools/FrigateRelay.MigrateConf/Reconciler.cs
  - tests/FrigateRelay.MigrateConf.Tests/ReconcilerTests.cs
  - docs/parity-report.md
tdd: true
risk: medium
---

# Plan 3.1: `migrate-conf reconcile` subcommand + `docs/parity-report.md`

## Context

CONTEXT-12 Wave 3 produces the parity reconciliation. Architect-locked decision: reconciliation is a `reconcile` subcommand of the existing `tools/FrigateRelay.MigrateConf/` (NOT a separate csproj, NOT bash/Python). PLAN-1.3 Task 1 already scaffolded the verb router with a stub for `reconcile`; this plan replaces the stub with the real implementation and adds the test coverage + the parity-report doc template.

**The reconciler:**
- Reads FrigateRelay NDJSON (Serilog Compact format from PLAN-1.5) — extracts `(timestamp, Camera, Label, EventId.Name → action)` from lines whose `"@i"` is `"BlueIrisDryRun"` or `"PushoverDryRun"`.
- Reads legacy CSV (`timestamp,camera,label,action,outcome`).
- Buckets timestamps by 60-second window (CONTEXT-12 success criterion: "for every camera+label combination firing within one minute, both services issued exactly one notification") — this is the cooldown match window.
- Produces a Markdown summary: total counts, missed alerts (in legacy but not in FrigateRelay NDJSON), spurious alerts (in FrigateRelay but not in legacy).

**Architect-discretion locked:**

- **Subcommand of MigrateConf** — minimizes new csproj surface (CONTEXT-12 architect-discretion bias).
- **Output is Markdown, not JSON.** The `docs/parity-report.md` template is the operator-facing artifact. Reconciler writes the populated report directly.
- **60-second bucket** is the cooldown match window. Per CONTEXT-12 success criterion: any camera+label firing within one minute = same logical event.
- **Reconciler does NOT auto-commit the report.** It writes to `docs/parity-report.md` (or the operator's `--output` override); the operator commits manually as part of v1.0.0 cutover.

## Dependencies

- **PLAN-1.3** — Provides `tools/FrigateRelay.MigrateConf/` with the verb router skeleton. This plan extends `Program.cs` (adding a real `RunReconcile` body) and adds `Reconciler.cs`.
- **PLAN-1.5** — Locks the NDJSON field shape (`Camera`, `Label`, `EventId`, `@i` for EventId.Name). Reconciler relies on these field names.
- **PLAN-2.1** — Operator checklist establishes that the parity window has run and artifacts exist. Wave 3 builders MAY run the reconciler against the real artifacts; tests use synthetic fixtures.
- **Wave 3 file-disjoint with PLAN-3.2.** PLAN-3.1 owns `tools/FrigateRelay.MigrateConf/Program.cs` (extend), `tools/FrigateRelay.MigrateConf/Reconciler.cs` (new), `tests/FrigateRelay.MigrateConf.Tests/ReconcilerTests.cs` (new), `docs/parity-report.md` (new). PLAN-3.2 owns `README.md`, `RELEASING.md`, `CHANGELOG.md`. No overlap.

## Tasks

### Task 1: Implement `Reconciler.cs` + extend `RunReconcile` in `Program.cs`

**Files:**
- `tools/FrigateRelay.MigrateConf/Reconciler.cs` (create)
- `tools/FrigateRelay.MigrateConf/Program.cs` (modify — replace `RunReconcile` stub with real impl)

**Action:** create + modify

**Description:**

**`Reconciler.cs`** — pure logic, parses both inputs, returns a `ReconcileReport` record:

```csharp
using System.Globalization;
using System.Text.Json;

namespace FrigateRelay.MigrateConf;

internal static class Reconciler
{
    public sealed record ActionRow(DateTimeOffset Timestamp, string Camera, string Label, string Action);

    public sealed record ReconcileReport(
        int LegacyCount,
        int FrigateRelayCount,
        IReadOnlyList<ActionRow> MissedAlerts,
        IReadOnlyList<ActionRow> SpuriousAlerts,
        int MatchedPairs);

    public static ReconcileReport Reconcile(string ndjsonPath, string legacyCsvPath, TimeSpan bucket)
    {
        var fr = ReadFrigateRelayNdjson(ndjsonPath).ToList();
        var legacy = ReadLegacyCsv(legacyCsvPath).ToList();

        // Bucket-key: (camera, label, action, floor(timestamp / bucket))
        static (string, string, string, long) KeyOf(ActionRow r, TimeSpan b)
            => (r.Camera, r.Label, r.Action, r.Timestamp.UtcTicks / b.Ticks);

        var legacyKeys = legacy.ToLookup(r => KeyOf(r, bucket));
        var frKeys = fr.ToLookup(r => KeyOf(r, bucket));

        var missed = legacy.Where(r => !frKeys.Contains(KeyOf(r, bucket))).ToList();
        var spurious = fr.Where(r => !legacyKeys.Contains(KeyOf(r, bucket))).ToList();
        var matched = legacy.Count - missed.Count;

        return new ReconcileReport(legacy.Count, fr.Count, missed, spurious, matched);
    }

    public static IEnumerable<ActionRow> ReadFrigateRelayNdjson(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }

            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("@i", out var iProp)) continue;
                var eventName = iProp.GetString();
                var action = eventName switch
                {
                    "BlueIrisDryRun" => "BlueIris",
                    "PushoverDryRun" => "Pushover",
                    _ => null
                };
                if (action is null) continue;

                var ts = doc.RootElement.GetProperty("@t").GetString()!;
                var camera = doc.RootElement.GetProperty("Camera").GetString() ?? "";
                var label = doc.RootElement.GetProperty("Label").GetString() ?? "";

                yield return new ActionRow(
                    DateTimeOffset.Parse(ts, CultureInfo.InvariantCulture),
                    camera, label, action);
            }
        }
    }

    public static IEnumerable<ActionRow> ReadLegacyCsv(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0) yield break;
        var header = lines[0].Split(',');
        var iTs = Array.IndexOf(header, "timestamp");
        var iCam = Array.IndexOf(header, "camera");
        var iLab = Array.IndexOf(header, "label");
        var iAct = Array.IndexOf(header, "action");
        if (iTs < 0 || iCam < 0 || iLab < 0 || iAct < 0)
        {
            throw new InvalidDataException(
                $"Legacy CSV at {path} must have header: timestamp,camera,label,action,outcome");
        }
        for (var i = 1; i < lines.Length; i++)
        {
            var fields = lines[i].Split(',');
            if (fields.Length < header.Length) continue;
            yield return new ActionRow(
                DateTimeOffset.Parse(fields[iTs], CultureInfo.InvariantCulture),
                fields[iCam].Trim(), fields[iLab].Trim(), fields[iAct].Trim());
        }
    }

    public static string RenderMarkdown(ReconcileReport r, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# FrigateRelay v1.0.0 — Parity Report");
        sb.AppendLine();
        sb.AppendLine($"- **Window:** {windowStart:O} → {windowEnd:O}");
        sb.AppendLine($"- **Legacy actions logged:** {r.LegacyCount}");
        sb.AppendLine($"- **FrigateRelay would-be-actions logged:** {r.FrigateRelayCount}");
        sb.AppendLine($"- **Matched pairs:** {r.MatchedPairs}");
        sb.AppendLine($"- **Missed alerts (legacy but not FrigateRelay):** {r.MissedAlerts.Count}");
        sb.AppendLine($"- **Spurious alerts (FrigateRelay but not legacy):** {r.SpuriousAlerts.Count}");
        sb.AppendLine();
        sb.AppendLine("## Missed alerts");
        sb.AppendLine();
        if (r.MissedAlerts.Count == 0) sb.AppendLine("None.");
        else
        {
            sb.AppendLine("| Timestamp | Camera | Label | Action |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var a in r.MissedAlerts) sb.AppendLine($"| {a.Timestamp:O} | {a.Camera} | {a.Label} | {a.Action} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Spurious alerts");
        sb.AppendLine();
        if (r.SpuriousAlerts.Count == 0) sb.AppendLine("None.");
        else
        {
            sb.AppendLine("| Timestamp | Camera | Label | Action |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var a in r.SpuriousAlerts) sb.AppendLine($"| {a.Timestamp:O} | {a.Camera} | {a.Label} | {a.Action} |");
        }
        return sb.ToString();
    }
}
```

**`Program.cs` modification** — replace the existing `RunReconcile` stub with:

```csharp
internal static int RunReconcile(string[] args)
{
    if (!TryGetArg(args, "--frigaterelay", out var ndjson) ||
        !TryGetArg(args, "--legacy", out var csv) ||
        !TryGetArg(args, "--output", out var output))
    {
        return Fail("Usage: migrate-conf reconcile --frigaterelay <ndjson-path> --legacy <csv-path> --output <md-path> [--bucket-seconds 60]");
    }

    var bucketSeconds = TryGetArg(args, "--bucket-seconds", out var bs) && int.TryParse(bs, out var b) ? b : 60;
    var bucket = TimeSpan.FromSeconds(bucketSeconds);

    var report = Reconciler.Reconcile(ndjson, csv, bucket);

    // Window bounds: min/max across both inputs.
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
```

Exit codes: `0` = parity (zero missed, zero spurious), `2` = parity gap (operator must investigate before declaring v1.0.0 cutover per ROADMAP success criterion). `1` is reserved for usage errors.

**Acceptance Criteria:**
- `tools/FrigateRelay.MigrateConf/Reconciler.cs` exists.
- `grep -q 'RunReconcile' tools/FrigateRelay.MigrateConf/Program.cs` AND the body is no longer the Wave 1 stub (`grep -q 'reconcile verb is not yet implemented' tools/FrigateRelay.MigrateConf/Program.cs` returns NO match).
- `dotnet build tools/FrigateRelay.MigrateConf/FrigateRelay.MigrateConf.csproj -c Release` clean.
- `dotnet build FrigateRelay.sln -c Release` clean.
- Smoke (manual): `dotnet run --project tools/FrigateRelay.MigrateConf -c Release -- reconcile` (no args) prints the usage string and exits 1.

### Task 2: Reconciler unit tests

**Files:**
- `tests/FrigateRelay.MigrateConf.Tests/ReconcilerTests.cs` (create)

**Action:** create (TDD: write before Task 1's `Reconciler.cs` impl)

**Description:**

Synthetic NDJSON + CSV fixture content built inline (no separate file). Tests cover: a perfect match, a missed alert (legacy-only), a spurious alert (FrigateRelay-only), and the 60-second bucket boundary.

```csharp
using FluentAssertions;
using FrigateRelay.MigrateConf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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

    [TestMethod]
    public void Reconcile_PerfectMatch_ReturnsZeroMissedAndZeroSpurious()
    {
        var ndjson = WriteTemp(
            """
            {"@t":"2026-04-29T12:00:05Z","@m":"DryRun","@i":"BlueIrisDryRun","Camera":"DriveWayHD","Label":"person","EventId":"ev-1"}
            {"@t":"2026-04-29T12:00:05Z","@m":"DryRun","@i":"PushoverDryRun","Camera":"DriveWayHD","Label":"person","EventId":"ev-1"}
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
            {"@t":"2026-04-29T12:00:05Z","@m":"DryRun","@i":"BlueIrisDryRun","Camera":"BackYard","Label":"car","EventId":"ev-9"}
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
            {"@t":"2026-04-29T12:00:05Z","@m":"DryRun","@i":"BlueIrisDryRun","Camera":"DriveWayHD","Label":"person","EventId":"ev-1"}
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
    public void RenderMarkdown_ReportWithMissedAndSpurious_ContainsBothTables()
    {
        var report = new Reconciler.ReconcileReport(
            LegacyCount: 1,
            FrigateRelayCount: 1,
            MissedAlerts: new[] { new Reconciler.ActionRow(DateTimeOffset.Parse("2026-04-29T12:00:00Z"), "Cam1", "person", "BlueIris") },
            SpuriousAlerts: new[] { new Reconciler.ActionRow(DateTimeOffset.Parse("2026-04-29T13:00:00Z"), "Cam2", "car", "Pushover") },
            MatchedPairs: 0);

        var md = Reconciler.RenderMarkdown(report,
            DateTimeOffset.Parse("2026-04-29T12:00:00Z"),
            DateTimeOffset.Parse("2026-04-29T13:00:00Z"));

        md.Should().Contain("# FrigateRelay v1.0.0 — Parity Report");
        md.Should().Contain("## Missed alerts");
        md.Should().Contain("Cam1");
        md.Should().Contain("## Spurious alerts");
        md.Should().Contain("Cam2");
    }
}
```

**Acceptance Criteria:**
- `tests/FrigateRelay.MigrateConf.Tests/ReconcilerTests.cs` exists.
- `grep -c '\[TestMethod\]' tests/FrigateRelay.MigrateConf.Tests/ReconcilerTests.cs` returns at least 5.
- `dotnet build tests/FrigateRelay.MigrateConf.Tests/FrigateRelay.MigrateConf.Tests.csproj -c Release` clean.
- `dotnet run --project tests/FrigateRelay.MigrateConf.Tests -c Release --no-build` exits 0 with all tests Passed.

### Task 3: Create `docs/parity-report.md` (template + populated-from-real-data if available)

**Files:**
- `docs/parity-report.md` (create)

**Action:** create

**Description:**

If the operator has run the parity window and produced `logs/frigaterelay-*.log` + `parity-window/legacy-actions.csv`, the builder runs:

```bash
dotnet run --project tools/FrigateRelay.MigrateConf -c Release -- \
  reconcile \
  --frigaterelay logs/frigaterelay-$(date +%Y%m%d).log \
  --legacy parity-window/legacy-actions.csv \
  --output docs/parity-report.md
```

If the operator has NOT yet run the parity window (Wave 2 hasn't completed), the builder writes a TEMPLATE version of the file with placeholder text:

```markdown
# FrigateRelay v1.0.0 — Parity Report

> **Template** — to be replaced with the real reconciliation output once the
> 48-hour parity window per `docs/parity-window-checklist.md` has completed
> and the operator runs:
>
> ```bash
> dotnet run --project tools/FrigateRelay.MigrateConf -c Release -- \
>   reconcile \
>   --frigaterelay logs/frigaterelay-YYYYMMDD.log \
>   --legacy parity-window/legacy-actions.csv \
>   --output docs/parity-report.md
> ```

- **Window:** TBD → TBD
- **Legacy actions logged:** TBD
- **FrigateRelay would-be-actions logged:** TBD
- **Matched pairs:** TBD
- **Missed alerts (legacy but not FrigateRelay):** TBD
- **Spurious alerts (FrigateRelay but not legacy):** TBD

## Missed alerts

TBD — table of `(timestamp, camera, label, action)` rows.

## Spurious alerts

TBD — table of `(timestamp, camera, label, action)` rows.

## Sign-off

The operator MUST review this report before declaring v1.0.0 cutover. Per
ROADMAP Phase 12 success criteria, every discrepancy is either explained as
an intentional improvement OR fixed before the cutover.
```

**Builder decision rule:** check `test -f logs/frigaterelay-*.log && test -f parity-window/legacy-actions.csv`. If both present, run the reconciler and commit the populated report. If absent, commit the template version above. Either way the file exists and the README link from PLAN-3.2 will resolve.

**Acceptance Criteria:**
- `test -f docs/parity-report.md`
- `grep -q 'FrigateRelay v1.0.0 — Parity Report' docs/parity-report.md`
- Either:
  - **Template branch:** `grep -q '> \*\*Template\*\*' docs/parity-report.md` AND `grep -q 'TBD' docs/parity-report.md`
  - **Populated branch:** `grep -q '\*\*Window:\*\*' docs/parity-report.md` AND no `TBD` markers AND counts are concrete numbers.
- `grep -nE '192\.168\.|10\.0\.0\.' docs/parity-report.md` returns zero matches (any real IPs in the populated report MUST be RFC 5737 only or sanitized).
- `.github/scripts/secret-scan.sh` exits 0.

## Verification

```bash
# 1. Build clean
dotnet build FrigateRelay.sln -c Release

# 2. New + existing tests green (both MigrateConf.Tests projects' methods)
dotnet run --project tests/FrigateRelay.MigrateConf.Tests -c Release --no-build

# 3. Reconcile usage prints when args missing
dotnet run --project tools/FrigateRelay.MigrateConf -c Release --no-build -- reconcile
# expect exit 1 and a Usage: ... line

# 4. End-to-end smoke against synthetic fixtures (mirror Task 2 first test)
ND=/tmp/recon-smoke.log
CSV=/tmp/recon-smoke.csv
OUT=/tmp/recon-smoke.md
echo '{"@t":"2026-04-29T12:00:05Z","@m":"DryRun","@i":"BlueIrisDryRun","Camera":"DriveWayHD","Label":"person","EventId":"ev-1"}' > "$ND"
printf 'timestamp,camera,label,action,outcome\n2026-04-29T12:00:05Z,DriveWayHD,person,BlueIris,success\n' > "$CSV"
dotnet run --project tools/FrigateRelay.MigrateConf -c Release --no-build -- \
  reconcile --frigaterelay "$ND" --legacy "$CSV" --output "$OUT"
test -f "$OUT"
grep -q 'Matched pairs:.*1' "$OUT"

# 5. Parity-report.md exists
test -f docs/parity-report.md
grep -q 'FrigateRelay v1.0.0 — Parity Report' docs/parity-report.md

# 6. Secret-scan stays clean
.github/scripts/secret-scan.sh
```
