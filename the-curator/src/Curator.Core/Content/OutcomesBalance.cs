namespace Curator.Core.Content;

/// <summary>Outcome numbers (BUILD_BRIEF §5.11).</summary>
public sealed record OutcomesBalance
{
    public required IReadOnlyDictionary<OutcomeCategory, int> DefaultDelayDays { get; init; }
}
