using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Contracts;

public sealed class AcceptContractResult
{
    [JsonPropertyName("agent")]
    required public Models.Agents.Agent Agent { get; init; }

    [JsonPropertyName("contract")]
    required public Contract Contract { get; init; }
}

public sealed class DeliverContractRequest
{
    [JsonPropertyName("shipSymbol")]
    required public string ShipSymbol { get; init; }

    [JsonPropertyName("tradeSymbol")]
    required public string TradeSymbol { get; init; }

    [JsonPropertyName("units")]
    public int Units { get; init; }
}

public sealed class DeliverContractResult
{
    [JsonPropertyName("contract")]
    required public Contract Contract { get; init; }

    [JsonPropertyName("cargo")]
    required public Fleet.ShipCargo Cargo { get; init; }
}

public sealed class FulfillContractResult
{
    [JsonPropertyName("agent")]
    required public Models.Agents.Agent Agent { get; init; }

    [JsonPropertyName("contract")]
    required public Contract Contract { get; init; }
}
