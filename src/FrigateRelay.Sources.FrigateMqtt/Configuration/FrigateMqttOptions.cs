namespace FrigateRelay.Sources.FrigateMqtt.Configuration;

/// <summary>
/// MQTT transport configuration for the Frigate event source plugin.
/// Bound from the <c>FrigateMqtt</c> configuration section (e.g. in <c>appsettings.json</c>).
/// </summary>
/// <remarks>
/// Subscription rules are bound separately from the top-level <c>Subscriptions</c>
/// configuration section by the host; this options type covers MQTT transport only.
/// </remarks>
public sealed record FrigateMqttOptions
{
    /// <summary>
    /// Hostname or IP address of the MQTT broker.
    /// Default: <c>"localhost"</c>.
    /// Do not commit hard-coded production hostnames — supply via environment variable or user secrets.
    /// </summary>
    public string Server { get; init; } = "localhost";

    /// <summary>
    /// TCP port on which the MQTT broker is listening.
    /// Default: <c>1883</c> (standard unencrypted MQTT port).
    /// Use <c>8883</c> for TLS-secured connections.
    /// </summary>
    public int Port { get; init; } = 1883;

    /// <summary>
    /// MQTT client identifier sent to the broker on connect.
    /// Must be unique per connected client; duplicate client IDs cause broker takeover loops
    /// (the broker disconnects whichever instance was already connected, the reconnect loop
    /// re-establishes, and the cycle thrashes — see issue #18 for the symptom pattern).
    /// </summary>
    /// <remarks>
    /// Default is <c>"frigate-relay-{MachineName}-{ProcessId}"</c> so two instances launched
    /// with default config (e.g. production container plus a developer's local debug run, or
    /// two HA replicas) get distinct ClientIds out of the box. Operators on strict MQTT 3.1.1
    /// brokers that enforce the 23-character ClientId limit should override this with a
    /// shorter explicit value.
    /// </remarks>
    public string ClientId { get; init; } = DefaultClientId;

    private static readonly string DefaultClientId =
        $"frigate-relay-{Environment.MachineName}-{Environment.ProcessId}";

    /// <summary>
    /// MQTT topic to subscribe to for Frigate event messages.
    /// Default: <c>"frigate/events"</c> (Frigate's standard event topic).
    /// </summary>
    public string Topic { get; init; } = "frigate/events";

    /// <summary>
    /// TLS/SSL options for the MQTT broker connection.
    /// </summary>
    public FrigateMqttTlsOptions Tls { get; init; } = new();
}

/// <summary>
/// TLS/SSL transport options for the MQTT broker connection.
/// Applied per-client via MQTTnet's <c>WithTlsOptions</c> — never via a global callback.
/// </summary>
public sealed record FrigateMqttTlsOptions
{
    /// <summary>
    /// When <c>true</c>, TLS encryption is enabled for the broker connection.
    /// Default: <c>false</c>.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// When <c>true</c>, server certificate validation is bypassed for this client.
    /// Should only be set to <c>true</c> in development environments with self-signed certificates.
    /// Default: <c>false</c> (safe production default — always validate certificates).
    /// </summary>
    public bool AllowInvalidCertificates { get; init; }
}
