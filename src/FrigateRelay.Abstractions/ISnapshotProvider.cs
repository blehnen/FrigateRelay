namespace FrigateRelay.Abstractions;

/// <summary>Represents a plugin that can fetch a snapshot image for a given event, returning <see langword="null"/> when no image is available.</summary>
public interface ISnapshotProvider
{
    /// <summary>Gets the unique name of this snapshot provider.</summary>
    string Name { get; }

    /// <summary>Fetches a snapshot for the given request, or returns <see langword="null"/> if the provider cannot produce one.</summary>
    /// <param name="request">The snapshot request carrying event context and fetch options.</param>
    /// <param name="ct">A token that signals the host is shutting down.</param>
    /// <returns>A <see cref="Task{TResult}"/> whose result is a <see cref="SnapshotResult"/>, or <see langword="null"/> if unavailable.</returns>
    Task<SnapshotResult?> FetchAsync(SnapshotRequest request, CancellationToken ct);
}
