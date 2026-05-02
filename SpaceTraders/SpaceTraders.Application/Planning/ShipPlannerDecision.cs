namespace SpaceTraders.Application.Planning;

/// <summary>
/// Phase 3: ship planner boundary.
/// The kind of command a ship planner asks the executor to issue. Each planner decision
/// emits at most one ship command, satisfying the "one decision -> one command" principle.
/// </summary>
[Obsolete("Phase 10: use GoalExecutionOutcome instead. Will be removed in Phase 12.")]
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

    /// <summary>Phase 4b: supply construction materials at the current waypoint.</summary>
    SupplyConstruction,

    /// <summary>Phase 4b: refuel the docked ship from the local fuel market.</summary>
    Refuel,

    /// <summary>Phase 4b: buy cargo at the docked waypoint (used by the builder planner to load materials).</summary>
    BuyCargo,

    /// <summary>Phase 4b: repair the docked ship at the local shipyard.</summary>
    Repair,

    /// <summary>Phase 4b: scrap the docked ship.</summary>
    Scrap,

    /// <summary>Phase 6.5b: sell a specific cargo good at the docked market.</summary>
    SellCargo,

    /// <summary>Phase 6.5b: deliver cargo for a contract at the current docked waypoint.</summary>
    DeliverContractCargo,

    /// <summary>Phase 6.5b: refuel the docked ship using cargo hydrocarbons (fromCargo = true).</summary>
    RefuelFromCargo,

    /// <summary>Phase 6.5b: jettison a specific cargo good.</summary>
    JettisonCargo,

    /// <summary>Phase 6.5b: mark the current waypoint as visited and refresh market/shipyard data (used by the scouting planner).</summary>
    RefreshScoutData,
}

/// <summary>
/// Phase 3: ship planner boundary.
/// Represents the single command a ship planner has chosen for a ship at a decision point.
/// </summary>
[Obsolete("Phase 10: use GoalExecutionResult instead. Will be removed in Phase 12.")]
public sealed record ShipPlannerDecision
{
    public required string ShipSymbol { get; init; }

    public required ShipPlannerCommandKind Kind { get; init; }

    public string DestinationWaypoint { get; init; } = string.Empty;

    public string FlightMode { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    /// <summary>Phase 4b: trade good symbol for <see cref="ShipPlannerCommandKind.SupplyConstruction"/> and <see cref="ShipPlannerCommandKind.BuyCargo"/>.</summary>
    public string TradeSymbol { get; init; } = string.Empty;

    /// <summary>Phase 4b: unit count for cargo / supply commands.</summary>
    public int Units { get; init; }

    /// <summary>Phase 4b: target system symbol (used by <see cref="ShipPlannerCommandKind.SupplyConstruction"/>).</summary>
    public string SystemSymbol { get; init; } = string.Empty;

    /// <summary>Phase 4b: target waypoint symbol (used by <see cref="ShipPlannerCommandKind.SupplyConstruction"/> and <see cref="ShipPlannerCommandKind.DeliverContractCargo"/>).</summary>
    public string WaypointSymbol { get; init; } = string.Empty;

    /// <summary>Phase 6.5b: contract identifier (used by <see cref="ShipPlannerCommandKind.DeliverContractCargo"/>).</summary>
    public string ContractId { get; init; } = string.Empty;

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

    public static ShipPlannerDecision SupplyConstruction(
        string shipSymbol,
        string systemSymbol,
        string waypointSymbol,
        string tradeSymbol,
        int units,
        string reason) => new()
    {
        ShipSymbol = shipSymbol,
        Kind = ShipPlannerCommandKind.SupplyConstruction,
        SystemSymbol = systemSymbol,
        WaypointSymbol = waypointSymbol,
        TradeSymbol = tradeSymbol,
        Units = units,
        Reason = reason,
    };

    public static ShipPlannerDecision Refuel(string shipSymbol, string reason) => new()
    {
        ShipSymbol = shipSymbol,
        Kind = ShipPlannerCommandKind.Refuel,
        Reason = reason,
    };

    public static ShipPlannerDecision BuyCargo(string shipSymbol, string tradeSymbol, int units, string reason) => new()
    {
        ShipSymbol = shipSymbol,
        Kind = ShipPlannerCommandKind.BuyCargo,
        TradeSymbol = tradeSymbol,
        Units = units,
        Reason = reason,
    };

    public static ShipPlannerDecision Repair(string shipSymbol, string reason) => new()
    {
        ShipSymbol = shipSymbol,
        Kind = ShipPlannerCommandKind.Repair,
        Reason = reason,
    };

    public static ShipPlannerDecision Scrap(string shipSymbol, string reason) => new()
    {
        ShipSymbol = shipSymbol,
        Kind = ShipPlannerCommandKind.Scrap,
        Reason = reason,
    };

    public static ShipPlannerDecision SellCargo(string shipSymbol, string tradeSymbol, int units, string reason) => new()
    {
        ShipSymbol = shipSymbol,
        Kind = ShipPlannerCommandKind.SellCargo,
        TradeSymbol = tradeSymbol,
        Units = units,
        Reason = reason,
    };

    public static ShipPlannerDecision DeliverContractCargo(
        string shipSymbol,
        string contractId,
        string tradeSymbol,
        int units,
        string destinationWaypoint,
        string reason) => new()
    {
        ShipSymbol = shipSymbol,
        Kind = ShipPlannerCommandKind.DeliverContractCargo,
        ContractId = contractId,
        TradeSymbol = tradeSymbol,
        Units = units,
        WaypointSymbol = destinationWaypoint,
        Reason = reason,
    };

    public static ShipPlannerDecision RefuelFromCargo(string shipSymbol, string reason) => new()
    {
        ShipSymbol = shipSymbol,
        Kind = ShipPlannerCommandKind.RefuelFromCargo,
        Reason = reason,
    };

    public static ShipPlannerDecision JettisonCargo(string shipSymbol, string tradeSymbol, int units, string reason) => new()
    {
        ShipSymbol = shipSymbol,
        Kind = ShipPlannerCommandKind.JettisonCargo,
        TradeSymbol = tradeSymbol,
        Units = units,
        Reason = reason,
    };

    public static ShipPlannerDecision RefreshScoutData(string shipSymbol, string reason) => new()
    {
        ShipSymbol = shipSymbol,
        Kind = ShipPlannerCommandKind.RefreshScoutData,
        Reason = reason,
    };
}
