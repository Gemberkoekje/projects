using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Shipyards;

public sealed class Shipyard
{
    [JsonPropertyName("symbol")]
    required public string Symbol { get; init; }

    [JsonPropertyName("shipTypes")]
    public IReadOnlyList<ShipyardShipType>? ShipTypes { get; init; }

    [JsonPropertyName("ships")]
    public IReadOnlyList<ShipyardShip>? Ships { get; init; }
}

public sealed class ShipyardShipType
{
    [JsonPropertyName("type")]
    required public string Type { get; init; }
}

public sealed class ShipyardShip
{
    [JsonPropertyName("type")]
    required public string Type { get; init; }

    [JsonPropertyName("purchasePrice")]
    public long PurchasePrice { get; init; }
}
