using FrigateRelay.Abstractions;

namespace FrigateRelay.Host.Health;

/// <summary>
/// Thread-safe implementation of <see cref="IMqttConnectionStatus"/> using
/// <see cref="Volatile"/> reads/writes over a backing <see cref="int"/> field
/// (0 = disconnected, 1 = connected).
/// </summary>
internal sealed class MqttConnectionStatus : IMqttConnectionStatus
{
    // 0 = disconnected, 1 = connected. Use int so Volatile can operate on it without boxing.
    private int _connected;

    /// <inheritdoc />
    public bool IsConnected => Volatile.Read(ref _connected) == 1;

    /// <inheritdoc />
    public void SetConnected(bool connected) =>
        Volatile.Write(ref _connected, connected ? 1 : 0);
}
