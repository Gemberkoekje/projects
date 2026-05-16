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

    [JsonPropertyName("modules")]
    public IReadOnlyList<ShipModule>? Modules { get; init; }

    [JsonPropertyName("frame")]
    public ShipComponent? Frame { get; init; }

    [JsonPropertyName("reactor")]
    public ShipComponent? Reactor { get; init; }

    [JsonPropertyName("engine")]
    public ShipComponent? Engine { get; init; }

    [JsonPropertyName("cooldown")]
    public ShipCooldown? Cooldown { get; init; }
}

public sealed class ShipRegistration
{
    [JsonPropertyName("role")]
    required public string Role { get; init; }
}

public sealed class ShipRequirements
{
    [JsonPropertyName("power")]
    public int? Power { get; init; }

    [JsonPropertyName("crew")]
    public int? Crew { get; init; }

    [JsonPropertyName("slots")]
    public int? Slots { get; init; }
}

public sealed class ShipMount
{
    [JsonPropertyName("symbol")]
    required public string Symbol { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("strength")]
    public int? Strength { get; init; }

    [JsonPropertyName("deposits")]
    public IReadOnlyList<string>? Deposits { get; init; }

    [JsonPropertyName("requirements")]
    public ShipRequirements? Requirements { get; init; }
}

public sealed class ShipModule
{
    [JsonPropertyName("symbol")]
    required public string Symbol { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("capacity")]
    public int? Capacity { get; init; }

    [JsonPropertyName("range")]
    public int? Range { get; init; }

    [JsonPropertyName("requirements")]
    public ShipRequirements? Requirements { get; init; }
}

public sealed class ShipComponent
{
    [JsonPropertyName("symbol")]
    required public string Symbol { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("condition")]
    public double Condition { get; init; }

    [JsonPropertyName("integrity")]
    public double Integrity { get; init; }

    // Frame-specific
    [JsonPropertyName("moduleSlots")]
    public int? ModuleSlots { get; init; }

    [JsonPropertyName("mountingPoints")]
    public int? MountingPoints { get; init; }

    [JsonPropertyName("fuelCapacity")]
    public int? FuelCapacity { get; init; }

    // Reactor-specific
    [JsonPropertyName("powerOutput")]
    public int? PowerOutput { get; init; }

    // Engine-specific
    [JsonPropertyName("speed")]
    public int? Speed { get; init; }

    [JsonPropertyName("requirements")]
    public ShipRequirements? Requirements { get; init; }
}

public sealed class ShipCooldown
{
    [JsonPropertyName("totalSeconds")]
    public int TotalSeconds { get; init; }

    [JsonPropertyName("remainingSeconds")]
    public int RemainingSeconds { get; init; }

    [JsonPropertyName("expiration")]
    public DateTimeOffset? Expiration { get; init; }
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
