using System.ComponentModel.DataAnnotations;

namespace FrigateRelay.Plugins.Doods2;

/// <summary>
/// Configuration for one named instance of the DOODS2 validator. Bound from a
/// <c>Validators:&lt;instanceKey&gt;</c> child section of <c>appsettings.json</c> via
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}.Get(string)"/> and the
/// <c>AddOptions&lt;T&gt;(name).Bind(...)</c> named-options pattern.
/// </summary>
/// <remarks>
/// <para>
/// One <see cref="Doods2Options"/> instance per named validator entry — operators express
/// per-detector tuning by creating multiple validator instances with different
/// <see cref="DetectorName"/> + <see cref="MinConfidence"/> + <see cref="AllowedLabels"/>
/// (CONTEXT-14 D3). For example, <c>doods2_persons</c> targeting the <c>tensorflow</c>
/// detector and <c>doods2_vehicles</c> targeting the <c>pytorch</c> detector can coexist.
/// </para>
/// <para>
/// <strong>Self-hosted only.</strong> DOODS2 is a self-hosted inference server (e.g.
/// <c>http://doods:10200</c>). No authentication surface — DOODS2 ships with no API key.
/// </para>
/// <para>
/// <strong>Dual transport (CONTEXT-14 D4).</strong> <see cref="Transport"/> selects between
/// <see cref="Doods2Transport.Http"/> (<c>POST /detect</c>) and <see cref="Doods2Transport.Grpc"/>
/// (gRPC <c>odrpc.Detect</c> RPC). Default is HTTP for least-friction deployment.
/// gRPC is faster on the hot path once a persistent HTTP/2 channel is established.
/// </para>
/// <para>
/// <strong>Confidence scale.</strong> DOODS2 returns confidence values in the 0–100 range
/// (e.g. <c>87.4</c> for 87.4% confidence). The validator normalizes these internally by
/// dividing by 100 before comparing to <see cref="MinConfidence"/>. Operators always express
/// <see cref="MinConfidence"/> on the 0.0–1.0 scale, consistent with all other validators.
/// </para>
/// </remarks>
public sealed class Doods2Options
{
    /// <summary>Base URL of the self-hosted DOODS2 server, e.g. <c>http://doods:10200</c>.</summary>
    [Required, Url]
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// Transport protocol to use when communicating with the DOODS2 server.
    /// Default is <see cref="Doods2Transport.Http"/> for least-friction deployment.
    /// Set to <see cref="Doods2Transport.Grpc"/> for lower latency on the hot path
    /// (CONTEXT-14 D4).
    /// </summary>
    public Doods2Transport Transport { get; set; } = Doods2Transport.Http;

    /// <summary>
    /// Detector name to use. Must match a detector defined on the DOODS2 server
    /// (e.g. <c>"default"</c>, <c>"tensorflow"</c>, <c>"pytorch"</c>). Sent as
    /// <c>detector_name</c> in the request body/gRPC message.
    /// </summary>
    [Required]
    public string DetectorName { get; set; } = "default";

    /// <summary>Minimum detection confidence (0.0-1.0) to be accepted as a positive match. DOODS2 returns 0-100; the validator normalizes internally.</summary>
    [Range(0.0, 1.0)]
    public double MinConfidence { get; set; } = 0.5;

    /// <summary>
    /// Allowed detection labels. When empty, any label passes (combined with the confidence
    /// gate). When non-empty, detections whose <c>label</c> field is not in this list are
    /// rejected (case-insensitive ordinal comparison).
    /// </summary>
    public string[] AllowedLabels { get; set; } = [];

    /// <summary>
    /// What verdict to return when the underlying call fails (timeout, network error,
    /// non-success HTTP status, gRPC error). Default <see cref="Doods2ValidatorErrorMode.FailClosed"/>
    /// matches the legacy intent: don't notify if you can't confirm.
    /// </summary>
    public Doods2ValidatorErrorMode OnError { get; set; } = Doods2ValidatorErrorMode.FailClosed;

    /// <summary>HTTP/gRPC timeout for the validator request. Default 5 seconds.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Per-instance opt-in to bypass TLS certificate validation for the HTTP transport.
    /// Configures a per-instance <c>SocketsHttpHandler.SslOptions.RemoteCertificateValidationCallback</c>
    /// via <c>ConfigurePrimaryHttpMessageHandler</c>. NEVER set globally on
    /// <c>ServicePointManager</c> (PROJECT.md / CLAUDE.md invariant).
    /// <para>
    /// This option applies to the HTTP transport only. TLS skip for gRPC transport is not
    /// supported in v1.2 — file a follow-up issue if needed.
    /// </para>
    /// </summary>
    public bool AllowInvalidCertificates { get; set; }
}

/// <summary>Transport protocol for <see cref="Doods2Options.Transport"/>.</summary>
public enum Doods2Transport
{
    /// <summary>HTTP REST transport: <c>POST /detect</c>. Default.</summary>
    Http,

    /// <summary>gRPC transport: <c>odrpc.Detect</c> RPC. Lower latency on hot path (CONTEXT-14 D4).</summary>
    Grpc,
}

/// <summary>Failure stance for <see cref="Doods2Options.OnError"/>.</summary>
public enum Doods2ValidatorErrorMode
{
    /// <summary>Skip the action when the validator is unavailable. Safe default.</summary>
    FailClosed,

    /// <summary>Allow the action to proceed when the validator is unavailable. Risk: notifies during outages.</summary>
    FailOpen,
}
