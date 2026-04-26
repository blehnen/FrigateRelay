using System.Text.Json.Serialization;

namespace FrigateRelay.Host.Configuration;

/// <summary>
/// Represents a single action entry within a subscription's <c>Actions</c> list.
/// Carries the plugin name and an optional per-action snapshot provider override.
/// </summary>
/// <param name="Plugin">
/// The name of the action plugin to invoke (case-insensitive match against registered
/// <c>IActionPlugin</c> names). Required.
/// </param>
/// <param name="SnapshotProvider">
/// Optional name of the snapshot provider to use for this action. When <see langword="null"/>,
/// the per-subscription default or global default is used (resolver tiering from Plan 1.1).
/// </param>
[JsonConverter(typeof(ActionEntryJsonConverter))]
public sealed record ActionEntry(string Plugin, string? SnapshotProvider = null);
