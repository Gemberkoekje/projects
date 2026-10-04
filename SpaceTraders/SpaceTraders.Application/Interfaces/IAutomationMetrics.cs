using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Interfaces;

/// <summary>
/// Records automation metrics. The API host exports them to Prometheus, on a port of its own
/// (<c>Metrics:Port</c>) that needs no API key.
/// </summary>
public interface IAutomationMetrics
{
    /// <summary>Counts a goal the circuit breaker blocked (<c>spacetraders_goal_breaker_trips_total</c>).</summary>
    void GoalBreakerTripped(string shipSymbol);

    /// <summary>Records the database size (<c>spacetraders_db_size_bytes</c>).</summary>
    void DatabaseSize(long bytes);

    /// <summary>Counts a goal step that ran (<c>spacetraders_goal_steps_total{kind}</c>).</summary>
    void GoalStep(string goalKind);

    /// <summary>
    /// Counts a request the bot initiated to the SpaceTraders API (<c>spacetraders_api_requests_initiated_total{method,endpoint}</c>,
    /// slice 2.10): once, as it starts, before the pause after a 502 and the local request budget. A request the pause
    /// refuses counts here but never in <see cref="ApiRequest"/>, and a retry of a 429 counts again there but not here.
    /// </summary>
    void ApiRequestInitiated(string method, string endpoint);

    /// <summary>
    /// Counts a response from the SpaceTraders API (<c>spacetraders_api_requests_total{method,endpoint,status}</c>).
    /// Every attempt counts, retries of a 429 included; <paramref name="endpoint"/> is the route
    /// template, and <paramref name="status"/> the status code, or <c>error</c> when no response came.
    /// </summary>
    void ApiRequest(string method, string endpoint, string status);

    /// <summary>
    /// Counts a 429 from the SpaceTraders API (<c>spacetraders_api_throttled_total{source}</c>):
    /// <c>rate_limiter</c> when it carries the rate limiter's headers, else <c>infrastructure</c>.
    /// </summary>
    void ApiThrottled(string source);

    /// <summary>
    /// Adds the time a request waited for the local request budget
    /// (<c>spacetraders_api_rate_limit_wait_seconds_total</c>), by <paramref name="kind"/>: <c>read</c>
    /// for a GET, which gives way to writes (D19), or <c>write</c> for anything else.
    /// </summary>
    void RateLimitWait(TimeSpan wait, string kind);

    /// <summary>Counts a message Wolverine handled without an error (<c>spacetraders_messages_handled_total{type}</c>).</summary>
    void MessageHandled(string messageType);

    /// <summary>Adds credits earned (<c>spacetraders_credits_earned_total{source}</c>), by ledger category.</summary>
    void CreditsEarned(string source, long amount);

    /// <summary>Adds credits spent (<c>spacetraders_credits_spent_total{category}</c>), by ledger category.</summary>
    void CreditsSpent(string category, long amount);

    /// <summary>
    /// Adds units of a good our ships sold to a market (<c>spacetraders_goods_sold_units_total{system,waypoint,good}</c>),
    /// whoever sold them: traders, miners, siphoners and the command ship in its spare time. Next to the market's prices
    /// and supply, it shows what our sales do to a market and to the goods it makes from them. The system is the
    /// waypoint's: its symbol up to the last dash.
    /// </summary>
    /// <param name="waypointSymbol">The market, such as <c>X1-DC53-H51</c>.</param>
    /// <param name="tradeSymbol">The good sold.</param>
    /// <param name="units">The units sold.</param>
    void GoodsSold(string waypointSymbol, string tradeSymbol, int units);

    /// <summary>
    /// Adds units of a good our ships bought from a market (<c>spacetraders_goods_bought_units_total{system,waypoint,good}</c>).
    /// The system is the waypoint's: its symbol up to the last dash.
    /// </summary>
    /// <param name="waypointSymbol">The market, such as <c>X1-DC53-K85</c>.</param>
    /// <param name="tradeSymbol">The good bought.</param>
    /// <param name="units">The units bought.</param>
    void GoodsBought(string waypointSymbol, string tradeSymbol, int units);

    /// <summary>
    /// Counts a trip that ended (<c>spacetraders_trips_total{activity}</c>, D46): a <c>trade</c>, <c>mining</c>,
    /// <c>siphoning</c> or <c>spare_time</c> trip, or a <c>contract</c> round trip.
    /// </summary>
    void TripEnded(string activity);

