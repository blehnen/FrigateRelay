using FrigateRelay.Abstractions;
using Microsoft.Extensions.Logging;

namespace FrigateRelay.Samples.PluginGuide;

/// <summary>
/// Demonstrates a minimal <see cref="IValidationPlugin"/> implementation.
/// Returns <see cref="Verdict.Pass()"/> when the detected label is "person";
/// returns <see cref="Verdict.Fail(string)"/> for any other label.
/// </summary>
/// <remarks>
/// <para>
/// Validators run per-action, not globally (CLAUDE.md V3). A failing verdict
/// short-circuits THAT action only — other actions in the same event continue
/// independently.
/// </para>
/// <para>
/// The snapshot parameter mirrors the action plugin signature.
/// When validators are present, the dispatcher pre-resolves the snapshot ONCE and
/// shares it across the validator chain and the action, avoiding redundant HTTP
/// fetches. Metadata-only validators (like this one) can ignore the parameter.
/// </para>
/// </remarks>
public sealed partial class SampleValidationPlugin : IValidationPlugin
{
    private readonly ILogger<SampleValidationPlugin> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="SampleValidationPlugin"/>.
    /// </summary>
    /// <param name="logger">The logger provided by the host DI container.</param>
    public SampleValidationPlugin(ILogger<SampleValidationPlugin> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "SampleValidator";

    /// <inheritdoc />
    public Task<Verdict> ValidateAsync(
        EventContext ctx,
        SnapshotContext snapshot,
        CancellationToken ct)
    {
        if (ctx.Label == "person")
        {
            LogPass(_logger, ctx.EventId, ctx.Label);
            return Task.FromResult(Verdict.Pass());
        }

        LogFail(_logger, ctx.EventId, ctx.Label);
        return Task.FromResult(Verdict.Fail($"label '{ctx.Label}' is not 'person'"));
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "sample_validator pass event_id={EventId} label={Label}")]
    private static partial void LogPass(ILogger logger, string eventId, string label);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "sample_validator fail event_id={EventId} label={Label}")]
    private static partial void LogFail(ILogger logger, string eventId, string label);
}
