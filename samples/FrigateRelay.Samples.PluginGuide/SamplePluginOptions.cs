namespace FrigateRelay.Samples.PluginGuide;

/// <summary>
/// Configuration options for <see cref="SampleActionPlugin"/>.
/// Bound from the <c>Sample</c> configuration section.
/// </summary>
public sealed class SamplePluginOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the action plugin should attempt to
    /// resolve and log snapshot bytes. Defaults to <see langword="false"/>.
    /// </summary>
    public bool FetchSnapshot { get; set; }
}
