namespace SpaceTraders.Domain.Enums;

public enum LedgerCategory
{
    TradeBuy,
    TradeSell,
    MiningSell,
    ContractPayout,
    ContractDeposit,
    FuelPurchase,
    ShipPurchase,
    ModulePurchase,
    MountPurchase,
    Repair,
    Other,
    AntimatterPurchase,
    ConstructionBuy,

    /// <summary>The one-off reward for charting a waypoint (PLAN.md slice 6.30, D99).</summary>
    ChartReward,
}
