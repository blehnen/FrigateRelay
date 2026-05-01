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
/// <strong>Upstream status note.</strong> Active CodeProject.AI development has stopped
/// upstream. This plugin remains supported because (a) older CPAI installs still work, and
/// (b) the plugin's <c>/v1/vision/detection</c> request shape is API-compatible with
/// <see href="https://github.com/MikeLud/CodeProject.AI-Custom-IPcam-Models/discussions">Blue Onyx</see>,
/// so existing users on either backend should keep using it. Operators standing up a
/// <em>new</em> validator setup are encouraged to evaluate alternatives — see the
/// "Validator engine status" section in the project README.
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
