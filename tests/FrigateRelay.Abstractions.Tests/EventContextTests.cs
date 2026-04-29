using System.Reflection;
using FluentAssertions;
using FrigateRelay.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Abstractions.Tests;

[TestClass]
public sealed class EventContextTests
{
    // (f) Every settable property on EventContext is init-only (immutability check via Reflection)
    [TestMethod]
    public void EventContext_AllMembers_AreInitOnly()
    {
        var properties = typeof(EventContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToList();

        properties.Should().NotBeEmpty("EventContext must have writable properties");

        foreach (var prop in properties)
        {
            var setter = prop.SetMethod;
            setter.Should().NotBeNull(
                $"property {prop.Name} must have a setter");

            // In .NET, an init-only setter is marked with a modreq on
            // System.Runtime.CompilerServices.IsExternalInit. The reliable
            // way to detect this is via the ReturnParameter's required
            // custom modifiers.
            var modifiers = setter!.ReturnParameter
                .GetRequiredCustomModifiers();
            modifiers.Should().Contain(
                t => t.Name == "IsExternalInit",
                $"property {prop.Name} must be init-only");
        }
    }

    // (g) Zones defaults to an empty list when not supplied
    [TestMethod]
    public void Zones_DefaultsToEmpty()
    {
        var ctx = new EventContext
        {
            EventId       = "evt-001",
            Camera        = "front",
            Label         = "person",
            StartedAt     = DateTimeOffset.UtcNow,
            RawPayload    = "{}",
            SnapshotFetcher = _ => ValueTask.FromResult<byte[]?>(null),
        };

        ctx.Zones.Should().NotBeNull();
        ctx.Zones.Should().BeEmpty();
    }
}
