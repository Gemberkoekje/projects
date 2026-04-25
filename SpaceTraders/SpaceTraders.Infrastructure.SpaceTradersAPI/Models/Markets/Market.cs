using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Markets;

public sealed class Market
{
    [JsonPropertyName("symbol")]
    public required string Symbol { get; init; }

    [JsonPropertyName("exports")]
    public IReadOnlyList<TradeGoodSymbol>? Exports { get; init; }

    [JsonPropertyName("imports")]
    public IReadOnlyList<TradeGoodSymbol>? Imports { get; init; }

    [JsonPropertyName("exchange")]
    public IReadOnlyList<TradeGoodSymbol>? Exchange { get; init; }

    [JsonPropertyName("tradeGoods")]
    public IReadOnlyList<TradeGood>? TradeGoods { get; init; }
}

public sealed class TradeGoodSymbol
{
    [JsonPropertyName("symbol")]
    public required string Symbol { get; init; }
}

public sealed class TradeGood
{
    [JsonPropertyName("symbol")]
    public required string Symbol { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("tradeVolume")]
    public int TradeVolume { get; init; }

    [JsonPropertyName("supply")]
    public required string Supply { get; init; }

    [JsonPropertyName("activity")]
    public string? Activity { get; init; }

    [JsonPropertyName("purchasePrice")]
    public long PurchasePrice { get; init; }

    [JsonPropertyName("sellPrice")]
    public long SellPrice { get; init; }
}
