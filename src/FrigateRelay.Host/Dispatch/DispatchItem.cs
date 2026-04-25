using System.Diagnostics;
using FrigateRelay.Abstractions;

namespace FrigateRelay.Host.Dispatch;

/// <summary>
/// A lightweight value type that carries all state needed to execute one action plugin invocation.
/// Stored in per-plugin bounded channels; the struct design avoids GC pressure when channels hold
/// large numbers of pending items.
/// </summary>
internal readonly record struct DispatchItem(
    EventContext Context,
    IActionPlugin Plugin,
    IReadOnlyList<IValidationPlugin> Validators,
    Activity? Activity);
