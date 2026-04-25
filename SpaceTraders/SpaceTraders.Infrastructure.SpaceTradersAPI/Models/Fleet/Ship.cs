using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;

public sealed class Ship
{
    [JsonPropertyName("symbol")]
    public required string Symbol { get; init; }

    [JsonPropertyName("nav")]
    public ShipNav? Nav { get; init; }

    [JsonPropertyName("crew")]
    public ShipCrew? Crew { get; init; }

    [JsonPropertyName("fuel")]
    public ShipFuel? Fuel { get; init; }
}

public sealed class ShipNav
{
    [JsonPropertyName("systemSymbol")]
    public required string SystemSymbol { get; init; }

    [JsonPropertyName("waypointSymbol")]
    public required string WaypointSymbol { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("flightMode")]
    public required string FlightMode { get; init; }

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
    public required string Symbol { get; init; }

    [JsonPropertyName("systemSymbol")]
    public required string SystemSymbol { get; init; }
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