    /// <summary>
    /// Adds what a trip made after fuel, by activity (D46): a profit to <c>spacetraders_trip_profit_credits_total{activity}</c>,
    /// a loss, as a positive amount, to <c>spacetraders_trip_loss_credits_total{activity}</c>, and 0 to the other, so both
    /// series exist. A counter can't go down, so the two are apart; profit minus loss is what the activity made. A
    /// contract's deposit and payout count as its profit when they come.
    /// </summary>
    void TripProfit(string activity, long profit);

    /// <summary>
    /// Adds units a ship extracted (<c>spacetraders_extracted_units_total{ship,good}</c>): a mining laser's yield,
    /// or a gas siphon's (slice 6.7). Neither a siphon nor a spare-time extraction (slice 6.8, which takes no survey
    /// by design) is counted by <see cref="Extraction"/>, which the survey statistics read.
    /// </summary>
    void Extracted(string shipSymbol, string tradeSymbol, int units);

    /// <summary>Adds units a ship jettisoned (<c>spacetraders_jettisoned_units_total{ship,good}</c>).</summary>
    void Jettisoned(string shipSymbol, string tradeSymbol, int units);

    /// <summary>Counts an extraction (<c>spacetraders_extractions_total{ship,surveyed}</c>), with a survey or without one (slice 6.4).</summary>
    void Extraction(string shipSymbol, bool surveyed);

    /// <summary>Counts a survey a ship took (<c>spacetraders_surveys_taken_total{waypoint,size}</c>).</summary>
    void SurveyTaken(string waypointSymbol, string size);

    /// <summary>
    /// Counts a survey that ended (<c>spacetraders_surveys_ended_total{waypoint,reason,used}</c>): it
    /// <c>expired</c>, or the API refused it as <c>exhausted</c> or <c>not_verified</c>; <c>used</c> when any
    /// extraction was made with it. Many unused surveys mean surveying runs ahead of the miners.
    /// </summary>
    void SurveyEnded(string waypointSymbol, string reason, bool used);

    /// <summary>
    /// Records the usable surveys in the cache (<c>spacetraders_surveys_active{waypoint,used}</c>), by
    /// waypoint and whether any extraction used them yet. A waypoint without surveys loses its series.
    /// </summary>
    void Surveys(IReadOnlyCollection<SurveyMetricsSample> surveys);

    /// <summary>Sets whether an anomaly is active (<c>spacetraders_anomaly_active{rule,subject}</c>).</summary>
    void Anomaly(string rule, string subject, bool active);

    /// <summary>Records when the server resets next (<c>spacetraders_server_next_reset_timestamp_seconds</c>).</summary>
    void NextServerReset(DateTimeOffset next);

    /// <summary>Records the agent's credits as cached (<c>spacetraders_agent_credits</c>).</summary>
    void Credits(long credits);

    /// <summary>
    /// Records the credits a ship purchase must leave (<c>spacetraders_credit_reserve</c>, D51): the floor, and the credits
    /// per unit the ships that trade can carry. A purchase waits until the credits are its price above this.
    /// </summary>
    void ReservedCredits(long credits);

    /// <summary>
    /// Records every ship's state (<c>spacetraders_ships{role,state}</c> and
    /// <c>spacetraders_ship_status_since_timestamp_seconds{ship,role,state,goal,reason}</c>), where it
    /// is and what it does (<c>spacetraders_ship_info{ship,location,activity}</c>), what it can do
    /// (<c>spacetraders_ship_capabilities_info{ship,can}</c>), what the bot calls it
    /// (<c>spacetraders_ship_name_info{ship,name,type}</c>, slice 2.14), when it arrives
    /// (<c>spacetraders_ship_arrival_timestamp_seconds{ship}</c>, while in transit) and its hold
    /// (<c>spacetraders_ship_cargo_units{ship,good}</c>, <c>spacetraders_ship_cargo_capacity_units{ship}</c>).
    /// A ship whose labels changed since the last call entered its state at <paramref name="now"/>;
    /// a ship that is no longer in <paramref name="ships"/> loses its series, and so does a good that
    /// is no longer aboard.
    /// </summary>
    void Fleet(IReadOnlyCollection<ShipMetricsSample> ships, DateTimeOffset now);

    /// <summary>
    /// Records the accepted contracts' deliverables (<c>spacetraders_contract_units_required</c>,
    /// <c>spacetraders_contract_units_fulfilled</c> and <c>spacetraders_contract_deadline_timestamp_seconds</c>).
    /// A contract that is no longer in <paramref name="deliverables"/> loses its series.
    /// </summary>
    void Contracts(IReadOnlyCollection<ContractMetricsSample> deliverables);

