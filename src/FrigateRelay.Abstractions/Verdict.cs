namespace FrigateRelay.Abstractions;

/// <summary>
/// Represents the outcome of a validation plugin evaluation: either a pass (with optional confidence score) or a fail (with a non-empty reason).
/// Invalid states — a passing verdict with a reason, or a failing verdict without one — are unrepresentable because the constructor is private
/// and only the three static factories can create instances.
/// </summary>
public readonly record struct Verdict
{
    /// <summary>Gets a value indicating whether the validation passed.</summary>
    public bool Passed { get; }

    /// <summary>Gets the reason for failure, or <see langword="null"/> if the verdict is a pass.</summary>
    public string? Reason { get; }

    /// <summary>Gets the optional confidence score supplied by a passing verdict, or <see langword="null"/> if no score was provided.</summary>
    public double? Score { get; }

    private Verdict(bool passed, string? reason, double? score)
    {
        Passed = passed;
        Reason = reason;
        Score = score;
    }

    /// <summary>Creates a passing <see cref="Verdict"/> with no score.</summary>
    /// <returns>A <see cref="Verdict"/> where <see cref="Passed"/> is <see langword="true"/> and both <see cref="Reason"/> and <see cref="Score"/> are <see langword="null"/>.</returns>
    public static Verdict Pass() => new(true, null, null);

    /// <summary>Creates a passing <see cref="Verdict"/> carrying a confidence score.</summary>
    /// <param name="score">The confidence score produced by the validator (e.g. 0.0–1.0, though the range is not enforced here).</param>
    /// <returns>A <see cref="Verdict"/> where <see cref="Passed"/> is <see langword="true"/>, <see cref="Reason"/> is <see langword="null"/>, and <see cref="Score"/> equals <paramref name="score"/>.</returns>
    public static Verdict Pass(double score) => new(true, null, score);

    /// <summary>Creates a failing <see cref="Verdict"/> carrying a non-empty reason.</summary>
    /// <param name="reason">A human-readable explanation of why validation failed. Must not be <see langword="null"/>, empty, or whitespace.</param>
    /// <returns>A <see cref="Verdict"/> where <see cref="Passed"/> is <see langword="false"/> and <see cref="Reason"/> equals <paramref name="reason"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason"/> is <see langword="null"/>, empty, or whitespace.</exception>
    public static Verdict Fail(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A failing verdict must carry a non-empty reason.", nameof(reason));

        return new(false, reason, null);
    }
}
