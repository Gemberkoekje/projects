namespace SpaceTraders.Application.Planning;

/// <summary>
/// Phase 3: ship planner boundary.
/// The kind of command a ship planner asks the executor to issue. Each planner decision
/// emits at most one ship command, satisfying the "one decision -> one command" principle.
/// </summary>
public enum ShipPlannerCommandKind
{
    /// <summary>No command should be issued (e.g. ship is in transit, no assignment, etc.).</summary>
    None,

    /// <summary>Dock the ship at its current waypoint. Requires <see cref="Domain.Enums.ShipLocalStatus.InOrbit"/>.</summary>
    Dock,

    /// <summary>Orbit the ship from a docked state. Requires <see cref="Domain.Enums.ShipLocalStatus.Docked"/>.</summary>
    Orbit,

    /// <summary>Navigate to <see cref="ShipPlannerDecision.DestinationWaypoint"/>. Requires in-orbit.</summary>
    Navigate,

    /// <summary>Extract resources at the current waypoint.</summary>
    Extract,

    /// <summary>Survey at the current waypoint before extracting.</summary>
    Survey,

    /// <summary>Siphon gas at the current waypoint.</summary>
    Siphon,

    /// <summary>Reassign the ship to "Idle" because the current assignment cannot be progressed.</summary>
    AssignIdle,

    /// <summary>Patch the ship nav flight mode to <see cref="ShipPlannerDecision.FlightMode"/> before navigating.</summary>
    PatchFlightMode,
}

/// <summary>
/// Phase 3: ship planner boundary.
/// Represents the single command a ship planner has chosen for a ship at a decision point.
/// </summary>
public sealed record ShipPlannerDecision
{
    public required string ShipSymbol { get; init; }

    public required ShipPlannerCommandKind Kind { get; init; }

    public string DestinationWaypoint { get; init; } = string.Empty;

    public string FlightMode { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public static ShipPlannerDecision None(string shipSymbol, string reason) => new()
    {
        ShipSymbol = shipSymbol,
        Kind = ShipPlannerCommandKind.None,
        Reason = reason,
    };

    public static ShipPlannerDecision Dock(string shipSymbol, string reason) => new()
    {
        ShipSymbol = shipSymbol,
        Kind = ShipPlannerCommandKind.Dock,
        Reason = reason,
    };

    public static ShipPlannerDecision Orbit(string shipSymbol, string reason) => new()
    {
        ShipSymbol = shipSymbol,
        Kind = ShipPlannerCommandKind.Orbit,
        Reason = reason,
    };

    public static ShipPlannerDecision Navigate(string shipSymbol, string destinationWaypoint, string reason) => new()
    {
        ShipSymbol = shipSymbol,
        Kind = ShipPlannerCommandKind.Navigate,
        DestinationWaypoint = destinationWaypoint,
        Reason = reason,
    };

    public static ShipPlannerDecision Extract(string shipSymbol, string reason) => new()
    {
        ShipSymbol = shipSymbol,
        Kind = ShipPlannerCommandKind.Extract,
        Reason = reason,
    };

    public static ShipPlannerDecision Survey(string shipSymbol, string reason) => new()
    {
        ShipSymbol = shipSymbol,
        Kind = ShipPlannerCommandKind.Survey,
        Reason = reason,
    };

    public static ShipPlannerDecision Siphon(string shipSymbol, string reason) => new()
    {
        ShipSymbol = shipSymbol,
        Kind = ShipPlannerCommandKind.Siphon,
        Reason = reason,
    };

    public static ShipPlannerDecision AssignIdle(string shipSymbol, string reason) => new()
    {
        ShipSymbol = shipSymbol,
        Kind = ShipPlannerCommandKind.AssignIdle,
        Reason = reason,
    };

    public static ShipPlannerDecision PatchFlightMode(string shipSymbol, string flightMode, string reason) => new()
    {
        ShipSymbol = shipSymbol,
        Kind = ShipPlannerCommandKind.PatchFlightMode,
        FlightMode = flightMode,
        Reason = reason,
    };
}