    /// <summary>
    /// Records the construction sites' materials (slice 6.6: <c>spacetraders_construction_units_required</c> and
    /// <c>spacetraders_construction_units_fulfilled</c>, each <c>{site,trade_symbol}</c>), as cached: the home system's jump
    /// gate, complete or not. A material that is no longer in <paramref name="materials"/> loses its series.
    /// </summary>
    void Construction(IReadOnlyCollection<ConstructionMetricsSample> materials);

    /// <summary>
    /// Records the cached markets: when each was observed
    /// (<c>spacetraders_market_observed_timestamp_seconds{system,waypoint,waypoint_type}</c>) and, once a
    /// ship has been there, each good's prices, trade volume, supply and activity
    /// (<c>spacetraders_market_purchase_price</c>, <c>_sell_price</c>, <c>_trade_volume</c>, <c>_supply</c>
    /// and <c>_activity</c>, each <c>{system,waypoint,good,kind}</c>). A market or a good that is no
    /// longer in <paramref name="markets"/> loses its series.
    /// </summary>
    void Markets(IReadOnlyCollection<MarketMetricsSample> markets);

    /// <summary>
    /// Records the cached shipyards: when each was observed
    /// (<c>spacetraders_shipyard_observed_timestamp_seconds{system,waypoint,waypoint_type}</c>), the ship
    /// types it sells (<c>spacetraders_shipyard_ship_type{system,waypoint,ship_type}</c>) and, once a ship
    /// has been there, their prices and supply (<c>spacetraders_shipyard_ship_price</c>,
    /// <c>_ship_supply</c>), their tank and hold (<c>_ship_fuel_capacity_units</c>,
    /// <c>_ship_cargo_capacity_units</c>), and what each could do in the fleet and carries, one series per
    /// ship type (<c>spacetraders_shipyard_ship_info{system,waypoint,ship_type,can,equipment}</c>, slice
    /// 2.11). A shipyard or a ship type that is no longer listed loses its series, and a ship type listed
    /// without details keeps only its type.
    /// </summary>
    void Shipyards(IReadOnlyCollection<ShipyardMetricsSample> shipyards);

    /// <summary>
    /// Records the game's production chains, one series per good
    /// (<c>spacetraders_good_supply_chain{good,made_from,used_for}</c>): what it is made from, and what is
    /// made from it. Raw goods are made from nothing.
    /// </summary>
    /// <param name="madeFrom">Each exported good, with the goods it is made from.</param>
    void SupplyChain(IReadOnlyDictionary<string, IReadOnlyList<string>> madeFrom);

    /// <summary>
    /// Records what each known system offers, for the systems dashboard (asked on 2026-10-04): its state and gate
    /// (<c>spacetraders_system_info{system,state,gate,gate_state}</c>, always 1), its jumps from home
    /// (<c>spacetraders_system_jumps_from_home</c>), when it was explored (<c>spacetraders_system_explored_timestamp_seconds</c>),
    /// the systems its gate connects to (<c>spacetraders_system_connection_info{system,to}</c>), its markets, shipyards and
    /// uncharted waypoints (<c>spacetraders_system_facilities{system,kind}</c>), its waypoints by type
    /// (<c>spacetraders_system_waypoints{system,type}</c>), where each good can be mined or siphoned
    /// (<c>spacetraders_system_gathering_sites{system,good}</c>), the raw goods its markets buy
    /// (<c>spacetraders_system_raw_good_price{system,good,market}</c> and <c>_raw_good_supply{system,good}</c>), and its best
    /// trades (<c>spacetraders_system_trade_margin</c> and <c>_trade_volume</c>, each <c>{system,good,buy_at,sell_at}</c>).
    /// A series that is no longer in <paramref name="systems"/> is removed.
    /// </summary>
    /// <param name="systems">Every system the bot knows.</param>
    void Systems(IReadOnlyCollection<SpaceTraders.Application.Exploring.SystemSample> systems);

    /// <summary>
    /// Records the agent's settings, one series per setting, always 1
    /// (<c>spacetraders_setting_info{setting,current,next_run,description}</c>): its value now, the value the next run
    /// starts with (D69) and what it does, for the dashboard's settings table (slice 2.9). A setting whose values or
    /// description changed loses its old series, and a setting that is no longer in <paramref name="settings"/> loses its
    /// own.
    /// </summary>
    void Settings(IReadOnlyCollection<SettingMetricsSample> settings);

