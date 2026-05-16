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
}
