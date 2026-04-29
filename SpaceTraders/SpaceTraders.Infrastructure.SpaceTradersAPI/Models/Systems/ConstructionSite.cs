using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Systems;

public sealed class ConstructionSite
{
    [JsonPropertyName("symbol")]
    required public string Symbol { get; init; }

    [JsonPropertyName("materials")]
    public IReadOnlyList<ConstructionMaterial>? Materials { get; init; }

    [JsonPropertyName("isComplete")]
    public bool IsComplete { get; init; }
}

public sealed class ConstructionMaterial
{
    [JsonPropertyName("tradeSymbol")]
    required public string TradeSymbol { get; init; }

    [JsonPropertyName("required")]
    public int Required { get; init; }

    [JsonPropertyName("fulfilled")]
    public int Fulfilled { get; init; }
}

public sealed class SupplyConstructionRequest
{
    [JsonPropertyName("shipSymbol")]
    required public string ShipSymbol { get; init; }

    [JsonPropertyName("tradeSymbol")]
    required public string TradeSymbol { get; init; }

    [JsonPropertyName("units")]
    public int Units { get; init; }
}

public sealed class SupplyConstructionData
{
    [JsonPropertyName("construction")]
    required public ConstructionSite Construction { get; init; }

    [JsonPropertyName("cargo")]
    required public Fleet.ShipCargo Cargo { get; init; }
}
