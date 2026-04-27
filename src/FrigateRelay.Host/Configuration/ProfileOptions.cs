namespace FrigateRelay.Host.Configuration;

internal sealed record ProfileOptions
{
    public IReadOnlyList<ActionEntry> Actions { get; init; } = Array.Empty<ActionEntry>();
}
