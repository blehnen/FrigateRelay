using System.ComponentModel.DataAnnotations;

namespace FrigateRelay.Plugins.FrigateSnapshot;

/// <summary>
/// Configuration options for the FrigateSnapshot provider.
/// </summary>
/// <remarks>
/// <para><see cref="BaseUrl"/> must not contain a hard-coded IP address — supply it via environment variable
/// or user-secrets (CLAUDE.md hardcoded-IP and secrets rules).</para>
/// <para>TLS: Frigate is typically served over plain HTTP on a private network. HTTPS with a self-signed
/// certificate is not supported in v1; use a reverse proxy to terminate TLS if required.</para>
/// </remarks>
public sealed record FrigateSnapshotOptions
{
    /// <summary>
    /// Gets the base URL of the Frigate instance (e.g. <c>http://frigate:5000</c>).
    /// Must be supplied via configuration — no default is provided to avoid committing IP addresses.
    /// </summary>
    [Required]
    [Url]
    public required string BaseUrl { get; init; }

    /// <summary>Gets a value indicating whether to fetch the thumbnail image instead of the full snapshot.</summary>
    public bool UseThumbnail { get; init; }

    /// <summary>Gets a value indicating whether to request bounding-box overlay on the snapshot image.</summary>
    public bool IncludeBoundingBox { get; init; }

    /// <summary>Gets a value indicating whether to request a timestamp overlay on the snapshot image.</summary>
    public bool IncludeTimestamp { get; init; }

    /// <summary>Gets a value indicating whether to request a cropped (tight) snapshot image.</summary>
    public bool Crop { get; init; }

    /// <summary>Gets the JPEG quality (0–100) to request, or <see langword="null"/> to use the Frigate default.</summary>
    public int? Quality { get; init; }

    /// <summary>Gets the pixel height to request, or <see langword="null"/> to use the Frigate default.</summary>
    public int? Height { get; init; }

    /// <summary>
    /// Gets the Bearer token for authenticated Frigate deployments.
    /// Defaults to empty string — supply via environment variable, never commit a real token.
    /// </summary>
    public string ApiToken { get; init; } = "";

    /// <summary>Gets the per-request HTTP timeout.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets the number of 404-retry attempts after the initial request.
    /// Frigate publishes the MQTT event before the snapshot is flushed to disk — a single retry
    /// after a short delay mitigates the timing race.
    /// </summary>
    public int Retry404Count { get; init; } = 1;

    /// <summary>Gets the delay between 404-retry attempts.</summary>
    public TimeSpan Retry404Delay { get; init; } = TimeSpan.FromMilliseconds(500);
}
