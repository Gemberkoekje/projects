namespace Curator.Core.Content;

/// <summary>What the patron says and feels at each decision.</summary>
public sealed record VisitDecisions
{
    /// <summary>Nothing scripted: generic lines and balance trust deltas apply.</summary>
    public static readonly VisitDecisions Empty = new();

    public DecisionLine Lent { get; init; } = DecisionLine.Empty;

    public DecisionLine Alternative { get; init; } = DecisionLine.Empty;

    public DecisionLine OfferRefused { get; init; } = DecisionLine.Empty;

    public DecisionLine Declined { get; init; } = DecisionLine.Empty;

    public DecisionLine WalkedOut { get; init; } = DecisionLine.Empty;
}
