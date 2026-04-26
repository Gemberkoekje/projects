using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;

public sealed class Ship
{
    [JsonPropertyName("symbol")]
    required public string Symbol { get; init; }

    [JsonPropertyName("registration")]
    public ShipRegistration? Registration { get; init; }

    [JsonPropertyName("nav")]
    public ShipNav? Nav { get; init; }

    [JsonPropertyName("crew")]
    public ShipCrew? Crew { get; init; }

    [JsonPropertyName("fuel")]
    public ShipFuel? Fuel { get; init; }

    [JsonPropertyName("cargo")]
    public FleetShipCargo? Cargo { get; init; }

    [JsonPropertyName("mounts")]
    public IReadOnlyList<ShipMount>? Mounts { get; init; }
}

public sealed class ShipRegistration
{
    [JsonPropertyName("role")]
    required public string Role { get; init; }
}

public sealed class ShipMount
{
    [JsonPropertyName("symbol")]
    required public string Symbol { get; init; }
}

public sealed class ShipNav
{
    [JsonPropertyName("systemSymbol")]
    required public string SystemSymbol { get; init; }

    [JsonPropertyName("waypointSymbol")]
    required public string WaypointSymbol { get; init; }

    [JsonPropertyName("status")]
    required public string Status { get; init; }

    [JsonPropertyName("flightMode")]
    required public string FlightMode { get; init; }

    [JsonPropertyName("route")]
    public ShipNavRoute? Route { get; init; }
}

public sealed class ShipNavRoute
{
    [JsonPropertyName("arrival")]
    public DateTimeOffset Arrival { get; init; }

    [JsonPropertyName("departureTime")]
    public DateTimeOffset DepartureTime { get; init; }

    [JsonPropertyName("origin")]
    public ShipNavRouteWaypoint? Origin { get; init; }

    [JsonPropertyName("destination")]
    public ShipNavRouteWaypoint? Destination { get; init; }
}

public sealed class ShipNavRouteWaypoint
{
    [JsonPropertyName("symbol")]
    required public string Symbol { get; init; }

    [JsonPropertyName("systemSymbol")]
    required public string SystemSymbol { get; init; }
}

public sealed class ShipCrew
{
    [JsonPropertyName("current")]
    public int Current { get; init; }

    [JsonPropertyName("capacity")]
    public int Capacity { get; init; }
}

public sealed class ShipFuel
{
    [JsonPropertyName("current")]
    public int Current { get; init; }

    [JsonPropertyName("capacity")]
    public int Capacity { get; init; }
}

public sealed class FleetShipCargo
{
    [JsonPropertyName("units")]
    public int Units { get; init; }

    [JsonPropertyName("capacity")]
    public int Capacity { get; init; }

    [JsonPropertyName("inventory")]
    public IReadOnlyList<CargoItem>? Inventory { get; init; }
}
