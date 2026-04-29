using System.Diagnostics.Metrics;
using FluentAssertions;
using FrigateRelay.Host.Dispatch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Host.Tests.Observability;

/// <summary>
/// Surface-only assertions on <see cref="DispatcherDiagnostics"/> static fields —
/// confirms the meter name, activity source name, and all expected counter instrument
/// names are published when the meter is active. No SUT execution required.
/// </summary>
[TestClass]
public sealed class DispatcherDiagnosticsTests
{
    // -----------------------------------------------------------------------
    // Test 1: Meter.Name must be "FrigateRelay" (CLAUDE.md observability invariant)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Meter_Name_IsFrigateRelay()
    {
        DispatcherDiagnostics.Meter.Name.Should().Be("FrigateRelay");
    }

    // -----------------------------------------------------------------------
    // Test 2: ActivitySource.Name must be "FrigateRelay" (CLAUDE.md invariant)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ActivitySource_Name_IsFrigateRelay()
    {
        DispatcherDiagnostics.ActivitySource.Name.Should().Be("FrigateRelay");
    }

    // -----------------------------------------------------------------------
    // Test 3: All 10 expected counter instrument names are published via the meter.
    //         Uses MeterListener.InstrumentPublished to capture names when
    //         MeterListener.Start() triggers publication callbacks.
    // -----------------------------------------------------------------------

    [TestMethod]
    public void AllExpectedCountersDeclared()
    {
        var expectedNames = new HashSet<string>
        {
            "frigaterelay.events.received",
            "frigaterelay.events.matched",
            "frigaterelay.actions.dispatched",
            "frigaterelay.actions.succeeded",
            "frigaterelay.actions.failed",
            "frigaterelay.validators.passed",
            "frigaterelay.validators.rejected",
            "frigaterelay.errors.unhandled",
            "frigaterelay.dispatch.drops",
            "frigaterelay.dispatch.exhausted",
        };

        var published = new HashSet<string>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, _) =>
        {
            if (instrument.Meter.Name == "FrigateRelay")
                published.Add(instrument.Name);
        };

        // Start() triggers InstrumentPublished for all currently-active instruments.
        listener.Start();

        // Touch each static counter field to ensure the meter registration is not
        // lazily deferred (in practice they are all initialized at type-load time,
        // but the explicit references keep the assertion honest).
        _ = DispatcherDiagnostics.EventsReceived;
        _ = DispatcherDiagnostics.EventsMatched;
        _ = DispatcherDiagnostics.ActionsDispatched;
        _ = DispatcherDiagnostics.ActionsSucceeded;
        _ = DispatcherDiagnostics.ActionsFailed;
        _ = DispatcherDiagnostics.ValidatorsPassed;
        _ = DispatcherDiagnostics.ValidatorsRejected;
        _ = DispatcherDiagnostics.ErrorsUnhandled;
        _ = DispatcherDiagnostics.Drops;
        _ = DispatcherDiagnostics.Exhausted;

        foreach (var name in expectedNames)
        {
            published.Should().Contain(name, $"counter '{name}' must be published by the FrigateRelay meter");
        }
    }
}
