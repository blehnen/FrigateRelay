using System.Collections.Frozen;
using System.Text.RegularExpressions;
using FrigateRelay.Abstractions;

namespace FrigateRelay.Plugins.BlueIris;

internal sealed partial class BlueIrisUrlTemplate
{
    [GeneratedRegex(@"\{(?<name>[a-z_]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    private static readonly FrozenSet<string> AllowedTokens =
        new[] { "camera", "camera_shortname", "label", "event_id", "zone" }
            .ToFrozenSet(StringComparer.Ordinal);

    private readonly string _template;

    private BlueIrisUrlTemplate(string template) => _template = template;

    /// <summary>
    /// Parses the template and validates that every {placeholder} is in the allowlist.
    /// Throws <see cref="ArgumentException"/> with a diagnostic listing the offending token
    /// AND the full allowed set. Caller (registrar) wraps in OptionsValidationException via
    /// .Validate(...).ValidateOnStart() so the host fails fast at startup (PROJECT.md S2).
    /// </summary>
    public static BlueIrisUrlTemplate Parse(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
            throw new ArgumentException("BlueIris.TriggerUrlTemplate must not be null or whitespace.", nameof(template));

        foreach (Match m in TokenRegex().Matches(template))
        {
            var name = m.Groups["name"].Value;
            if (!AllowedTokens.Contains(name))
                throw new ArgumentException(
                    $"BlueIris.TriggerUrlTemplate contains unknown placeholder '{{{name}}}'. " +
                    $"Allowed placeholders: {{camera}}, {{camera_shortname}}, {{label}}, {{event_id}}, {{zone}}.",
                    nameof(template));
        }
        return new BlueIrisUrlTemplate(template);
    }

    public string Resolve(EventContext ctx)
    {
        return TokenRegex().Replace(_template, m => m.Groups["name"].Value switch
        {
            "camera"           => Uri.EscapeDataString(ctx.Camera),
            "camera_shortname" => Uri.EscapeDataString(
                                      string.IsNullOrWhiteSpace(ctx.CameraShortName)
                                          ? ctx.Camera
                                          : ctx.CameraShortName),
            "label"            => Uri.EscapeDataString(ctx.Label),
            "event_id"         => Uri.EscapeDataString(ctx.EventId),
            "zone"             => Uri.EscapeDataString(ctx.Zones.Count > 0 ? ctx.Zones[0] : ""),
            _                  => m.Value, // unreachable — Parse() guards
        });
    }
}
