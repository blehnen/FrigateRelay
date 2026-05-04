using FrigateRelay.Abstractions;

namespace FrigateRelay.Plugins.BlueIris;

internal sealed class BlueIrisUrlTemplate
{
    private readonly EventTokenTemplate _inner;

    private BlueIrisUrlTemplate(EventTokenTemplate inner) => _inner = inner;

    /// <summary>
    /// Parses the template and validates that every {placeholder} is in the allowlist.
    /// Delegates to <see cref="EventTokenTemplate.Parse"/> — single source of truth for
    /// <see cref="EventTokenTemplate.AllowedTokens"/>, token regex, and error wording.
    /// </summary>
    public static BlueIrisUrlTemplate Parse(string template) =>
        new(EventTokenTemplate.Parse(template, "BlueIris.TriggerUrlTemplate"));

    /// <summary>
    /// Substitutes <see cref="EventContext"/> fields into the template with URL encoding.
    /// BlueIris trigger URLs always require percent-encoded values.
    /// </summary>
    public string Resolve(EventContext ctx) => _inner.Resolve(ctx, urlEncode: true);
}
