using System.ComponentModel.DataAnnotations;

namespace FrigateRelay.Plugins.Pushover;

/// <summary>
/// Configuration options for the Pushover action plugin.
/// </summary>
internal sealed class PushoverOptions
{
    /// <summary>Gets the Pushover application API token.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Pushover.AppToken is required.")]
    public string AppToken { get; init; } = "";

    /// <summary>Gets the Pushover user/group key.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Pushover.UserKey is required.")]
    public string UserKey { get; init; } = "";

    /// <summary>Gets the message template. Tokens: {camera}, {label}, {event_id}, {zone}.</summary>
    public string MessageTemplate { get; init; } = "{label} detected on {camera}";

    /// <summary>Gets the notification title. When null, Pushover renders the app name.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the Pushover message priority (-2 to 2, default 0).</summary>
    [Range(-2, 2, ErrorMessage = "Pushover.Priority must be in the range -2 to 2.")]
    public int Priority { get; init; } = 0;

    /// <summary>Gets the base address for the Pushover API (configurable for testing).</summary>
    public string BaseAddress { get; init; } = "https://api.pushover.net";

    /// <summary>Gets the HTTP request timeout.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets the dispatch queue capacity.</summary>
    public int QueueCapacity { get; init; } = 256;
}
