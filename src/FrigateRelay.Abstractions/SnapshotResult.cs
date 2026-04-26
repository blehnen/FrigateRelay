namespace FrigateRelay.Abstractions;

/// <summary>
/// Contains the raw bytes and metadata returned by a snapshot provider after a successful snapshot fetch.
/// </summary>
public sealed record SnapshotResult
{
    /// <summary>Gets the raw image bytes returned by the provider.</summary>
    public required byte[] Bytes { get; init; }

    /// <summary>Gets the MIME content-type of <see cref="Bytes"/> (e.g. <c>image/jpeg</c>).</summary>
    public required string ContentType { get; init; }

    /// <summary>Gets the name of the provider that produced this result.</summary>
    public required string ProviderName { get; init; }

    /// <summary>
    /// Gets the HTTP ETag value returned by the provider, if any.
    /// Reserved for future conditional-fetch caching; not populated in v1.
    /// </summary>
    public string? ETag { get; init; }
}
