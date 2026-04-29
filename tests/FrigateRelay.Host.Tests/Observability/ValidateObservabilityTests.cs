using FluentAssertions;
using FrigateRelay.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Host.Tests.Observability;

/// <summary>
/// Unit tests for <see cref="StartupValidation.ValidateObservability"/> (ID-16 closure).
/// Uses <see cref="ConfigurationBuilder"/> with in-memory collection — no full host required.
/// The method is <c>internal static</c> and accessible via <c>InternalsVisibleTo</c> in
/// <c>FrigateRelay.Host.csproj</c>.
/// </summary>
[TestClass]
public sealed class ValidateObservabilityTests
{
    // -----------------------------------------------------------------------
    // Test 1: malformed Otel:OtlpEndpoint → one error containing the key name
    // -----------------------------------------------------------------------

    [TestMethod]
    public void MalformedOtlpEndpoint_ProducesOneError_ContainingKeyName()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Otel:OtlpEndpoint"] = "not-a-uri",
                ["Serilog:Seq:ServerUrl"] = "",   // empty = no validation
            })
            .Build();

        var errors = new List<string>();
        StartupValidation.ValidateObservability(config, errors);

        errors.Should().HaveCount(1, "exactly one error for malformed OTLP endpoint");
        errors[0].Should().Contain("Otel:OtlpEndpoint",
            "error message must name the offending config key so operators can find it");
    }

    // -----------------------------------------------------------------------
    // Test 2: malformed Serilog:Seq:ServerUrl → one error containing the key name
    // -----------------------------------------------------------------------

    [TestMethod]
    public void MalformedSeqServerUrl_ProducesOneError_ContainingKeyName()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Otel:OtlpEndpoint"] = "",         // empty = no validation
                ["Serilog:Seq:ServerUrl"] = "not-a-uri",
            })
            .Build();

        var errors = new List<string>();
        StartupValidation.ValidateObservability(config, errors);

        errors.Should().HaveCount(1, "exactly one error for malformed Seq server URL");
        errors[0].Should().Contain("Serilog:Seq:ServerUrl",
            "error message must name the offending config key so operators can find it");
    }

    // -----------------------------------------------------------------------
    // Test 3: valid absolute URIs for both keys → zero errors
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ValidAbsoluteUris_ProduceZeroErrors()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Otel:OtlpEndpoint"] = "http://otel-collector:4317",
                ["Serilog:Seq:ServerUrl"] = "http://seq:5341",
            })
            .Build();

        var errors = new List<string>();
        StartupValidation.ValidateObservability(config, errors);

        errors.Should().BeEmpty("valid absolute URIs must produce no validation errors");
    }
}
