using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;

public sealed class RepairQuoteResult
{
    [JsonPropertyName("transaction")]
    required public ShipModificationTransaction Transaction { get; init; }
}

public sealed class RepairResult
{
    [JsonPropertyName("agent")]
    required public Models.Agents.Agent Agent { get; init; }

    [JsonPropertyName("ship")]
    required public Ship Ship { get; init; }

    [JsonPropertyName("transaction")]
    required public ShipModificationTransaction Transaction { get; init; }
}

public sealed class ScrapQuoteResult
{
    [JsonPropertyName("transaction")]
    required public ShipModificationTransaction Transaction { get; init; }
}

public sealed class ScrapResult
{
    [JsonPropertyName("agent")]
    required public Models.Agents.Agent Agent { get; init; }

    [JsonPropertyName("transaction")]
    required public ShipModificationTransaction Transaction { get; init; }
}

public sealed class InstallMountResult
{
    [JsonPropertyName("agent")]
    required public Models.Agents.Agent Agent { get; init; }

    [JsonPropertyName("cargo")]
    required public ShipCargo Cargo { get; init; }

    [JsonPropertyName("transaction")]
    required public ShipModificationTransaction Transaction { get; init; }

    [JsonPropertyName("mounts")]
    required public IReadOnlyList<ShipMount> Mounts { get; init; }
}

public sealed class RemoveMountResult
{
    [JsonPropertyName("agent")]
    required public Models.Agents.Agent Agent { get; init; }

    [JsonPropertyName("cargo")]
    required public ShipCargo Cargo { get; init; }

    [JsonPropertyName("transaction")]
    required public ShipModificationTransaction Transaction { get; init; }

    [JsonPropertyName("mounts")]
    required public IReadOnlyList<ShipMount> Mounts { get; init; }
}

public sealed class InstallModuleResult
{
    [JsonPropertyName("agent")]
    required public Models.Agents.Agent Agent { get; init; }

    [JsonPropertyName("cargo")]
    required public ShipCargo Cargo { get; init; }

    [JsonPropertyName("transaction")]
    required public ShipModificationTransaction Transaction { get; init; }

    [JsonPropertyName("modules")]
    required public IReadOnlyList<ShipModule> Modules { get; init; }
}

public sealed class RemoveModuleResult
{
    [JsonPropertyName("agent")]
    required public Models.Agents.Agent Agent { get; init; }

    [JsonPropertyName("cargo")]
    required public ShipCargo Cargo { get; init; }

    [JsonPropertyName("transaction")]
    required public ShipModificationTransaction Transaction { get; init; }

    [JsonPropertyName("modules")]
    required public IReadOnlyList<ShipModule> Modules { get; init; }
}

public sealed class ShipModificationTransaction
{
    [JsonPropertyName("waypointSymbol")]
    public string? WaypointSymbol { get; init; }

    [JsonPropertyName("shipSymbol")]
    public string? ShipSymbol { get; init; }

    [JsonPropertyName("tradeSymbol")]
    public string? TradeSymbol { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("units")]
    public int Units { get; init; }

    [JsonPropertyName("pricePerUnit")]
    public long PricePerUnit { get; init; }

    [JsonPropertyName("totalPrice")]
    public long TotalPrice { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; init; }
}
