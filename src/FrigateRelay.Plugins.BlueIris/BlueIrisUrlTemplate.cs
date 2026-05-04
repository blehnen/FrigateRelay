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
    /// <param name="template">The URL template string with <c>{token}</c> placeholders.</param>
    /// <param name="callerName">
    /// The config-key name used in parse-error messages so operators see the correct key
    /// (defaults to <c>BlueIris.TriggerUrlTemplate</c>; pass <c>BlueIris.SnapshotUrlTemplate</c>
    /// when validating the snapshot URL).
    /// </param>
    public static BlueIrisUrlTemplate Parse(string template, string callerName = "BlueIris.TriggerUrlTemplate") =>
        new(EventTokenTemplate.Parse(template, callerName));

    /// <summary>
    /// Substitutes <see cref="EventContext"/> fields into the template with URL encoding.
    /// BlueIris trigger URLs always require percent-encoded values.
    /// </summary>
    public string Resolve(EventContext ctx) => _inner.Resolve(ctx, urlEncode: true);
}
