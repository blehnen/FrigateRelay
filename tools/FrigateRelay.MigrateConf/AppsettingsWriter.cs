using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrigateRelay.MigrateConf;

internal static class AppsettingsWriter
{
    private static readonly JsonSerializerOptions _compact = new() { WriteIndented = false };

    public static string Build(IReadOnlyList<IniReader.Section> sections)
    {
        var server = sections.FirstOrDefault(s => s.Name == "ServerSettings");
        var pushover = sections.FirstOrDefault(s => s.Name == "PushoverSettings");
        var subs = sections.Where(s => s.Name == "SubscriptionSettings").ToList();

        var notifySleepRaw = ValueOrEmpty(pushover, "NotifySleepTime");
        var hasCooldown = int.TryParse(notifySleepRaw, out var cooldown);
        int? cooldownValue = hasCooldown ? cooldown : null;

        // Build subscriptions as compact per-line entries to meet the ≤60% size-ratio gate.
        var subsBlock = BuildSubsBlock(subs, cooldownValue);

        var mqttServer = Esc(ValueOrEmpty(server, "server"));
        var snapshotUrl = Esc(AppendCameraToken(ValueOrEmpty(server, "blueirisimages")));

        var lines = new List<string>
        {
            "{",
            $"  \"FrigateMqtt\": {{ \"Server\": \"{mqttServer}\" }},",
            "  \"BlueIris\": {",
            "    \"TriggerUrlTemplate\": \"http://example.invalid/admin?trigger&camera={camera}\",",
            $"    \"SnapshotUrlTemplate\": \"{snapshotUrl}\"",
            "  },",
            "  \"Pushover\": { \"AppToken\": \"\", \"UserKey\": \"\" },",
            "  \"Profiles\": {",
            "    \"Standard\": {",
            "      \"Actions\": [",
            "        { \"Plugin\": \"BlueIris\" },",
            "        { \"Plugin\": \"Pushover\", \"SnapshotProvider\": \"Frigate\" }",
            "      ]",
            "    }",
            "  },",
            "  \"Subscriptions\": " + subsBlock,
            "}"
        };
        return string.Join("\n", lines);
    }

    private static string BuildSubsBlock(List<IniReader.Section> subs, int? cooldown)
    {
        var lines = new List<string>(subs.Count + 2) { "[" };
        for (var i = 0; i < subs.Count; i++)
        {
            var obj = BuildSubscriptionCompact(subs[i], cooldown);
            var comma = i < subs.Count - 1 ? "," : "";
            lines.Add("    " + obj + comma);
        }
        lines.Add("  ]");
        return string.Join("\n", lines);
    }

    private static string BuildSubscriptionCompact(IniReader.Section s, int? cooldown)
    {
        var node = new JsonObject
        {
            ["Name"] = ValueOrEmpty(s, "Name"),
            ["Camera"] = ValueOrEmpty(s, "CameraName"),
            ["Label"] = ValueOrEmpty(s, "ObjectName"),
        };
        var zone = ValueOrEmpty(s, "Zone");
        if (!string.IsNullOrEmpty(zone))
            node["Zone"] = zone;
        node["Profile"] = "Standard";
        if (cooldown.HasValue)
            node["CooldownSeconds"] = cooldown.Value;
        return node.ToJsonString(_compact);
    }

    private static string ValueOrEmpty(IniReader.Section? section, string key)
        => section?.Entries.FirstOrDefault(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value ?? "";

    private static string AppendCameraToken(string baseUrl)
        => string.IsNullOrEmpty(baseUrl) ? "" : (baseUrl.EndsWith('/') ? baseUrl + "{camera}" : baseUrl + "/{camera}");

    private static string Esc(string s)
        => JsonSerializer.Serialize(s)[1..^1];
}
