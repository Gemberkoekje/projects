using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.Application.Goals;

/// <summary>
/// Read-only contextual data populated by <see cref="ShipGoalExecutorService"/> and consumed by
/// goal executors. Analogous to <c>ShipPlannerContext</c> but shaped for goal-based execution.
/// </summary>
/// <remarks>Phase 10: introduced as part of the goal-driven architecture migration.</remarks>
public sealed record ShipGoalContext
{
    /// <summary>Number of active surveys at the ship's current waypoint.</summary>
    public int ActiveSurveyCount { get; init; }

    /// <summary>Symbol of the nearest market waypoint in the ship's system that sells FUEL. Empty when none is cached.</summary>
    public string FuelMarketWaypoint { get; init; } = string.Empty;

    /// <summary>True when the ship is docked at a waypoint whose cached market data includes FUEL.</summary>
    public bool CurrentWaypointSellsFuel { get; init; }

    /// <summary>Recommended flight mode for the primary navigation target of the current goal. Empty when not applicable.</summary>
    public string RecommendedFlightMode { get; init; } = string.Empty;

    /// <summary>Active accepted, unfulfilled contracts (populated only when ship is docked).</summary>
    public IReadOnlyList<ContractDto> ActiveContracts { get; init; } = [];

    /// <summary>Market snapshot at the ship's current docked waypoint (null when not docked or no data cached).</summary>
    public MarketSnapshot? CurrentMarketSnapshot { get; init; }

    /// <summary>Setting: minimum HYDROCARBON units to keep in hold (not sold or jettisoned).</summary>
    public int MiningReserveHydrocarbonUnits { get; init; }

    /// <summary>Setting: minimum sell price required to bother selling a mining resource.</summary>
    public int MiningMinimumSellPrice { get; init; }

    /// <summary>Setting: when <c>true</c>, jettison low-value cargo when cargo hold is full.</summary>
    public bool MiningJettisonLowValueWhenFull { get; init; }

    /// <summary>
    /// The nearest market waypoint that buys the goal's primary trade symbol, pre-resolved for MineResource
    /// and SiphonResource goals by <see cref="ShipGoalExecutorService"/>. Null for other goal types.
    /// </summary>
    public string? NearestSellMarket { get; init; }

    /// <summary>True when the SupplyConstruction target site is already complete.</summary>
    public bool ConstructionComplete { get; init; }
}
