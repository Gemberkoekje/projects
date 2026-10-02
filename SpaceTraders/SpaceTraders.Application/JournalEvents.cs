namespace SpaceTraders.Application;

/// <summary>
/// The journal: one log line per meaningful thing, each with an <c>EventKind</c> property, so that
/// <c>{namespace="spacetraders"} | json | EventKind != ""</c> in Loki reads as a timeline of the run.
/// Every journal line starts with its kind (<c>"{EventKind:l}: ..."</c>; <c>:l</c> keeps Serilog from
/// quoting it in the rendered message) and carries the standard
/// properties of what it is about: <c>ShipSymbol</c>, <c>ContractId</c>, <c>WaypointSymbol</c>,
/// <c>TradeSymbol</c>, <c>Plan</c>, and a <c>Reason</c> for anything blocked or idle.
/// </summary>
/// <remarks>Most are Information; a kind that needs attention logs at Warning or above.</remarks>
public static class JournalEvents
{
    /// <summary>A contract was accepted (<c>ContractId</c>, <c>Payment</c>).</summary>
    public const string ContractAccepted = nameof(ContractAccepted);

    /// <summary>Goods were delivered to a contract (<c>ContractId</c>, <c>ShipSymbol</c>, <c>TradeSymbol</c>, <c>Units</c>).</summary>
    public const string ContractDelivered = nameof(ContractDelivered);

    /// <summary>A contract was fulfilled (<c>ContractId</c>, <c>Payment</c>).</summary>
    public const string ContractFulfilled = nameof(ContractFulfilled);

    /// <summary>A ship was bought (<c>ShipSymbol</c>, <c>ShipType</c>, <c>WaypointSymbol</c>, <c>Cost</c>).</summary>
    public const string ShipPurchased = nameof(ShipPurchased);

    /// <summary>Cargo was bought (<c>ShipSymbol</c>, <c>TradeSymbol</c>, <c>Units</c>, <c>WaypointSymbol</c>, <c>Cost</c>).</summary>
    public const string CargoBought = nameof(CargoBought);

    /// <summary>Cargo was sold (<c>ShipSymbol</c>, <c>TradeSymbol</c>, <c>Units</c>, <c>WaypointSymbol</c>, <c>Revenue</c>).</summary>
    public const string CargoSold = nameof(CargoSold);

    /// <summary>
    /// A ship took a trade trip (<c>ShipSymbol</c>, <c>TradeSymbol</c>, <c>Units</c>, <c>BuyWaypoint</c>,
    /// <c>SellWaypoint</c>, <c>SellPrice</c>, <c>FuelCost</c> for the whole trip, <c>ExpectedProfit</c>;
    /// <c>BuyPrice</c> for a purchase, and <c>FeedsTradeSymbol</c> when the sell market makes a pricier
    /// good from it).
    /// </summary>
    public const string TradeStarted = nameof(TradeStarted);

    /// <summary>
    /// A trade trip moved its sale to another market, because selling where it was no longer paid
    /// (<c>ShipSymbol</c>, <c>TradeSymbol</c>, <c>WaypointSymbol</c>, <c>SellWaypoint</c>, <c>Reason</c>).
    /// </summary>
    public const string TradeRerouted = nameof(TradeRerouted);

    /// <summary>
    /// A trade trip was given up before its purchase, because the newest prices made it no longer
    /// lucrative (<c>ShipSymbol</c>, <c>TradeSymbol</c>, <c>WaypointSymbol</c>, <c>Reason</c>).
    /// </summary>
    public const string TradeDropped = nameof(TradeDropped);

    /// <summary>A plan started (<c>Plan</c>).</summary>
    public const string PlanStarted = nameof(PlanStarted);

    /// <summary>A plan completed (<c>Plan</c>).</summary>
    public const string PlanCompleted = nameof(PlanCompleted);

    /// <summary>A plan can't go on for now (<c>Plan</c>, <c>Reason</c>).</summary>
    public const string PlanBlocked = nameof(PlanBlocked);

    /// <summary>A ship has no goal and no assignment (<c>ShipSymbol</c>, <c>Reason</c>).</summary>
    public const string ShipIdle = nameof(ShipIdle);

    /// <summary>A ship's goal was blocked (<c>ShipSymbol</c>, <c>GoalKind</c>, <c>Reason</c>).</summary>
    public const string ShipBlocked = nameof(ShipBlocked);

    /// <summary>A setting changed (<c>Setting</c>, <c>OldValue</c>, <c>NewValue</c>).</summary>
    public const string SettingChanged = nameof(SettingChanged);

    /// <summary>The SpaceTraders server was reset (<c>Detail</c>).</summary>
    public const string ResetDetected = nameof(ResetDetected);

    /// <summary>The API answered 502 and calls pause (<c>PausedUntil</c>).</summary>
    public const string ApiUnavailable = nameof(ApiUnavailable);

    /// <summary>The API answers again after a pause.</summary>
    public const string ApiAvailable = nameof(ApiAvailable);

    /// <summary>An anomaly became active (<c>Rule</c>, <c>Subject</c>).</summary>
    public const string AnomalyRaised = nameof(AnomalyRaised);

    /// <summary>An anomaly is no longer active (<c>Rule</c>, <c>Subject</c>).</summary>
    public const string AnomalyCleared = nameof(AnomalyCleared);
}
