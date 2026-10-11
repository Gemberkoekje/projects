namespace Curator.Core.Content;

/// <summary>What came of a visit (BUILD_BRIEF §5.11). Declines and walk-outs have categories of their own.</summary>
public enum OutcomeCategory
{
    None = 0,
    Good,
    Unhelpful,
    Harm,
    Mixed,
    Declined,
    WalkedOut,
}
