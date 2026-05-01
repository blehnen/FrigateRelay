using System.Globalization;

namespace FrigateRelay.IntegrationTests.RealBroker;

/// <summary>
/// Operator-supplied environment for the opt-in real-broker test profile.
/// Tests in this project self-skip via <see cref="SkipIfNotEnabled"/> unless
/// <c>FRIGATERELAY_TEST_REAL_BROKER=1</c> is set, so CI (which never sets it)
/// reports these as Inconclusive/Skipped and never times out connecting to
/// a non-existent broker.
/// </summary>
internal static class RealBrokerEnvironment
{
    public const string OptInVar = "FRIGATERELAY_TEST_REAL_BROKER";
    public const string HostVar = "FRIGATERELAY_TEST_MQTT_HOST";
    public const string PortVar = "FRIGATERELAY_TEST_MQTT_PORT";
    public const string UsernameVar = "FRIGATERELAY_TEST_MQTT_USERNAME";
    public const string PasswordVar = "FRIGATERELAY_TEST_MQTT_PASSWORD";

    /// <summary>
    /// Topic prefix the real-broker tests publish under. Deliberately distinct
    /// from Frigate's <c>frigate/events</c> so a misconfigured test cannot
    /// trigger production action plugins listening on the real topic.
    /// </summary>
    public const string TestTopicPrefix = "frigaterelay-test";

    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(OptInVar), "1", StringComparison.Ordinal);

    /// <summary>
    /// Marks the calling test Inconclusive (Skipped) when the real-broker
    /// profile is not enabled. Call as the first line of every test method.
    /// </summary>
    public static void SkipIfNotEnabled()
    {
        if (!IsEnabled)
        {
            Assert.Inconclusive(
                $"Real-broker tests are opt-in. Set {OptInVar}=1 plus {HostVar} (and optionally " +
                $"{PortVar}, {UsernameVar}, {PasswordVar}) to enable. See CONTRIBUTING.md.");
        }
    }

    /// <summary>
    /// Returns the operator-configured broker endpoint. Throws if the opt-in
    /// flag is set but required vars are missing — fail-fast with a clear
    /// diagnostic rather than time out connecting to nothing.
    /// </summary>
    public static (string Host, int Port) GetBroker()
    {
        var host = Environment.GetEnvironmentVariable(HostVar);
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException($"{HostVar} must be set when {OptInVar}=1");

        var portRaw = Environment.GetEnvironmentVariable(PortVar);
        if (string.IsNullOrWhiteSpace(portRaw))
            return (host, 1883);

        if (!int.TryParse(portRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
            throw new InvalidOperationException($"{PortVar} must be an integer; got '{portRaw}'");

        return (host, port);
    }

    public static (string? Username, string? Password) GetCredentials()
        => (
            Environment.GetEnvironmentVariable(UsernameVar),
            Environment.GetEnvironmentVariable(PasswordVar));

    /// <summary>
    /// Builds a unique-per-run topic under <see cref="TestTopicPrefix"/>. Two
    /// concurrent test runs against the same broker (or a leftover retained
    /// message from a prior run) cannot cross-contaminate.
    /// </summary>
    public static string NewIsolatedTopic()
        => $"{TestTopicPrefix}/{Guid.NewGuid():N}/events";

    /// <summary>
    /// Builds a unique ClientId so two concurrent test runs against the same
    /// broker do not race each other (the same foot-gun #18 documents).
    /// </summary>
    public static string NewIsolatedClientId()
        => $"frigaterelay-test-{Guid.NewGuid():N}";
}
