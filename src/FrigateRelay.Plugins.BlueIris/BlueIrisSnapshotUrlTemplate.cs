namespace FrigateRelay.Plugins.BlueIris;

/// <summary>
/// Marker wrapper that disambiguates the snapshot URL template singleton from the trigger
/// URL template singleton in the DI container. Both are backed by <see cref="BlueIrisUrlTemplate"/>
/// but injected via distinct wrapper types to prevent silent overwrite.
/// </summary>
internal sealed record BlueIrisSnapshotUrlTemplate(BlueIrisUrlTemplate Template);
