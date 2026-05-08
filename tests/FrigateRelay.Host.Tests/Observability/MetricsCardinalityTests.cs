using FrigateRelay.Host.Observability;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Host.Tests.Observability;

/// <summary>
/// Unit tests for <see cref="MetricsTagWriter.NormalizeCameraTag"/> — the camera-tag
/// cardinality guard introduced in Phase 16 / issue #18.
/// </summary>
[TestClass]
public sealed class MetricsCardinalityTests
{
    // -----------------------------------------------------------------------
    // Test 1: Known camera (exact case) returns input unchanged.
    // Also asserts that case-insensitive membership does NOT change the caller's casing —
    // only membership matters; the returned string is always the caller's original value.
    // -----------------------------------------------------------------------

    [TestMethod]
    public void NormalizeCameraTag_KnownCamera_ReturnsInputUnchanged()
    {
        var writer = MakeWriter(new MetricsTagsOptions { KnownCameras = ["Driveway"] });

        // Exact-case match → unchanged.
        var result1 = writer.NormalizeCameraTag("Driveway");
        Assert.AreEqual("Driveway", result1, "Known camera with exact casing must be returned unchanged.");

        // Lower-case match (OrdinalIgnoreCase membership) → caller's casing ("driveway") is preserved.
        var result2 = writer.NormalizeCameraTag("driveway");
        Assert.AreEqual("driveway", result2, "Known camera matched case-insensitively must still return the caller's original casing.");
    }

    // -----------------------------------------------------------------------
    // Test 2: Unknown camera value is folded to the literal string "other".
    // -----------------------------------------------------------------------

    [TestMethod]
    public void NormalizeCameraTag_UnknownCamera_ReturnsOther()
    {
        var writer = MakeWriter(new MetricsTagsOptions { KnownCameras = ["Driveway"] });

        var result = writer.NormalizeCameraTag("AttackerInjected");
        Assert.AreEqual("other", result, "Unknown camera must be folded to the literal 'other'.");
    }

    // -----------------------------------------------------------------------
    // Test 3: Empty allowlist → passthrough (current behavior preserved for all operators
    // who have not yet configured KnownCameras).
    // -----------------------------------------------------------------------

    [TestMethod]
    public void NormalizeCameraTag_EmptyAllowlist_ReturnsInputUnchanged()
    {
        var writer = MakeWriter(new MetricsTagsOptions { KnownCameras = [] });

        var result = writer.NormalizeCameraTag("AnythingAtAll");
        Assert.AreEqual("AnythingAtAll", result, "Empty allowlist must pass the camera value through unchanged.");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Constructs a <see cref="MetricsTagWriter"/> with a static, non-reloading options snapshot.
    /// Reuses the <see cref="StaticMonitor{T}"/> pattern from <see cref="CounterIncrementTests"/>.
    /// </summary>
    private static MetricsTagWriter MakeWriter(MetricsTagsOptions options) =>
        new(new StaticMonitor<MetricsTagsOptions>(options));

    private sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
