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
            if (TryParseNdjsonLine(line, out var row))
                yield return row;
        }
    }

    /// <summary>
    /// Parses a single FrigateRelay NDJSON log line into an <see cref="ActionRow"/>. Returns
    /// <see langword="false"/> (skipping the line) for blanks, non-JSON, non-DryRun messages, or
    /// records missing a timestamp. The row's strings are copied out before the backing
    /// <see cref="JsonDocument"/> is disposed, so it is safe to return.
    /// </summary>
    private static bool TryParseNdjsonLine(string line, out ActionRow row)
    {
        row = default!;
        if (string.IsNullOrWhiteSpace(line)) return false;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch (JsonException) { return false; }

        using (doc)
        {
            // @mt is the message template; @i is a hex Murmur3 hash (NOT the action name).
            // Discriminate on whether @mt begins with the known plugin DryRun prefix.
            var mt = GetStringOrNull(doc.RootElement, "@mt") ?? "";
            var action = mt switch
            {
                _ when mt.StartsWith("BlueIris DryRun", StringComparison.Ordinal) => "BlueIris",
                _ when mt.StartsWith("Pushover DryRun", StringComparison.Ordinal) => "Pushover",
                _ => null
            };
            if (action is null) return false;

            // Skip (do not throw) on a missing/non-string/unparseable timestamp — one malformed
            // log line must not abort the whole reconciliation (CodeRabbit PR #90).
            var ts = GetStringOrNull(doc.RootElement, "@t");
            if (ts is null ||
                !DateTimeOffset.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            {
                return false;
            }

            var camera = GetStringOrNull(doc.RootElement, "Camera") ?? "";
            var label = GetStringOrNull(doc.RootElement, "Label") ?? "";

            row = new ActionRow(timestamp, camera, label, action);
            return true;
        }
    }

    // Returns a property's string value, or null when the property is absent or is not a JSON
    // string. JsonElement.GetString() throws on non-string kinds, so guarding on ValueKind keeps
    // odd-but-valid NDJSON lines skippable rather than fatal (CodeRabbit PR #90).
    private static string? GetStringOrNull(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

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
                string.Create(CultureInfo.InvariantCulture, $"Legacy CSV at {path} must have header: timestamp,camera,label,action,outcome"));
        }
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var fields = lines[i].Split(',');
            if (fields.Length < header.Length) continue;
            yield return new ActionRow(
                DateTimeOffset.Parse(fields[iTs], CultureInfo.InvariantCulture),
                fields[iCam].Trim(), fields[iLab].Trim(), fields[iAct].Trim());
        }
    }

    public static string RenderMarkdown(ReconcileReport r, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        var ic = CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# FrigateRelay v1.0.0 — Parity Report");
        sb.AppendLine();
        sb.Append(ic, $"- **Window:** {windowStart:O} → {windowEnd:O}").AppendLine();
        sb.Append(ic, $"- **Legacy actions logged:** {r.LegacyCount}").AppendLine();
        sb.Append(ic, $"- **FrigateRelay would-be-actions logged:** {r.FrigateRelayCount}").AppendLine();
        sb.Append(ic, $"- **Matched pairs:** {r.MatchedPairs}").AppendLine();
        sb.Append(ic, $"- **Missed alerts (legacy but not FrigateRelay):** {r.MissedAlerts.Count}").AppendLine();
        sb.Append(ic, $"- **Spurious alerts (FrigateRelay but not legacy):** {r.SpuriousAlerts.Count}").AppendLine();
        sb.AppendLine();
        sb.AppendLine("## Missed alerts");
        sb.AppendLine();
        if (r.MissedAlerts.Count == 0) sb.AppendLine("None.");
        else
        {
            sb.AppendLine("| Timestamp | Camera | Label | Action |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var a in r.MissedAlerts)
                sb.Append(ic, $"| {a.Timestamp:O} | {a.Camera} | {a.Label} | {a.Action} |").AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("## Spurious alerts");
        sb.AppendLine();
        if (r.SpuriousAlerts.Count == 0) sb.AppendLine("None.");
        else
        {
            sb.AppendLine("| Timestamp | Camera | Label | Action |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var a in r.SpuriousAlerts)
                sb.Append(ic, $"| {a.Timestamp:O} | {a.Camera} | {a.Label} | {a.Action} |").AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("## Sign-off");
        sb.AppendLine();
        sb.AppendLine("The operator MUST review this report before declaring v1.0.0 cutover. Per");
        sb.AppendLine("ROADMAP Phase 12 success criteria, every discrepancy is either explained as");
        sb.AppendLine("an intentional improvement OR fixed before the cutover.");
        return sb.ToString();
    }
}
