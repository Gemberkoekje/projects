using System.Text.Json.Serialization;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;

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

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("supply")]
    public string? Supply { get; init; }

    [JsonPropertyName("activity")]
    public string? Activity { get; init; }

    [JsonPropertyName("purchasePrice")]
    public long PurchasePrice { get; init; }

    [JsonPropertyName("frame")]
    public ShipComponent? Frame { get; init; }

    [JsonPropertyName("reactor")]
    public ShipComponent? Reactor { get; init; }

    [JsonPropertyName("engine")]
    public ShipComponent? Engine { get; init; }

    [JsonPropertyName("modules")]
    public IReadOnlyList<ShipModule>? Modules { get; init; }

    [JsonPropertyName("mounts")]
    public IReadOnlyList<ShipMount>? Mounts { get; init; }

    [JsonPropertyName("crew")]
    public ShipCrew? Crew { get; init; }
}
