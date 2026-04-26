using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;

public sealed class ShipNavResult
{
    [JsonPropertyName("nav")]
    required public ShipNav Nav { get; init; }

    [JsonPropertyName("fuel")]
    public ShipFuel? Fuel { get; init; }
}

public sealed class NavigateResult
{
    [JsonPropertyName("nav")]
    required public ShipNav Nav { get; init; }

    [JsonPropertyName("fuel")]
    public ShipFuel? Fuel { get; init; }

    [JsonPropertyName("events")]
    public IReadOnlyList<object>? Events { get; init; }
}

public sealed class ShipCargo
{
    [JsonPropertyName("capacity")]
    public int Capacity { get; init; }

    [JsonPropertyName("units")]
    public int Units { get; init; }

    [JsonPropertyName("inventory")]
    public IReadOnlyList<CargoItem>? Inventory { get; init; }
}

public sealed class CargoItem
{
    [JsonPropertyName("symbol")]
    required public string Symbol { get; init; }

    [JsonPropertyName("units")]
    public int Units { get; init; }
}

public sealed class SellCargoResult
{
    [JsonPropertyName("agent")]
    required public Models.Agents.Agent Agent { get; init; }

    [JsonPropertyName("cargo")]
    required public ShipCargo Cargo { get; init; }

    [JsonPropertyName("transaction")]
    required public MarketTransaction Transaction { get; init; }
}

public sealed class MarketTransaction
{
    [JsonPropertyName("waypointSymbol")]
    required public string WaypointSymbol { get; init; }

    [JsonPropertyName("shipSymbol")]
    required public string ShipSymbol { get; init; }

    [JsonPropertyName("tradeSymbol")]
    required public string TradeSymbol { get; init; }

    [JsonPropertyName("type")]
    required public string Type { get; init; }

    [JsonPropertyName("units")]
    public int Units { get; init; }

    [JsonPropertyName("pricePerUnit")]
    public long PricePerUnit { get; init; }

    [JsonPropertyName("totalPrice")]
    public long TotalPrice { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }
}

public sealed class BuyCargoResult
{
    [JsonPropertyName("agent")]
    required public Models.Agents.Agent Agent { get; init; }

    [JsonPropertyName("cargo")]
    required public ShipCargo Cargo { get; init; }

    [JsonPropertyName("transaction")]
    required public MarketTransaction Transaction { get; init; }
}

public sealed class RefuelResult
{
    [JsonPropertyName("agent")]
    required public Models.Agents.Agent Agent { get; init; }

    [JsonPropertyName("fuel")]
    required public ShipFuel Fuel { get; init; }

    [JsonPropertyName("transaction")]
    required public MarketTransaction Transaction { get; init; }
}

public sealed class ExtractResult
{
    [JsonPropertyName("extraction")]
    required public Extraction Extraction { get; init; }

    [JsonPropertyName("cargo")]
    required public ShipCargo Cargo { get; init; }

    [JsonPropertyName("cooldown")]
    required public Cooldown Cooldown { get; init; }
}

public sealed class Extraction
{
    [JsonPropertyName("shipSymbol")]
    required public string ShipSymbol { get; init; }

    [JsonPropertyName("yield")]
    required public ExtractionYield Yield { get; init; }
}

public sealed class ExtractionYield
{
    [JsonPropertyName("symbol")]
    required public string Symbol { get; init; }

    [JsonPropertyName("units")]
    public int Units { get; init; }
}

public sealed class Cooldown
{
    [JsonPropertyName("shipSymbol")]
    required public string ShipSymbol { get; init; }

    [JsonPropertyName("totalSeconds")]
    public int TotalSeconds { get; init; }

    [JsonPropertyName("remainingSeconds")]
    public int RemainingSeconds { get; init; }

    [JsonPropertyName("expiration")]
    public DateTimeOffset? Expiration { get; init; }
}

public sealed class PurchaseShipResult
{
    [JsonPropertyName("agent")]
    required public Models.Agents.Agent Agent { get; init; }

    [JsonPropertyName("ship")]
    required public Ship Ship { get; init; }

    [JsonPropertyName("transaction")]
    required public ShipyardTransaction Transaction { get; init; }
}

public sealed class ShipyardTransaction
{
    [JsonPropertyName("waypointSymbol")]
    required public string WaypointSymbol { get; init; }

    [JsonPropertyName("shipSymbol")]
    required public string ShipSymbol { get; init; }

    [JsonPropertyName("shipType")]
    required public string ShipType { get; init; }

    [JsonPropertyName("price")]
    public long Price { get; init; }

    [JsonPropertyName("agentSymbol")]
    required public string AgentSymbol { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }
}
