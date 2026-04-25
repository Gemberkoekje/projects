using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;

public sealed class ShipNavResult
{
    [JsonPropertyName("nav")]
    public required ShipNav Nav { get; init; }

    [JsonPropertyName("fuel")]
    public ShipFuel? Fuel { get; init; }
}

public sealed class NavigateResult
{
    [JsonPropertyName("nav")]
    public required ShipNav Nav { get; init; }

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
    public required string Symbol { get; init; }

    [JsonPropertyName("units")]
    public int Units { get; init; }
}

public sealed class SellCargoResult
{
    [JsonPropertyName("agent")]
    public required Models.Agents.Agent Agent { get; init; }

    [JsonPropertyName("cargo")]
    public required ShipCargo Cargo { get; init; }

    [JsonPropertyName("transaction")]
    public required MarketTransaction Transaction { get; init; }
}

public sealed class MarketTransaction
{
    [JsonPropertyName("waypointSymbol")]
    public required string WaypointSymbol { get; init; }

    [JsonPropertyName("shipSymbol")]
    public required string ShipSymbol { get; init; }

    [JsonPropertyName("tradeSymbol")]
    public required string TradeSymbol { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

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
    public required Models.Agents.Agent Agent { get; init; }

    [JsonPropertyName("cargo")]
    public required ShipCargo Cargo { get; init; }

    [JsonPropertyName("transaction")]
    public required MarketTransaction Transaction { get; init; }
}

public sealed class RefuelResult
{
    [JsonPropertyName("agent")]
    public required Models.Agents.Agent Agent { get; init; }

    [JsonPropertyName("fuel")]
    public required ShipFuel Fuel { get; init; }

    [JsonPropertyName("transaction")]
    public required MarketTransaction Transaction { get; init; }
}

public sealed class ExtractResult
{
    [JsonPropertyName("extraction")]
    public required Extraction Extraction { get; init; }

    [JsonPropertyName("cargo")]
    public required ShipCargo Cargo { get; init; }

    [JsonPropertyName("cooldown")]
    public required Cooldown Cooldown { get; init; }
}

public sealed class Extraction
{
    [JsonPropertyName("shipSymbol")]
    public required string ShipSymbol { get; init; }

    [JsonPropertyName("yield")]
    public required ExtractionYield Yield { get; init; }
}

public sealed class ExtractionYield
{
    [JsonPropertyName("symbol")]
    public required string Symbol { get; init; }

    [JsonPropertyName("units")]
    public int Units { get; init; }
}

public sealed class Cooldown
{
    [JsonPropertyName("shipSymbol")]
    public required string ShipSymbol { get; init; }

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
    public required Models.Agents.Agent Agent { get; init; }

    [JsonPropertyName("ship")]
    public required Ship Ship { get; init; }

    [JsonPropertyName("transaction")]
    public required ShipyardTransaction Transaction { get; init; }
}

public sealed class ShipyardTransaction
{
    [JsonPropertyName("waypointSymbol")]
    public required string WaypointSymbol { get; init; }

    [JsonPropertyName("shipSymbol")]
    public required string ShipSymbol { get; init; }

    [JsonPropertyName("shipType")]
    public required string ShipType { get; init; }

    [JsonPropertyName("price")]
    public long Price { get; init; }

    [JsonPropertyName("agentSymbol")]
    public required string AgentSymbol { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }
}
