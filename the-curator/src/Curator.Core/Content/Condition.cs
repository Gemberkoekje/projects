namespace Curator.Core.Content;

/// <summary>A visit's or greeting's <c>when</c> (BUILD_BRIEF §5.12): every field present must hold.</summary>
public sealed record Condition
{
    /// <summary>The empty condition, which always holds.</summary>
    public static readonly Condition Always = new();

    /// <remarks>Nullable because a minimum of 0 still differs from no minimum when validating.</remarks>
    public int? MinTrust { get; init; }

    /// <remarks>Nullable because a maximum of 0 is a real condition.</remarks>
    public int? MaxTrust { get; init; }

    public IReadOnlyList<string> FlagsAll { get; init; } = [];

    public IReadOnlyList<string> FlagsNone { get; init; } = [];

    /// <summary>This patron's last decision must be one of these.</summary>
    public IReadOnlyList<DecisionKind> PreviousDecisionIn { get; init; } = [];

    /// <summary>The outcome of this patron's last lend must be one of these.</summary>
    public IReadOnlyList<OutcomeCategory> PreviousOutcomeIn { get; init; } = [];

    /// <summary>This patron must have borrowed this book (card history included), or empty.</summary>
    public string HasBorrowed { get; init; } = "";

    /// <summary>Whether no field is set.</summary>
    public bool IsAlways =>
        MinTrust is null && MaxTrust is null && FlagsAll.Count == 0 && FlagsNone.Count == 0 &&
        PreviousDecisionIn.Count == 0 && PreviousOutcomeIn.Count == 0 && HasBorrowed.Length == 0;
}
