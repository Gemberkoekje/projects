namespace SpaceTraders.Domain.Enums;

public enum ShipGoalKind
{
    None = 0,
    Idle = 1,
    MoveToWaypoint = 2,
    MineResource = 3,
    SiphonResource = 4,
    SellCargo = 5,
    DeliverCargo = 6,
    SupplyConstruction = 7,
    ScoutWaypoint = 8,
    PatrolMarket = 9,
    DeployProbe = 10,
    MineAndSell = 11,
    TradeBetweenMarkets = 12,
    SurveyWaypoint = 13,
    SiphonAndSell = 14,
    GatherAndSell = 15,
    Jump = 16,
    ExploreSystem = 17,

    /// <summary>A drone parked at a far asteroid, mining for the shuttle that collects there (slice 6.18, D83).</summary>
    MineForShuttle = 18,

    /// <summary>A shuttle collecting the parked drones' ore at a far asteroid and selling it (slice 6.18, D83).</summary>
    CollectOre = 19,

    /// <summary>An explorer warping to another system (slice 6.31, D100, D101).</summary>
    Warp = 20,
}
