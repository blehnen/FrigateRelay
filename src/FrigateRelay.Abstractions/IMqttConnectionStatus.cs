namespace FrigateRelay.Abstractions;

/// <summary>
/// Reports the current MQTT connection state. Updated by the MQTT event source
/// on connect/disconnect; consumed by the host-side health check for the /healthz
/// readiness endpoint.
/// </summary>
/// <remarks>
/// Lives in <c>FrigateRelay.Abstractions</c> so that both the source project
/// (<c>FrigateRelay.Sources.FrigateMqtt</c>) and the host project
/// (<c>FrigateRelay.Host</c>) can reference it without creating a circular
/// project dependency.
/// </remarks>
public interface IMqttConnectionStatus
{
    /// <summary>
    /// <see langword="true"/> when the MQTT client is currently connected to the broker;
    /// <see langword="false"/> otherwise (initial state, disconnected, or reconnecting).
    /// </summary>
    bool IsConnected { get; }

    /// <summary>Updates the connection state. Thread-safe.</summary>
    void SetConnected(bool connected);
}