    /// <summary>
    /// Records the role board's view (slice 6.9): each ship's role and why, one series per ship, always 1
    /// (<c>spacetraders_ship_role_info{ship,role,reason}</c>), and what each role it could take would earn it per hour, by
    /// the board's estimate (<c>spacetraders_ship_role_credits_per_hour{ship,role}</c>). A ship whose role or reason
    /// changed loses its old series, and a ship or role that is no longer in <paramref name="roles"/> loses its own.
    /// </summary>
    void Roles(IReadOnlyCollection<RoleMetricsSample> roles);

    /// <summary>
    /// Records the order ships are bought in (slice 6.10b, D43): what each plan that buys ships would buy now, one series per
    /// plan, its value what the ship costs as cached
    /// (<c>spacetraders_purchase_need_credits{plan,tier,position,ship_type,shipyard}</c>). <c>position</c> is the tier's
    /// place in the order, 1 for the contract's drone to 6 for drones and cargo ships in turn; the lowest is what the
    /// credits are saved up for. A plan that needs nothing, or whose need changed, loses its old series.
    /// </summary>
    void PurchaseNeeds(IReadOnlyCollection<PurchaseNeedMetricsSample> needs);
}

/// <summary>What one plan would buy, as the metrics show it (slice 6.10b, D43).</summary>
public sealed record PurchaseNeedMetricsSample
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public PurchaseNeedMetricsSample(string Plan, string Tier, int Position, string ShipType, string Shipyard, long Price)
    {
        this.Plan = Plan;
        this.Tier = Tier;
        this.Position = Position;
        this.ShipType = ShipType;
        this.Shipyard = Shipyard;
        this.Price = Price;
    }

    /// <summary>The plan that would buy, such as <c>Survey</c>.</summary>
    public required string Plan { get; init; }

    /// <summary>Its place in the order by name, such as <c>Surveyor</c>.</summary>
    public required string Tier { get; init; }

    /// <summary>Its place in the order by number: 1 comes first.</summary>
    public required int Position { get; init; }

    /// <summary>The ship, such as <c>SHIP_SURVEYOR</c>.</summary>
    public required string ShipType { get; init; }

    /// <summary>Where it would be bought.</summary>
    public required string Shipyard { get; init; }

    /// <summary>What it costs, as cached.</summary>
    public required long Price { get; init; }
}

/// <summary>One ship on the role board as the metrics show it (slice 6.9).</summary>
public sealed record RoleMetricsSample
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public RoleMetricsSample(string Ship, string Role, string Reason, IReadOnlyDictionary<string, long> CreditsPerHour)
    {
        this.Ship = Ship;
        this.Role = Role;
        this.Reason = Reason;
        this.CreditsPerHour = CreditsPerHour;
    }

    /// <summary>The ship.</summary>
    public required string Ship { get; init; }

    /// <summary>Its role: <c>Survey</c>, <c>Mine</c>, <c>Siphon</c>, <c>Trade</c> or <c>None</c>.</summary>
    public required string Role { get; init; }

    /// <summary>Why it has it, as the board says (<c>survey_first</c>, <c>most_profitable</c>, ...).</summary>
    public required string Reason { get; init; }

    /// <summary>For each role it could take but surveying, what its best trip earns per hour.</summary>
    public required IReadOnlyDictionary<string, long> CreditsPerHour { get; init; }
}

/// <summary>One of the agent's settings as the metrics show it (slice 2.9).</summary>
public sealed record SettingMetricsSample
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SettingMetricsSample(string Setting, string Value, string NextRun, string Description)
    {
        this.Setting = Setting;
        this.Value = Value;
        this.NextRun = NextRun;
        this.Description = Description;
    }

    /// <summary>The setting's key, such as <c>Automation.Plan.Mining.Enabled</c>.</summary>
    public required string Setting { get; init; }

    /// <summary>Its value now, as it may be shown: <c>(hidden)</c> for one that may hold a secret.</summary>
    public required string Value { get; init; }

    /// <summary>
    /// The value the next run starts with, as it may be shown (D69): the one chosen for it, else the default; empty for a
    /// key a run doesn't start with (a <c>Runtime.*</c> status flag, or one the seed doesn't hold).
    /// </summary>
    public required string NextRun { get; init; }

    /// <summary>What it does; empty for a key nothing describes (one only <c>PUT /settings/{key}</c> wrote).</summary>
    public required string Description { get; init; }
}

