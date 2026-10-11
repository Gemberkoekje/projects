namespace Curator.Core.Content;

/// <summary>Money numbers (BUILD_BRIEF §5.3).</summary>
public sealed record MoneyBalance
{
    public required int Start { get; init; }

    public required int UpkeepPerDay { get; init; }

    public required IReadOnlyDictionary<Rarity, int> FeeByRarity { get; init; }
}
