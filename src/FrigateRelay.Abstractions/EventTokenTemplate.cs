using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace FrigateRelay.Abstractions;

/// <summary>
/// A parsed, validated token-substitution template that resolves <see cref="EventContext"/>
/// fields into a string. Supports URL-encoding (default) or raw substitution.
/// </summary>
/// <remarks>
/// Allowed tokens: {camera}, {camera_shortname}, {label}, {event_id}, {zone}. The {score}
/// placeholder is explicitly excluded (ID-7 hard rail) — EventContext carries no Score property.
/// Use <see cref="Parse"/> to obtain an instance; the constructor is private.
/// </remarks>
public sealed partial class EventTokenTemplate
{
    [GeneratedRegex(@"\{(?<name>[a-z_]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    /// <summary>The set of token names accepted by <see cref="Parse"/>.</summary>
    public static readonly FrozenSet<string> AllowedTokens =
        new[] { "camera", "camera_shortname", "label", "event_id", "zone" }
            .ToFrozenSet(StringComparer.Ordinal);

    private readonly string _template;

    private EventTokenTemplate(string template) => _template = template;

    /// <summary>
    /// Parses <paramref name="template"/> and validates every {placeholder} against
    /// <see cref="AllowedTokens"/>. Throws <see cref="ArgumentException"/> on null/whitespace
    /// input or on any unknown token (including {score}).
    /// </summary>
    /// <param name="template">The raw template string, e.g. <c>https://host/{camera}?l={label}</c>.</param>
    /// <param name="callerName">
    /// A short descriptor of the caller (e.g. "BlueIris.TriggerUrl"). Included in exception
    /// messages so operators can locate the offending config key without a stack trace.
    /// </param>
    public static EventTokenTemplate Parse(string template, string callerName)
    {
        if (string.IsNullOrWhiteSpace(template))
            throw new ArgumentException(
                $"{callerName}: template must not be null or whitespace.",
                nameof(template));

        foreach (Match m in TokenRegex().Matches(template))
        {
            var name = m.Groups["name"].Value;
            if (!AllowedTokens.Contains(name))
                throw new ArgumentException(
                    $"{callerName}: template contains unknown placeholder '{{{name}}}'. " +
                    $"Allowed placeholders: {{camera}}, {{camera_shortname}}, {{label}}, {{event_id}}, {{zone}}.",
                    nameof(template));
        }

        return new EventTokenTemplate(template);
    }

    /// <summary>
    /// Substitutes <see cref="EventContext"/> fields into the template.
    /// </summary>
    /// <param name="context">The event whose fields are substituted.</param>
    /// <param name="urlEncode">
    /// When <see langword="true"/> (default), each substituted value is passed through
    /// <c>Uri.EscapeDataString</c> — appropriate for URL construction.
    /// When <see langword="false"/>, raw values are substituted — appropriate for
    /// human-readable message strings (e.g. Pushover notifications).
    /// </param>
    public string Resolve(EventContext context, bool urlEncode = true)
    {
        return TokenRegex().Replace(_template, m =>
        {
            var raw = m.Groups["name"].Value switch
            {
                "camera"           => context.Camera,
                "camera_shortname" => context.CameraShortName ?? context.Camera,
                "label"            => context.Label,
                "event_id"         => context.EventId,
                "zone"             => context.Zones.Count > 0 ? context.Zones[0] : "",
                _                  => m.Value, // unreachable — Parse() guards
            };
            return urlEncode ? Uri.EscapeDataString(raw) : raw;
        });
    }
}
