using System.Reflection;
using FluentAssertions;
using FrigateRelay.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Abstractions.Tests;

[TestClass]
public sealed class VerdictTests
{
    // (a) Pass() produces Passed=true, no Reason, no Score
    [TestMethod]
    public void Pass_NoScore_HasNoReasonAndNoScore()
    {
        var verdict = Verdict.Pass();

        verdict.Passed.Should().BeTrue();
        verdict.Reason.Should().BeNull();
        verdict.Score.Should().BeNull();
    }

    // (b) Pass(score) carries Score and no Reason
    [TestMethod]
    public void Pass_WithScore_CarriesScoreAndNoReason()
    {
        var verdict = Verdict.Pass(0.92);

        verdict.Passed.Should().BeTrue();
        verdict.Score.Should().Be(0.92);
        verdict.Reason.Should().BeNull();
    }

    // (c) Fail(reason) is failed and carries the reason
    [TestMethod]
    public void Fail_WithReason_IsFailedAndCarriesReason()
    {
        var verdict = Verdict.Fail("confidence below 0.75");

        verdict.Passed.Should().BeFalse();
        verdict.Reason.Should().Be("confidence below 0.75");
        verdict.Score.Should().BeNull();
    }

    // (d) Fail with null/empty/whitespace throws ArgumentException
    [TestMethod]
    [DataRow(null,  DisplayName = "null reason")]
    [DataRow("",    DisplayName = "empty reason")]
    [DataRow("   ", DisplayName = "whitespace reason")]
    public void Fail_WithNullOrWhitespaceReason_Throws(string? reason)
    {
        var act = () => Verdict.Fail(reason!);

        act.Should().Throw<ArgumentException>();
    }

    // (e) Verdict has no public constructors (invariant enforced via Reflection)
    [TestMethod]
    public void Verdict_Ctor_IsNotPublic()
    {
        var publicCtors = typeof(Verdict)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        publicCtors.Should().BeEmpty(
            "Verdict must only be creatable through its static factory methods");
    }
}
