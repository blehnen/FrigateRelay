using FrigateRelay.Abstractions;

namespace FrigateRelay.Host;

/// <summary>
/// Invokes every discovered <see cref="IPluginRegistrar"/> against the shared
/// <see cref="PluginRegistrationContext"/> at composition time (before the host is built).
/// All discovery is explicit and static — no runtime reflection over service descriptors.
/// </summary>
internal static class PluginRegistrarRunner
{
    private static readonly Action<ILogger, string, Exception?> LogRegisteringPlugin =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(2, "RegisteringPlugin"),
            "Registering plugin {Name}");

    /// <summary>
    /// Iterates <paramref name="registrars"/> in order, logging each invocation, and calls
    /// <see cref="IPluginRegistrar.Register"/> with the shared <paramref name="context"/>.
    /// </summary>
    /// <param name="registrars">The registrars to invoke.  May be empty.</param>
    /// <param name="context">The shared registration context (same instance for all registrars).</param>
    /// <param name="logger">Logger for per-registrar trace output.</param>
    public static void RunAll(
        IEnumerable<IPluginRegistrar> registrars,
        PluginRegistrationContext context,
        ILogger logger)
    {
        foreach (var registrar in registrars)
        {
            LogRegisteringPlugin(logger, registrar.GetType().Name, null);
            registrar.Register(context);
        }
    }
}
