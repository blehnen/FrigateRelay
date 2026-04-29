using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Host;
using FrigateRelay.Host.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace FrigateRelay.Host.Tests.Configuration;

/// <summary>
/// Unit tests for <see cref="StartupValidation.ValidateSerilogPath"/> (ID-21 closure, PLAN-1.2).
/// Mirrors the <c>ValidateObservabilityTests</c> shape: builds <see cref="IConfiguration"/> via
/// <c>ConfigurationBuilder.AddInMemoryCollection</c>, invokes the internal static method directly,
/// and asserts on the <c>errors</c> accumulator (D7 collect-all pattern).
/// </summary>
[TestClass]
public sealed class SerilogPathValidationTests
{
    // Helper: build a config with a single Serilog file sink pointing at the given path.
    private static IConfiguration ConfigWithSinkPath(string path) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Serilog:WriteTo:0:Name"] = "File",
                ["Serilog:WriteTo:0:Args:path"] = path,
            })
            .Build();

    // -----------------------------------------------------------------------
    // Test 1: path traversal via ".." segments
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ValidateSerilogPath_DotDotPath_AddsTraversalError()
    {
        var config = ConfigWithSinkPath("../../etc/passwd");
        var errors = new List<string>();

        StartupValidation.ValidateSerilogPath(config, errors);

        errors.Should().ContainSingle("exactly one error for a path-traversal path");
        errors[0].Should().Contain("path traversal",
            "error message must call out the traversal risk so operators understand the rejection");
    }

    // -----------------------------------------------------------------------
    // Test 2: UNC path \\server\share\...
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ValidateSerilogPath_UncPath_AddsUncError()
    {
        var config = ConfigWithSinkPath(@"\\server\share\log.txt");
        var errors = new List<string>();

        StartupValidation.ValidateSerilogPath(config, errors);

        errors.Should().ContainSingle("exactly one error for a UNC path");
        errors[0].Should().Contain("UNC",
            "error message must identify the rejection reason as UNC");
    }

    // -----------------------------------------------------------------------
    // Test 3: absolute path outside the allowlist (/etc/passwd)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ValidateSerilogPath_AbsolutePathOutsideAllowlist_AddsError()
    {
        var config = ConfigWithSinkPath("/etc/passwd");
        var errors = new List<string>();

        StartupValidation.ValidateSerilogPath(config, errors);

        errors.Should().ContainSingle("exactly one error for an off-allowlist absolute path");
        errors[0].Should().Contain("absolute path outside the allowed prefixes",
            "error message must name the rejection reason and allowlist");
    }

    // -----------------------------------------------------------------------
    // Test 4: allowlisted absolute path /var/log/frigaterelay/app.log
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ValidateSerilogPath_AllowlistedAbsolutePath_VarLog_NoError()
    {
        var config = ConfigWithSinkPath("/var/log/frigaterelay/app.log");
        var errors = new List<string>();

        StartupValidation.ValidateSerilogPath(config, errors);

        errors.Should().BeEmpty("paths under /var/log/frigaterelay/ are explicitly allowed");
    }

    // -----------------------------------------------------------------------
    // Test 5: allowlisted absolute path /app/logs/app.log (container case)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ValidateSerilogPath_AllowlistedAbsolutePath_AppLogs_NoError()
    {
        var config = ConfigWithSinkPath("/app/logs/app.log");
        var errors = new List<string>();

        StartupValidation.ValidateSerilogPath(config, errors);

        errors.Should().BeEmpty("paths under /app/logs/ are explicitly allowed (container case)");
    }

    // -----------------------------------------------------------------------
    // Test 6: relative safe path (current default)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ValidateSerilogPath_RelativeSafePath_NoError()
    {
        var config = ConfigWithSinkPath("logs/frigaterelay-.log");
        var errors = new List<string>();

        StartupValidation.ValidateSerilogPath(config, errors);

        errors.Should().BeEmpty("the current default relative path must be accepted without error");
    }

    // -----------------------------------------------------------------------
    // Test 7: no Serilog:WriteTo section configured
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ValidateSerilogPath_NoWriteToSection_NoError()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var errors = new List<string>();

        StartupValidation.ValidateSerilogPath(config, errors);

        errors.Should().BeEmpty("absent Serilog:WriteTo section must produce no errors");
    }

    // -----------------------------------------------------------------------
    // Test 8: sink with no Args:path value (empty/null)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ValidateSerilogPath_SinkWithNoPathArg_NoError()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Serilog:WriteTo:0:Name"] = "Console",
                // No Args:path key at all
            })
            .Build();
        var errors = new List<string>();

        StartupValidation.ValidateSerilogPath(config, errors);

        errors.Should().BeEmpty("a sink with no Args:path (e.g. Console sink) must not produce errors");
    }

    // -----------------------------------------------------------------------
    // Integration test: ValidateAll aggregates the path-traversal error
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ValidateAll_WithBadSerilogPath_ThrowsAggregatedError()
    {
        // Minimal ServiceProvider: IConfiguration with a bad path, plus the minimum
        // services ValidateAll needs for passes 1-4 (empty subscriptions so those passes
        // are no-ops, IActionPlugin + ISnapshotProvider enumerables present).
        var config = ConfigWithSinkPath("../../../etc/shadow");

        var actionPlugin = Substitute.For<IActionPlugin>();
        actionPlugin.Name.Returns("Dummy");

        var snapshotProvider = Substitute.For<ISnapshotProvider>();
        snapshotProvider.Name.Returns("Dummy");

        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(config)
            .AddSingleton<IActionPlugin>(actionPlugin)
            .AddSingleton<ISnapshotProvider>(snapshotProvider)
            .BuildServiceProvider();

        var options = new HostSubscriptionsOptions
        {
            Subscriptions = [],
        };

        var act = () => StartupValidation.ValidateAll(services, options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Startup configuration invalid*",
                "ValidateAll must use the standard aggregated header")
            .WithMessage("*path traversal*",
                "aggregated message must include the path-traversal rejection detail");
    }
}
