namespace Curator.Core.Content;

/// <summary>Mana numbers (BUILD_BRIEF §5.2).</summary>
public sealed record ManaBalance
{
    public required int BasePerDay { get; init; }

    public required int ReadThoughtsCost { get; init; }

    public required int IdentifyCost { get; init; }

    public required int RolloverCap { get; init; }

    public required int AttentiveBonusCap { get; init; }

    public required int GoodOutcomeBonus { get; init; }

    public required int TotalBonusCap { get; init; }
}
