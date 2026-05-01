using System.ComponentModel.DataAnnotations;

namespace FrigateRelay.Plugins.CodeProjectAi;

/// <summary>
/// Configuration for one named instance of the CodeProject.AI validator. Bound from a
/// <c>Validators:&lt;instanceKey&gt;</c> child section of <c>appsettings.json</c> via
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}.Get(string)"/> and the
/// <c>AddOptions&lt;T&gt;(name).Bind(...)</c> named-options pattern.
/// </summary>
/// <remarks>
/// <para>
/// One <see cref="CodeProjectAiOptions"/> instance per named validator entry — operators
/// express per-label tuning by creating multiple validator instances with different
/// <see cref="MinConfidence"/> + <see cref="AllowedLabels"/> (CONTEXT-7 D2 / D3).
/// </para>
/// <para>
/// <strong>Backend support.</strong> This plugin works against two server-side backends
/// that share the <c>POST /v1/vision/detection</c> request shape:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <strong>CodeProject.AI</strong> — the historical default. Active upstream development
/// has stopped, but current and older installs continue to work unchanged.
/// </description>
/// </item>
/// <item>
/// <description>
/// <strong><see href="https://github.com/xnorpx/blue-onyx">Blue Onyx</see></strong> —
/// verified working through this plugin with no code change; only <see cref="BaseUrl"/>
/// needs to point at the Blue Onyx host. Note that NVIDIA GPU acceleration is available
/// only via Blue Onyx's Windows EXE/service distribution; the Docker image is CPU-only.
/// </description>
/// </item>
/// </list>
/// <para>
/// See the "Validator engine status" section in the project README for the full
/// alternative-backends roadmap (Roboflow Inference, DOODS2 — each needs its own plugin
/// since the API shapes differ from CPAI/Blue Onyx).
/// </para>
/// </remarks>
public sealed class CodeProjectAiOptions
{
    /// <summary>Base URL of the CodeProject.AI server, e.g. <c>http://codeproject-ai:5000</c>.</summary>
    [Required, Url]
    public string BaseUrl { get; set; } = "";

    /// <summary>Minimum prediction confidence (0.0-1.0) to be accepted as a positive match.</summary>
    [Range(0.0, 1.0)]
    public double MinConfidence { get; set; } = 0.5;

    /// <summary>
    /// Allowed prediction labels. When empty, any label passes (combined with the confidence
    /// gate). When non-empty, predictions whose <c>label</c> is not in this list are rejected
    /// (case-insensitive ordinal comparison).
    /// </summary>
    public string[] AllowedLabels { get; set; } = [];

    /// <summary>
    /// What verdict to return when the underlying HTTP call fails (timeout, network error,
    /// non-success HTTP status). Default <see cref="ValidatorErrorMode.FailClosed"/> matches
    /// the legacy intent: don't notify if you can't confirm.
    /// </summary>
    public ValidatorErrorMode OnError { get; set; } = ValidatorErrorMode.FailClosed;

    /// <summary>HTTP timeout for the validator's POST. Default 5 seconds.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Per-instance opt-in to bypass TLS certificate validation. Configures a per-instance
    /// <c>SocketsHttpHandler.SslOptions.RemoteCertificateValidationCallback</c> via
    /// <c>ConfigurePrimaryHttpMessageHandler</c>. NEVER set globally on
    /// <c>ServicePointManager</c> (PROJECT.md / CLAUDE.md invariant).
    /// </summary>
    public bool AllowInvalidCertificates { get; set; }
}

/// <summary>Failure stance for <see cref="CodeProjectAiOptions.OnError"/>.</summary>
public enum ValidatorErrorMode
{
    /// <summary>Skip the action when the validator is unavailable. Safe default.</summary>
    FailClosed,

    /// <summary>Allow the action to proceed when the validator is unavailable. Risk: notifies during outages.</summary>
    FailOpen,
}
