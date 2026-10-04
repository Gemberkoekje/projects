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

    /// <summary>
    /// A ship surveyed a waypoint (slice 6.4); one line per survey it found (<c>ShipSymbol</c>,
    /// <c>WaypointSymbol</c>, <c>TradeSymbol</c> it surveyed for, <c>Signature</c>, <c>Size</c>,
    /// <c>Deposits</c>, <c>Expiration</c>).
    /// </summary>
    public const string Surveyed = nameof(Surveyed);

    /// <summary>
    /// A survey ended: it expired, or the API refused it as exhausted or not verified (<c>Signature</c>,
    /// <c>WaypointSymbol</c>, <c>Size</c>, <c>Reason</c>, <c>Extractions</c> made with it, <c>SurveyedAt</c>).
    /// </summary>
    public const string SurveyEnded = nameof(SurveyEnded);

    /// <summary>
    /// A ship extracted (<c>ShipSymbol</c>, <c>WaypointSymbol</c>, <c>TradeSymbol</c> it got, <c>Units</c>,
    /// <c>Target</c> it mines for, and the survey's <c>Signature</c>, empty without one).
    /// </summary>
    public const string Extracted = nameof(Extracted);

    /// <summary>
    /// A miner took mining work (slice 6.4): a trip to mine and sell, or a place in the contract's work
    /// (<c>ShipSymbol</c>, <c>TradeSymbol</c>, <c>WaypointSymbol</c> it mines at, <c>SellWaypoint</c>,
    /// <c>Reason</c>: <c>contract</c>, <c>surveyed</c>, <c>low_supply</c>, <c>lowest_supply</c> (no market is
    /// short of an ore, D28), <c>uncovered</c> (an ore no miner works on, D48) or <c>held_cargo</c>). A trip to a
    /// market out of the ship's CRUISE reach logs <see cref="DriftStarted"/> when it sets off (D45).
    /// </summary>
    public const string MiningStarted = nameof(MiningStarted);

    /// <summary>
    /// A ship siphoned at a gas giant (slice 6.7: <c>ShipSymbol</c>, <c>WaypointSymbol</c>, <c>TradeSymbol</c> it
    /// got, <c>Units</c>, <c>Target</c>: the gas its trip is for). A siphon takes no survey.
    /// </summary>
    public const string Siphoned = nameof(Siphoned);

    /// <summary>
    /// A siphoner took a siphon trip (slice 6.7: <c>ShipSymbol</c>, <c>TradeSymbol</c>, <c>WaypointSymbol</c> it
    /// siphons at, <c>SellWaypoint</c>, <c>Reason</c>: <c>low_supply</c>, <c>lowest_supply</c> (no market is short
    /// of a gas, D28), <c>uncovered</c> (a gas no siphoner works on, D48) or <c>held_cargo</c>). A trip to a market
    /// out of the ship's CRUISE reach logs <see cref="DriftStarted"/> when it sets off (D45).
    /// </summary>
    public const string SiphonStarted = nameof(SiphonStarted);

    /// <summary>
    /// A mining or siphon trip set off in DRIFT to its market, out of the ship's CRUISE reach (slice 6.10c, D45:
    /// <c>ShipSymbol</c>, <c>WaypointSymbol</c> it leaves, <c>SellWaypoint</c> it drifts to, <c>TradeSymbol</c>,
    /// <c>SourceWaypoint</c> it gathers at from there); or a ship that can only survey set off to the area where most drones
    /// mine (D54: <c>ShipSymbol</c>, <c>WaypointSymbol</c> it leaves, <c>Destination</c>). The drift burns 1 fuel whatever
    /// the distance and takes about ten times as long as in CRUISE; the ship's next flight, from that market, is in CRUISE.
    /// </summary>
    public const string DriftStarted = nameof(DriftStarted);

    /// <summary>
    /// A ship with nothing to survey or trade took a spare-time trip (slice 6.8: <c>ShipSymbol</c>, <c>WaypointSymbol</c>
    /// it gathers at, <c>Method</c>: <c>mines</c> or <c>siphons</c>). Its extractions log <c>Extracted</c> and its
    /// siphons <c>Siphoned</c>, with <c>Target</c> <c>whatever sells</c>; its sales <c>CargoSold</c>.
    /// </summary>
    public const string GatheringStarted = nameof(GatheringStarted);

    /// <summary>
    /// A survey or a trade took a ship off its spare-time trip before its hold was full (slice 6.8, D34, D37:
    /// <c>ShipSymbol</c>, <c>WaypointSymbol</c> it gathered at, <c>Units</c> aboard, <c>Reason</c>: <c>survey</c>,
    /// which keeps the hold aboard, or <c>trade</c>, which sells it first).
    /// </summary>
    public const string GatheringInterrupted = nameof(GatheringInterrupted);

    /// <summary>
    /// A trip ended, with what it made after fuel (D46: <c>ShipSymbol</c>; <c>Activity</c>: <c>trade</c>, <c>mining</c>,
    /// <c>siphoning</c>, <c>spare_time</c>, <c>contract</c> or <c>construction</c>, which earns nothing; <c>Earned</c>, what its sales brought in; <c>Spent</c>, what
    /// its cargo cost; <c>FuelCost</c>, the fuel its ship bought since it started; <c>Profit</c>, the first less the other
    /// two, negative for a loss; <c>Minutes</c> it took; <c>Reason</c>: <c>sold</c>, <c>delivered</c> for a contract round
    /// trip, <c>interrupted</c>, <c>runaway</c>, <c>rejected</c>, <c>nothing_aboard</c>, <c>no_buyer</c>,
    /// <c>not_bought_here</c>, <c>not_lucrative</c> or <c>not_possible</c>). A contract's deposit and payout aren't in its
    /// round trips: they count as its profit when they come.
    /// </summary>
    public const string TripEnded = nameof(TripEnded);

    /// <summary>
    /// The role board gave a ship another role (slice 6.9, D38: <c>ShipSymbol</c>, <c>OldRole</c>, <c>NewRole</c>,
    /// <c>Reason</c>: <c>only_role</c>, <c>survey_first</c>, <c>contract</c>, <c>coverage</c>, <c>gathers_first</c>,
    /// <c>construction</c>, <c>most_profitable</c>, <c>no_work</c> or <c>no_role</c>; for a role chosen by profit also
    /// <c>CreditsPerHour</c> and the <c>Job</c> that decided it). It takes effect when the ship's trip ends.
    /// </summary>
    public const string RoleChanged = nameof(RoleChanged);

    /// <summary>
    /// A free ship threw cargo overboard that nothing would sell or use (D42: <c>ShipSymbol</c>, <c>TradeSymbol</c>,
    /// <c>Units</c>, <c>WaypointSymbol</c>, <c>Reason</c>: <c>no_buyer</c>, no market it can reach buys it, or
    /// <c>not_worth_the_fuel</c>, the best sale doesn't pay for the fuel to get there).
    /// </summary>
    public const string CargoJettisoned = nameof(CargoJettisoned);

    /// <summary>
    /// A ship took a construction trip (slice 6.6: <c>ShipSymbol</c>, <c>TradeSymbol</c>, <c>Units</c>, <c>BuyWaypoint</c>,
    /// <c>WaypointSymbol</c>: the construction site; for a purchase also <c>BuyPrice</c>, <c>Cost</c> with its fuel and
    /// <c>FuelCost</c>; <c>Reason</c>: <c>purchase</c>, or <c>held_cargo</c> for materials the ship already held).
    /// </summary>
    public const string ConstructionStarted = nameof(ConstructionStarted);

    /// <summary>
    /// A ship supplied a construction site (slice 6.6: <c>ShipSymbol</c>, <c>TradeSymbol</c>, <c>Units</c>,
    /// <c>WaypointSymbol</c>, and the site's <c>Fulfilled</c> and <c>Required</c> units of the material afterwards).
    /// </summary>
    public const string ConstructionSupplied = nameof(ConstructionSupplied);

    /// <summary>
    /// A construction trip was given up (slice 6.6: <c>ShipSymbol</c>, <c>TradeSymbol</c>, <c>WaypointSymbol</c>,
    /// <c>Reason</c>: at the market <c>not_needed</c>, <c>not_sold_here</c>, <c>low_supply</c> (D61), <c>not_full_hold</c>
    /// (D62) or <c>over_budget</c> (D59); at the site <c>not_needed</c>; at Warning when the API refused the supply,
    /// <c>not_needed</c> or <c>wrong_location</c>, with the <c>Units</c> kept aboard). The construction plan chooses again.
    /// </summary>
    public const string ConstructionDropped = nameof(ConstructionDropped);

    /// <summary>
    /// A probe was sent to a shipyard where a purchase waits for one of our ships, which the API requires
    /// (slice 6.3, D30: <c>ShipSymbol</c>, <c>WaypointSymbol</c>, <c>ShipType</c> the purchase is for).
    /// </summary>
    public const string ProbeCalled = nameof(ProbeCalled);

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
