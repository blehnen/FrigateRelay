namespace FrigateRelay.MigrateConf;

/// <summary>
/// Reads a legacy SharpConfig-style INI file into an ordered list of (sectionName, keys[]) tuples.
/// Repeated section headers (e.g. multiple [SubscriptionSettings] blocks) are preserved as
/// distinct list entries — last-writer-wins semantics of standard INI dictionaries are explicitly avoided.
/// </summary>
internal static class IniReader
{
    public sealed record Section(string Name, IReadOnlyList<KeyValuePair<string, string>> Entries);

    public static List<Section> Read(string path)
    {
        var sections = new List<Section>();
        List<KeyValuePair<string, string>>? current = null;
        string? currentName = null;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (currentName is not null)
                {
                    sections.Add(new Section(currentName, current!));
                }
                currentName = line[1..^1].Trim();
                current = new List<KeyValuePair<string, string>>();
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq < 0 || current is null)
            {
                continue;
            }
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            current.Add(new KeyValuePair<string, string>(key, value));
        }
        if (currentName is not null)
        {
            sections.Add(new Section(currentName, current!));
        }
        return sections;
    }
}
