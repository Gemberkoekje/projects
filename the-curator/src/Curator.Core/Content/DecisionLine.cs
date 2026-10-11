namespace Curator.Core.Content;

/// <summary>A patron's line at a decision, with an optional trust change and flags.</summary>
public sealed record DecisionLine
{
    /// <summary>No line, default trust, no flags.</summary>
    public static readonly DecisionLine Empty = new();

    /// <summary>The line, or empty to fall back to questions.json's generic line.</summary>
    public string Line { get; init; } = "";

    /// <summary>Trust change; the balance default for this decision when absent.</summary>
    /// <remarks>Nullable because "absent" (use the balance) differs from an explicit 0.</remarks>
    public int? Trust { get; init; }

    public IReadOnlyList<string> SetFlags { get; init; } = [];
}
