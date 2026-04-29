using System.ComponentModel;
using System.Text.Json.Serialization;

namespace FrigateRelay.Host.Configuration;

/// <summary>
/// Represents a single action entry within a subscription's <c>Actions</c> list.
/// Carries the plugin name, an optional per-action snapshot provider override, and an
/// optional list of named validator instance keys that gate this action.
/// </summary>
/// <param name="Plugin">
/// The name of the action plugin to invoke (case-insensitive match against registered
/// <c>IActionPlugin</c> names). Required.
/// </param>
/// <param name="SnapshotProvider">
/// Optional name of the snapshot provider to use for this action. When <see langword="null"/>,
/// the per-subscription default or global default is used (resolver tiering from Plan 1.1).
/// </param>
/// <param name="Validators">
/// Optional list of named validator instance keys to gate this action. Each key must be
/// defined in the top-level <c>Validators</c> configuration section.
/// Empty/null = no validators (action fires unconditionally).
/// Validators run BEFORE the action's Polly retry pipeline; a failing verdict
/// short-circuits this action only — other actions in the same event fire independently.
/// Consumers MUST treat <see langword="null"/> and empty-list identically:
/// <c>(action.Validators?.Count ?? 0) == 0</c>.
/// </param>
[TypeConverter(typeof(ActionEntryTypeConverter))]
[JsonConverter(typeof(ActionEntryJsonConverter))]
internal sealed record ActionEntry(
    string Plugin,
    string? SnapshotProvider = null,
    IReadOnlyList<string>? Validators = null);