/// <summary>The usable surveys of one waypoint, used or not yet, as the metrics show them.</summary>
public sealed record SurveyMetricsSample
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SurveyMetricsSample(string Waypoint, bool Used, int Count)
    {
        this.Waypoint = Waypoint;
        this.Used = Used;
        this.Count = Count;
    }

    /// <summary>The surveyed waypoint.</summary>
    public required string Waypoint { get; init; }

    /// <summary>Whether any extraction was made with these surveys.</summary>
    public required bool Used { get; init; }

    /// <summary>How many there are.</summary>
    public required int Count { get; init; }
}

/// <summary>One cached market as the metrics show it.</summary>
public sealed record MarketMetricsSample
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MarketMetricsSample(string System, string Waypoint, string WaypointType, DateTimeOffset ObservedAt, IReadOnlyList<TradeGoodSnapshot> Goods)
    {
        this.System = System;
        this.Waypoint = Waypoint;
        this.WaypointType = WaypointType;
        this.ObservedAt = ObservedAt;
        this.Goods = Goods;
    }

    /// <summary>The system it is in.</summary>
    public required string System { get; init; }

    /// <summary>Its waypoint.</summary>
    public required string Waypoint { get; init; }

    /// <summary>The waypoint's type (<c>PLANET</c>, <c>ASTEROID</c>, ...); empty when unknown.</summary>
    public required string WaypointType { get; init; }

    /// <summary>When the bot last refreshed it.</summary>
    public required DateTimeOffset ObservedAt { get; init; }

    /// <summary>Its goods with their prices; empty until a ship has been there.</summary>
    public required IReadOnlyList<TradeGoodSnapshot> Goods { get; init; }
}

/// <summary>One cached shipyard as the metrics show it.</summary>
public sealed record ShipyardMetricsSample
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipyardMetricsSample(string System, string Waypoint, string WaypointType, DateTimeOffset ObservedAt, IReadOnlyList<string> ShipTypes, IReadOnlyList<ShipyardShipMetricsSample> Ships)
    {
        this.System = System;
        this.Waypoint = Waypoint;
        this.WaypointType = WaypointType;
        this.ObservedAt = ObservedAt;
        this.ShipTypes = ShipTypes;
        this.Ships = Ships;
    }

    /// <summary>The system it is in.</summary>
    public required string System { get; init; }

    /// <summary>Its waypoint.</summary>
    public required string Waypoint { get; init; }

    /// <summary>The waypoint's type; empty when unknown.</summary>
    public required string WaypointType { get; init; }

    /// <summary>When the bot last refreshed it.</summary>
    public required DateTimeOffset ObservedAt { get; init; }

    /// <summary>The ship types it sells.</summary>
    public required IReadOnlyList<string> ShipTypes { get; init; }

    /// <summary>The ships with their prices; empty until a ship has been there.</summary>
    public required IReadOnlyList<ShipyardShipMetricsSample> Ships { get; init; }
}

/// <summary>One ship type a shipyard lists in full, as the shipyards table shows it (slice 2.11).</summary>
public sealed record ShipyardShipMetricsSample
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipyardShipMetricsSample(string Type, long PurchasePrice, string Supply)
    {
        this.Type = Type;
        this.PurchasePrice = PurchasePrice;
        this.Supply = Supply;
    }

    /// <summary>The ship type, such as <c>SHIP_MINING_DRONE</c>.</summary>
    public required string Type { get; init; }

    /// <summary>What the shipyard charges for it.</summary>
    public required long PurchasePrice { get; init; }

    /// <summary>Its supply at the shipyard, <c>SCARCE</c> to <c>ABUNDANT</c>; empty when the shipyard gave none.</summary>
    public required string Supply { get; init; }

    /// <summary>What its tank holds, from its frame; 0 for a probe.</summary>
    public int FuelCapacity { get; init; }

    /// <summary>What its cargo holds take together; 0 for a ship without a hold.</summary>
    public int CargoCapacity { get; init; }

    /// <summary>
    /// What it could do in the fleet, judged as the fleet table's "can do" judges a ship: <c>Survey</c>, <c>Mine</c>,
    /// <c>Siphon</c> and <c>Trade</c>, in that order, such as <c>Mine, Trade</c>; <c>none</c> for a ship that can do
    /// none of them; but <c>Probe</c> for a probe.
    /// </summary>
    public string Can { get; init; } = string.Empty;

    /// <summary>
    /// Its mounts, then its modules, without their <c>MOUNT_</c> and <c>MODULE_</c> prefixes and without the cargo
    /// holds and crew quarters, such as <c>MINING_LASER_I, MINERAL_PROCESSOR_I</c>; <c>none</c> without any.
    /// </summary>
    public string Equipment { get; init; } = string.Empty;
}

