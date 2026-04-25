using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Contracts;

public sealed class AcceptContractResult
{
    [JsonPropertyName("agent")]
    public required Models.Agents.Agent Agent { get; init; }

    [JsonPropertyName("contract")]
    public required Contract Contract { get; init; }
}

public sealed class DeliverContractRequest
{
    [JsonPropertyName("shipSymbol")]
    public required string ShipSymbol { get; init; }

    [JsonPropertyName("tradeSymbol")]
    public required string TradeSymbol { get; init; }

    [JsonPropertyName("units")]
    public int Units { get; init; }
}

public sealed class DeliverContractResult
{
    [JsonPropertyName("contract")]
    public required Contract Contract { get; init; }

    [JsonPropertyName("cargo")]
    public required Fleet.ShipCargo Cargo { get; init; }
}

public sealed class FulfillContractResult
{
    [JsonPropertyName("agent")]
    public required Models.Agents.Agent Agent { get; init; }

    [JsonPropertyName("contract")]
    public required Contract Contract { get; init; }
}
