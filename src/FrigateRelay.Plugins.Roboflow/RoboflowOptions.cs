using System.ComponentModel.DataAnnotations;

namespace FrigateRelay.Plugins.Roboflow;

/// <summary>
/// Configuration for one named instance of the Roboflow Inference validator. Bound from a
/// <c>Validators:&lt;instanceKey&gt;</c> child section of <c>appsettings.json</c> via
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}.Get(string)"/> and the
/// <c>AddOptions&lt;T&gt;(name).Bind(...)</c> named-options pattern.
/// </summary>
/// <remarks>
/// <para>
/// One <see cref="RoboflowOptions"/> instance per named validator entry — operators express
/// per-model tuning by creating multiple validator instances with different
/// <see cref="ModelId"/> + <see cref="MinConfidence"/> + <see cref="AllowedLabels"/>
/// (CONTEXT-14 D3). For example, <c>roboflow_persons</c> and <c>roboflow_vehicles</c> can
/// each target a different model while sharing the same self-hosted Inference server.
/// </para>
/// <para>
/// <strong>Self-hosted only (CONTEXT-14 D2).</strong> This plugin targets the self-hosted
/// Roboflow Inference server (e.g. <c>http://roboflow:9001</c>). The Roboflow Hosted Cloud
/// API is out of scope for v1.2 — auth surface and quota error handling are deferred.
/// </para>
/// <para>
/// <strong>API contract.</strong> Uses <c>POST /infer/object_detection</c> as documented in
/// the Roboflow Inference v0.x/v1.x REST API. The <see cref="ModelId"/> must include the
/// version suffix (e.g. <c>rfdetr-base/1</c>). Confidence values in the response are in the
/// 0.0–1.0 range — no normalization is applied (contrast with DOODS2, which uses 0–100).
/// </para>
/// </remarks>
public sealed class RoboflowOptions
{
    /// <summary>Base URL of the self-hosted Roboflow Inference server, e.g. <c>http://roboflow:9001</c>.</summary>
    [Required, Url]
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// Model identifier including version suffix, e.g. <c>rfdetr-base/1</c>. Sent as
    /// <c>model_id</c> in the request body. Operators declare one validator instance per model
    /// (CONTEXT-14 D3).
    /// </summary>
    [Required]
    public string ModelId { get; set; } = "";

    /// <summary>Minimum prediction confidence (0.0-1.0) to be accepted as a positive match.</summary>
    [Range(0.0, 1.0)]
    public double MinConfidence { get; set; } = 0.5;

    /// <summary>
    /// Allowed prediction labels. When empty, any label passes (combined with the confidence
    /// gate). When non-empty, predictions whose <c>class</c> field is not in this list are
    /// rejected (case-insensitive ordinal comparison).
    /// </summary>
    public string[] AllowedLabels { get; set; } = [];

    /// <summary>
    /// What verdict to return when the underlying HTTP call fails (timeout, network error,
    /// non-success HTTP status). Default <see cref="RoboflowValidatorErrorMode.FailClosed"/>
    /// matches the legacy intent: don't notify if you can't confirm.
    /// </summary>
    public RoboflowValidatorErrorMode OnError { get; set; } = RoboflowValidatorErrorMode.FailClosed;

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

/// <summary>Failure stance for <see cref="RoboflowOptions.OnError"/>.</summary>
public enum RoboflowValidatorErrorMode
{
    /// <summary>Skip the action when the validator is unavailable. Safe default.</summary>
    FailClosed,

    /// <summary>Allow the action to proceed when the validator is unavailable. Risk: notifies during outages.</summary>
    FailOpen,
}