/// <summary>One ship as the metrics show it.</summary>
public sealed record ShipMetricsSample
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipMetricsSample(string Ship, string Role, string State, string Goal, string Reason)
    {
        this.Ship = Ship;
        this.Role = Role;
        this.State = State;
        this.Goal = Goal;
        this.Reason = Reason;
    }

    /// <summary>The ship's symbol.</summary>
    public required string Ship { get; init; }

    /// <summary>Its type as cached: the registration role after startup sync (<c>COMMAND</c>, <c>SATELLITE</c>), the shipyard type for a ship bought since (B25).</summary>
    public required string Role { get; init; }

    /// <summary><c>DOCKED</c>, <c>IN_ORBIT</c> or <c>IN_TRANSIT</c>, with arrivals dead-reckoned.</summary>
    public required string State { get; init; }

    /// <summary>Its goal's kind, else its assignment's type (<c>Contract</c>), else <c>None</c>.</summary>
    public required string Goal { get; init; }

    /// <summary>Why its goal is blocked (<c>runaway</c>); empty otherwise.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Where it is: its waypoint and the waypoint's type, such as <c>X1-AB-A1 (ASTEROID)</c>; in
    /// transit, <c>→</c> and where it goes.
    /// </summary>
    public string Location { get; init; } = string.Empty;

    /// <summary>What the bot has it do, in a few words, such as <c>mining COPPER_ORE</c>, <c>scouting</c> or <c>idle</c>.</summary>
    public string Activity { get; init; } = string.Empty;

    /// <summary>
    /// What its equipment lets it do, whichever plans are on: <c>Survey</c>, <c>Mine</c>, <c>Siphon</c> and <c>Trade</c>,
    /// in that order, such as <c>Siphon, Trade</c> for a siphon drone; <c>none</c> for a probe or a ship that can do none
    /// of them.
    /// </summary>
    public string Capabilities { get; init; } = string.Empty;

    /// <summary>When it arrives, while in transit; <c>default</c> otherwise.</summary>
    public DateTimeOffset ArrivesAt { get; init; }

    /// <summary>The units its hold takes.</summary>
    public int CargoCapacity { get; init; }

    /// <summary>What its hold carries, per good.</summary>
    public IReadOnlyList<CargoItemModel> Cargo { get; init; } = [];

    /// <summary>What was paid for it and for the mounts and modules installed on it, as the ledger has it; 0 for a starting ship.</summary>
    public long Value { get; init; }

    /// <summary>The name the bot gives it beside its symbol (slice 2.14, D72), such as <c>SPUTNIK-2</c>; empty before the agent is known.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The type its name is for: the shipyard type, such as <c>SHIP_PROBE</c>, else its registration role.</summary>
    public string NamedType { get; init; } = string.Empty;
}

/// <summary>One material of a construction site (slice 6.6).</summary>
public sealed record ConstructionMetricsSample
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ConstructionMetricsSample(string Site, string TradeSymbol, int UnitsRequired, int UnitsFulfilled)
    {
        this.Site = Site;
        this.TradeSymbol = TradeSymbol;
        this.UnitsRequired = UnitsRequired;
        this.UnitsFulfilled = UnitsFulfilled;
    }

    /// <summary>The construction site: the jump gate's waypoint.</summary>
    public required string Site { get; init; }

    public required string TradeSymbol { get; init; }

    public required int UnitsRequired { get; init; }

    public required int UnitsFulfilled { get; init; }
}

/// <summary>One deliverable of an accepted contract.</summary>
public sealed record ContractMetricsSample
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ContractMetricsSample(string Contract, string TradeSymbol, int UnitsRequired, int UnitsFulfilled, DateTimeOffset Deadline)
    {
        this.Contract = Contract;
        this.TradeSymbol = TradeSymbol;
        this.UnitsRequired = UnitsRequired;
        this.UnitsFulfilled = UnitsFulfilled;
        this.Deadline = Deadline;
    }

    /// <summary>The contract's id.</summary>
    public required string Contract { get; init; }

    public required string TradeSymbol { get; init; }

    public required int UnitsRequired { get; init; }

    public required int UnitsFulfilled { get; init; }

    /// <summary>The contract's deadline; <c>default</c> when unknown.</summary>
    public required DateTimeOffset Deadline { get; init; }
}
