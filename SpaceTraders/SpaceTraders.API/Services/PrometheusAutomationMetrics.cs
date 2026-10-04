using Prometheus;
using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.API.Services;

/// <summary>
/// Exports <see cref="IAutomationMetrics"/> to Prometheus. It defines every <c>spacetraders_*</c>
/// metric when it is created, so a scrape lists them all, also before they have a value. Every
/// counter series reaches Prometheus at 0 before it counts (<see cref="ZeroFirstCounter"/>, B43); a
/// gauge without labels has no series until it has a value (B52), as 0 would be read as one.
/// </summary>
/// <remarks>Thread-safe: the per-ship and per-contract series it tracks are guarded by a lock.</remarks>
public sealed class PrometheusAutomationMetrics : IAutomationMetrics
{
    private readonly ZeroFirstCounter _goalBreakerTrips;
    private readonly Gauge _databaseSizeBytes;
    private readonly ZeroFirstCounter _goalSteps;
    private readonly ZeroFirstCounter _apiRequestsInitiated;
    private readonly ZeroFirstCounter _apiRequests;
    private readonly ZeroFirstCounter _apiThrottled;
    private readonly ZeroFirstCounter _rateLimitWaitSeconds;
    private readonly ZeroFirstCounter _messagesHandled;
    private readonly ZeroFirstCounter _creditsEarned;
    private readonly ZeroFirstCounter _creditsSpent;
    private readonly ZeroFirstCounter _goodsSold;
    private readonly ZeroFirstCounter _goodsBought;
    private readonly ZeroFirstCounter _trips;
    private readonly ZeroFirstCounter _tripProfit;
    private readonly ZeroFirstCounter _tripLoss;
    private readonly Gauge _anomalyActive;
    private readonly Gauge _nextServerReset;
    private readonly Gauge _credits;
    private readonly Gauge _ships;
    private readonly Gauge _shipStatusSince;
    private readonly Gauge _contractUnitsRequired;
    private readonly Gauge _contractUnitsFulfilled;
    private readonly Gauge _contractDeadline;
    private readonly Gauge _constructionUnitsRequired;
    private readonly Gauge _constructionUnitsFulfilled;
    private readonly ZeroFirstCounter _extractedUnits;
    private readonly ZeroFirstCounter _jettisonedUnits;
    private readonly ZeroFirstCounter _extractions;
    private readonly ZeroFirstCounter _surveysTaken;
    private readonly ZeroFirstCounter _surveysEnded;
    private readonly Gauge _surveysActive;
    private readonly Gauge _shipInfo;
    private readonly Gauge _shipCapabilities;
    private readonly Gauge _shipArrival;
    private readonly Gauge _shipCargoUnits;
    private readonly Gauge _shipCargoCapacity;
    private readonly Gauge _shipValue;
    private readonly Gauge _marketObserved;
    private readonly Gauge _marketPurchasePrice;
    private readonly Gauge _marketSellPrice;
    private readonly Gauge _marketTradeVolume;
    private readonly Gauge _marketSupply;
    private readonly Gauge _marketActivity;
    private readonly Gauge _shipyardObserved;
    private readonly Gauge _shipyardShipType;
    private readonly Gauge _shipyardShipPrice;
    private readonly Gauge _shipyardShipSupply;
    private readonly Gauge _shipyardShipFuelCapacity;
    private readonly Gauge _shipyardShipCargoCapacity;
    private readonly Gauge _shipyardShipInfo;
    private readonly Gauge _supplyChain;
    private readonly Gauge _settingInfo;
    private readonly Gauge _roleInfo;
    private readonly Gauge _roleCreditsPerHour;
    private readonly Gauge _purchaseNeed;
    private readonly Gauge _creditReserve;

    private readonly Lock _lock = new();
    private readonly SampledGauge _systemInfo;
    private readonly SampledGauge _systemJumps;
    private readonly SampledGauge _systemExplored;
    private readonly SampledGauge _systemConnection;
    private readonly SampledGauge _systemFacilities;
    private readonly SampledGauge _systemWaypoints;
    private readonly SampledGauge _systemGatheringSites;
    private readonly SampledGauge _systemRawGoodPrice;
    private readonly SampledGauge _systemRawGoodSupply;
    private readonly SampledGauge _systemTradeMargin;
    private readonly SampledGauge _systemTradeVolume;
    private readonly Dictionary<string, string[]> _shipLabels = new(StringComparer.Ordinal);
    private readonly HashSet<(string Role, string State)> _shipCountLabels = [];
    private readonly HashSet<(string Contract, string TradeSymbol)> _deliverables = [];
    private readonly HashSet<string> _contracts = new(StringComparer.Ordinal);
    private readonly HashSet<(string Site, string TradeSymbol)> _constructionMaterials = [];
    private readonly Dictionary<string, (string Location, string Activity)> _shipInfoLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _shipCapabilityLabels = new(StringComparer.Ordinal);
    private readonly HashSet<string> _shipsInTransit = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _shipGoods = new(StringComparer.Ordinal);
    private readonly HashSet<(string System, string Waypoint, string WaypointType)> _markets = [];
    private readonly HashSet<(string System, string Waypoint, string Good, string Kind)> _marketGoods = [];
    private readonly HashSet<(string System, string Waypoint, string WaypointType)> _shipyards = [];
    private readonly HashSet<(string System, string Waypoint, string ShipType)> _shipyardShips = [];
    private readonly Dictionary<(string System, string Waypoint, string ShipType), (string Can, string Equipment)> _shipyardShipInfoLabels = [];
    private readonly HashSet<(string Good, string MadeFrom, string UsedFor)> _supplyChainLabels = [];
    private readonly HashSet<(string Waypoint, string Used)> _surveyLabels = [];
    private readonly Dictionary<string, (string Value, string Description)> _settingLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Role, string Reason)> _roleLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _purchaseNeedLabels = new(StringComparer.Ordinal);
    private readonly HashSet<(string Ship, string Role)> _roleEstimates = [];

    /// <summary>Defines the metrics in <paramref name="registry"/> (the default registry in the host).</summary>
    public PrometheusAutomationMetrics(CollectorRegistry registry)
    {
        var metrics = Metrics.WithCustomRegistry(registry);

        _goalBreakerTrips = ZeroFirst(
            "spacetraders_goal_breaker_trips_total",
            "Goals blocked by the per-ship circuit breaker for taking too many steps in a minute.",
            "ship");
        _databaseSizeBytes = UntilSet(
            "spacetraders_db_size_bytes",
            "Size of the bot's Postgres database (pg_database_size), read every 5 minutes.");
        _goalSteps = ZeroFirst(
            "spacetraders_goal_steps_total",
            "Goal steps run, by goal kind.",
            "kind");
        _apiRequestsInitiated = ZeroFirst(
            "spacetraders_api_requests_initiated_total",
            "Requests the bot initiated to the SpaceTraders API, by method and route template: once each, as it starts, before the pause after a 502 and the local request budget. Retries of a 429 don't count again.",
            "method",
            "endpoint");
        _apiRequests = ZeroFirst(
            "spacetraders_api_requests_total",
            "Responses from the SpaceTraders API, by method, route template and status code ('error' when none came). Every attempt counts, retries included.",
            "method",
            "endpoint",
            "status");
        _apiThrottled = ZeroFirst(
            "spacetraders_api_throttled_total",
            "429 responses from the SpaceTraders API: from its rate limiter (with x-ratelimit headers) or its cloud infrastructure (without).",
            "source");
        _rateLimitWaitSeconds = ZeroFirst(
            "spacetraders_api_rate_limit_wait_seconds_total",
            "Seconds that requests waited for the local request budget (2 per second plus a burst of 30 per minute), by kind: read (GET, gives way to writes) or write.",
            "kind");
        _messagesHandled = ZeroFirst(
            "spacetraders_messages_handled_total",
            "Messages Wolverine handled without an error, by message type.",
            "type");
        _creditsEarned = ZeroFirst(
            "spacetraders_credits_earned_total",
            "Credits earned, by ledger category.",
            "source");
        _creditsSpent = ZeroFirst(
            "spacetraders_credits_spent_total",
            "Credits spent, by ledger category.",
            "category");
        _goodsSold = ZeroFirst(
            "spacetraders_goods_sold_units_total",
            "Units of a good our ships sold to a market, whoever sold them: traders, miners, siphoners and the command ship in its spare time.",
            "system",
            "waypoint",
            "good");
        _goodsBought = ZeroFirst(
            "spacetraders_goods_bought_units_total",
            "Units of a good our ships bought from a market.",
            "system",
            "waypoint",
            "good");
        _trips = ZeroFirst(
            "spacetraders_trips_total",
            "Trips that ended, by activity: trade, mining, siphoning and spare_time trips, and contract round trips.",
            "activity");
        _tripProfit = ZeroFirst(
            "spacetraders_trip_profit_credits_total",
            "What trips made after fuel, by activity: each trip's sales less its cargo and fuel, when that isn't negative, and a contract's deposit and payout. Less spacetraders_trip_loss_credits_total, what the activity made.",
            "activity");
        _tripLoss = ZeroFirst(
            "spacetraders_trip_loss_credits_total",
            "What trips lost after fuel, by activity: each trip's cargo and fuel less its sales, when that is more; a contract round trip's fuel.",
            "activity");
        _anomalyActive = metrics.CreateGauge(
            "spacetraders_anomaly_active",
            "1 while an anomaly is active, 0 once it cleared.",
            "rule",
            "subject");
        _nextServerReset = UntilSet(
            "spacetraders_server_next_reset_timestamp_seconds",
            "When the SpaceTraders server resets next (Unix time), as the server said at startup.");
        _credits = UntilSet(
            "spacetraders_agent_credits",
            "The agent's credits, as cached.");
        _ships = metrics.CreateGauge(
            "spacetraders_ships",
            "Ships by role (their type as cached) and state (DOCKED, IN_ORBIT or IN_TRANSIT).",
            "role",
            "state");
        _shipStatusSince = metrics.CreateGauge(
            "spacetraders_ship_status_since_timestamp_seconds",
            "One series per ship: its role, state, goal and blocked reason as labels, and as value when it entered that combination (Unix time; since the start at the latest).",
            "ship",
            "role",
            "state",
            "goal",
            "reason");
        _contractUnitsRequired = metrics.CreateGauge(
            "spacetraders_contract_units_required",
            "Units an accepted contract requires, per good.",
            "contract",
            "trade_symbol");
        _contractUnitsFulfilled = metrics.CreateGauge(
            "spacetraders_contract_units_fulfilled",
            "Units delivered to an accepted contract, per good.",
            "contract",
            "trade_symbol");
        _contractDeadline = metrics.CreateGauge(
            "spacetraders_contract_deadline_timestamp_seconds",
            "Deadline of an accepted contract (Unix time).",
            "contract");
        _constructionUnitsRequired = metrics.CreateGauge(
            "spacetraders_construction_units_required",
            "Units a construction site (the home system's jump gate) requires, per material.",
            "site",
            "trade_symbol");
        _constructionUnitsFulfilled = metrics.CreateGauge(
            "spacetraders_construction_units_fulfilled",
            "Units supplied to a construction site (the home system's jump gate), per material, by anyone.",
            "site",
            "trade_symbol");
        _extractedUnits = ZeroFirst(
            "spacetraders_extracted_units_total",
            "Units ships extracted, by ship and good: each extraction's or siphon's yield, before what isn't wanted is jettisoned.",
            "ship",
            "good");
        _jettisonedUnits = ZeroFirst(
            "spacetraders_jettisoned_units_total",
            "Units ships jettisoned, by ship and good: extracted goods their work doesn't need.",
            "ship",
            "good");
        _extractions = ZeroFirst(
            "spacetraders_extractions_total",
            "Extractions, by ship and whether a survey guided them (surveyed: true or false).",
            "ship",
            "surveyed");
        _surveysTaken = ZeroFirst(
            "spacetraders_surveys_taken_total",
            "Surveys our ships took, by waypoint and deposit size (SMALL, MODERATE, LARGE).",
            "waypoint",
            "size");
        _surveysEnded = ZeroFirst(
            "spacetraders_surveys_ended_total",
            "Surveys that ended, by waypoint, why (expired, exhausted, not_verified) and whether any extraction used them (used: true or false).",
            "waypoint",
            "reason",
            "used");
        _surveysActive = metrics.CreateGauge(
            "spacetraders_surveys_active",
            "Usable surveys in the cache, by waypoint and whether any extraction used them yet (used: true or false).",
            "waypoint",
            "used");
        _shipInfo = metrics.CreateGauge(
            "spacetraders_ship_info",
            "One series per ship, always 1: where it is (its waypoint and the waypoint's type; in transit, an arrow and where it goes) and what the bot has it do.",
            "ship",
            "location",
            "activity");
        _shipCapabilities = metrics.CreateGauge(
            "spacetraders_ship_capabilities_info",
            "One series per ship, always 1: the roles its equipment allows, whichever plans are on (Survey, Mine, Siphon and Trade, in that order; none for a probe or a ship that can do none of them).",
            "ship",
            "can");
        _shipArrival = metrics.CreateGauge(
            "spacetraders_ship_arrival_timestamp_seconds",
            "When a ship in transit arrives (Unix time); no series while it isn't travelling.",
            "ship");
        _shipCargoUnits = metrics.CreateGauge(
            "spacetraders_ship_cargo_units",
            "Units in a ship's hold, per good aboard.",
            "ship",
            "good");
        _shipCargoCapacity = metrics.CreateGauge(
            "spacetraders_ship_cargo_capacity_units",
            "Units a ship's hold takes.",
            "ship");
        _shipValue = metrics.CreateGauge(
            "spacetraders_ship_value_credits",
            "What was paid for a ship and for the mounts and modules installed on it; 0 for a starting ship.",
            "ship");
        _systemInfo = new SampledGauge(metrics.CreateGauge(
            "spacetraders_system_info",
            "One series per system the bot knows, always 1: what the explore plan knows of it (home, explored, to_explore, gate_under_construction, jump_refused, no_gate, gate_unknown, cached) and its jump gate.",
            "system",
            "state",
            "gate",
            "gate_state"));
        _systemJumps = new SampledGauge(metrics.CreateGauge(
            "spacetraders_system_jumps_from_home",
            "How many jumps a system is from home through built gates (one more to a gate still under construction).",
            "system"));
        _systemExplored = new SampledGauge(metrics.CreateGauge(
            "spacetraders_system_explored_timestamp_seconds",
            "When the command ship explored a system (Unix time): it visited each market and shipyard there once.",
            "system"));
        _systemConnection = new SampledGauge(metrics.CreateGauge(
            "spacetraders_system_connection_info",
            "One series per jump gate connection the bot asked for, always 1.",
            "system",
            "to"));
        _systemFacilities = new SampledGauge(metrics.CreateGauge(
            "spacetraders_system_facilities",
            "A system's cached waypoints with a market, a shipyard, or still uncharted.",
            "system",
            "kind"));
        _systemWaypoints = new SampledGauge(metrics.CreateGauge(
            "spacetraders_system_waypoints",
            "A system's cached waypoints, by type.",
            "system",
            "type"));
        _systemGatheringSites = new SampledGauge(metrics.CreateGauge(
            "spacetraders_system_gathering_sites",
            "In how many waypoints of a system a good can be mined (by the asteroids' deposits) or siphoned (gas giants).",
            "system",
            "good"));
        _systemRawGoodPrice = new SampledGauge(metrics.CreateGauge(
            "spacetraders_system_raw_good_price",
            "The best price a market in a system pays for an ore or a gas it imports or exchanges, as last seen, and where.",
            "system",
            "good",
            "market"));
        _systemRawGoodSupply = new SampledGauge(metrics.CreateGauge(
            "spacetraders_system_raw_good_supply",
            "The lowest supply of an ore or a gas among the markets in a system that buy it: 1 SCARCE, 2 LIMITED, 3 MODERATE, 4 HIGH, 5 ABUNDANT.",
            "system",
            "good"));
        string[] tradeLabels = ["system", "good", "buy_at", "sell_at"];
        _systemTradeMargin = new SampledGauge(metrics.CreateGauge(
            "spacetraders_system_trade_margin",
            "The best trades within a system, one per good, the five best: what a unit earns before fuel, buying where it is cheapest and selling where it pays most.",
            tradeLabels));
        _systemTradeVolume = new SampledGauge(metrics.CreateGauge(
            "spacetraders_system_trade_volume",
            "The units one trade of a system's best trades moves at once: the smaller of the two markets' trade volumes.",
            tradeLabels));
        _marketObserved = metrics.CreateGauge(
            "spacetraders_market_observed_timestamp_seconds",
            "When the bot last refreshed a cached market (Unix time).",
            "system",
            "waypoint",
            "waypoint_type");
        string[] marketGoodLabels = ["system", "waypoint", "good", "kind"];
        _marketPurchasePrice = metrics.CreateGauge(
            "spacetraders_market_purchase_price",
            "What a market charges a ship per unit of a good, as last seen. kind is EXPORT, IMPORT or EXCHANGE.",
            marketGoodLabels);
        _marketSellPrice = metrics.CreateGauge(
            "spacetraders_market_sell_price",
            "What a market pays a ship per unit of a good, as last seen.",
            marketGoodLabels);
        _marketTradeVolume = metrics.CreateGauge(
            "spacetraders_market_trade_volume",
            "Units of a good a market trades per transaction before its price moves, as last seen.",
            marketGoodLabels);
        _marketSupply = metrics.CreateGauge(
            "spacetraders_market_supply",
            "A good's supply at a market, as last seen: 1 SCARCE, 2 LIMITED, 3 MODERATE, 4 HIGH, 5 ABUNDANT.",
            marketGoodLabels);
        _marketActivity = metrics.CreateGauge(
            "spacetraders_market_activity",
            "A good's activity at a market, as last seen: 0 RESTRICTED, 1 WEAK, 2 GROWING, 3 STRONG.",
            marketGoodLabels);
        _shipyardObserved = metrics.CreateGauge(
            "spacetraders_shipyard_observed_timestamp_seconds",
            "When the bot last refreshed a cached shipyard (Unix time).",
            "system",
            "waypoint",
            "waypoint_type");
        string[] shipyardShipLabels = ["system", "waypoint", "ship_type"];
        _shipyardShipType = metrics.CreateGauge(
            "spacetraders_shipyard_ship_type",
            "1 for each ship type a shipyard sells.",
            shipyardShipLabels);
        _shipyardShipPrice = metrics.CreateGauge(
            "spacetraders_shipyard_ship_price",
            "What a shipyard charges for a ship type, as last seen.",
            shipyardShipLabels);
        _shipyardShipSupply = metrics.CreateGauge(
            "spacetraders_shipyard_ship_supply",
            "A ship type's supply at a shipyard, as last seen: 1 SCARCE, 2 LIMITED, 3 MODERATE, 4 HIGH, 5 ABUNDANT.",
            shipyardShipLabels);
        _shipyardShipFuelCapacity = metrics.CreateGauge(
            "spacetraders_shipyard_ship_fuel_capacity_units",
            "What a ship type's tank holds (its frame's), as a shipyard last listed it; 0 for a probe.",
            shipyardShipLabels);
        _shipyardShipCargoCapacity = metrics.CreateGauge(
            "spacetraders_shipyard_ship_cargo_capacity_units",
            "Units a ship type's cargo holds take together, as a shipyard last listed it.",
            shipyardShipLabels);
        _shipyardShipInfo = metrics.CreateGauge(
            "spacetraders_shipyard_ship_info",
            "One series per ship type a shipyard listed in full, always 1: what it could do in the fleet, judged as spacetraders_ship_capabilities_info judges a ship (Survey, Mine, Siphon and Trade, in that order; none for a ship that can do none of them), but Probe for a probe; and its mounts and modules, without the cargo holds and crew quarters (none without any).",
            "system",
            "waypoint",
            "ship_type",
            "can",
            "equipment");
        _supplyChain = metrics.CreateGauge(
            "spacetraders_good_supply_chain",
            "One series per good, always 1: the goods it is made from and the goods made from it (the game's production chains).",
            "good",
            "made_from",
            "used_for");
        _settingInfo = metrics.CreateGauge(
            "spacetraders_setting_info",
            "One series per setting the agent has, always 1: its value now (one that may hold a secret shows (hidden)) and what it does.",
            "setting",
            "current",
            "description");
        _roleInfo = metrics.CreateGauge(
            "spacetraders_ship_role_info",
            "One series per ship on the role board, always 1: its role (Survey, Mine, Siphon, Trade or None) and why it has it.",
            "ship",
            "role",
            "reason");
        _roleCreditsPerHour = metrics.CreateGauge(
            "spacetraders_ship_role_credits_per_hour",
            "What each role a ship could take would earn it per hour, by the role board's estimate: its best trip, after fuel, with the production chains' share.",
            "ship",
            "role");
        _creditReserve = UntilSet(
            "spacetraders_credit_reserve",
            "The credits a ship purchase must leave (D51): FleetExpansion.MinCreditReserve, and FleetExpansion.ReservePerTradingCargoUnit for every unit the ships that trade can carry.");
        _purchaseNeed = metrics.CreateGauge(
            "spacetraders_purchase_need_credits",
            "What each plan that buys ships would buy now, by its place in the order ships are bought in (position 1 first): what the ship costs, as cached.",
            "plan",
            "tier",
            "position",
            "ship_type",
            "shipyard");

        // Counters reach Prometheus at 0 first, so increase() and rate() see their first increment (B43).
        ZeroFirstCounter ZeroFirst(string name, string help, params string[] labelNames)
            => new(metrics.CreateCounter(name, help, labelNames), registry);

        // A gauge without labels would be published at 0 at once, and the first scrape of a new pod could read the
        // credits as 0 before the first sample (B52). It is listed from the start, with a series once it is set.
        Gauge UntilSet(string name, string help)
            => metrics.CreateGauge(name, help, new GaugeConfiguration { SuppressInitialValue = true });
    }

    /// <inheritdoc />
    public void GoalBreakerTripped(string shipSymbol) => _goalBreakerTrips.Inc(1, shipSymbol);

    /// <inheritdoc />
    public void DatabaseSize(long bytes) => _databaseSizeBytes.Set(bytes);

    /// <inheritdoc />
    public void GoalStep(string goalKind) => _goalSteps.Inc(1, goalKind);

    /// <inheritdoc />
    public void ApiRequestInitiated(string method, string endpoint) => _apiRequestsInitiated.Inc(1, method, endpoint);

    /// <inheritdoc />
    public void ApiRequest(string method, string endpoint, string status) => _apiRequests.Inc(1, method, endpoint, status);

    /// <inheritdoc />
    public void ApiThrottled(string source) => _apiThrottled.Inc(1, source);

    /// <inheritdoc />
    public void RateLimitWait(TimeSpan wait, string kind) => _rateLimitWaitSeconds.Inc(wait.TotalSeconds, kind);

    /// <inheritdoc />
    public void MessageHandled(string messageType) => _messagesHandled.Inc(1, messageType);

    /// <inheritdoc />
    public void CreditsEarned(string source, long amount) => _creditsEarned.Inc(amount, source);

    /// <inheritdoc />
    public void CreditsSpent(string category, long amount) => _creditsSpent.Inc(amount, category);

    /// <inheritdoc />
    public void GoodsSold(string waypointSymbol, string tradeSymbol, int units)
        => _goodsSold.Inc(units, SystemOf(waypointSymbol), waypointSymbol, tradeSymbol);

    /// <inheritdoc />
    public void GoodsBought(string waypointSymbol, string tradeSymbol, int units)
        => _goodsBought.Inc(units, SystemOf(waypointSymbol), waypointSymbol, tradeSymbol);

    /// <inheritdoc />
    public void TripEnded(string activity) => _trips.Inc(1, activity);

    /// <inheritdoc />
    /// <remarks>
    /// Both counters get the activity's series, the one that doesn't count it at 0: PromQL's <c>profit - loss</c> drops
    /// an activity that has only one of the two.
    /// </remarks>
    public void TripProfit(string activity, long profit)
    {
        _tripProfit.Inc(Math.Max(profit, 0), activity);
        _tripLoss.Inc(Math.Max(-profit, 0), activity);
    }

    /// <inheritdoc />
    public void Extracted(string shipSymbol, string tradeSymbol, int units) => _extractedUnits.Inc(units, shipSymbol, tradeSymbol);

    /// <inheritdoc />
    public void Jettisoned(string shipSymbol, string tradeSymbol, int units) => _jettisonedUnits.Inc(units, shipSymbol, tradeSymbol);

    /// <inheritdoc />
    public void Extraction(string shipSymbol, bool surveyed) => _extractions.Inc(1, shipSymbol, Flag(surveyed));

    /// <inheritdoc />
    public void SurveyTaken(string waypointSymbol, string size) => _surveysTaken.Inc(1, waypointSymbol, size);

    /// <inheritdoc />
    public void SurveyEnded(string waypointSymbol, string reason, bool used) => _surveysEnded.Inc(1, waypointSymbol, reason, Flag(used));

    /// <inheritdoc />
    public void Surveys(IReadOnlyCollection<SurveyMetricsSample> surveys)
    {
        var current = surveys
            .GroupBy(sample => (sample.Waypoint, Used: Flag(sample.Used)))
            .ToDictionary(group => group.Key, group => group.Sum(sample => sample.Count));

        lock (_lock)
        {
            foreach (var gone in _surveyLabels.Where(series => !current.ContainsKey(series)).ToList())
            {
                _surveysActive.RemoveLabelled(gone.Waypoint, gone.Used);
                _surveyLabels.Remove(gone);
            }

            foreach (var (series, count) in current)
            {
                _surveysActive.WithLabels(series.Waypoint, series.Used).Set(count);
                _surveyLabels.Add(series);
            }
        }
    }

    /// <inheritdoc />
    public void Anomaly(string rule, string subject, bool active) => _anomalyActive.WithLabels(rule, subject).Set(active ? 1 : 0);

    /// <inheritdoc />
    public void NextServerReset(DateTimeOffset next) => _nextServerReset.Set(next.ToUnixTimeSeconds());

    /// <inheritdoc />
    public void Credits(long credits) => _credits.Set(credits);

    /// <inheritdoc />
    public void ReservedCredits(long credits) => _creditReserve.Set(credits);

    /// <inheritdoc />
    public void Fleet(IReadOnlyCollection<ShipMetricsSample> ships, DateTimeOffset now)
    {
        lock (_lock)
        {
            foreach (var ship in ships)
            {
                string[] labels = [ship.Ship, ship.Role, ship.State, ship.Goal, ship.Reason];
                if (_shipLabels.TryGetValue(ship.Ship, out var previous))
                {
                    if (previous.SequenceEqual(labels, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    _shipStatusSince.RemoveLabelled(previous);
                }

                _shipStatusSince.WithLabels(labels).Set(now.ToUnixTimeSeconds());
                _shipLabels[ship.Ship] = labels;
            }

            var current = ships.Select(s => s.Ship).ToHashSet(StringComparer.Ordinal);
            foreach (var gone in _shipLabels.Keys.Where(symbol => !current.Contains(symbol)).ToList())
            {
                _shipStatusSince.RemoveLabelled(_shipLabels[gone]);
                _shipLabels.Remove(gone);
            }

            foreach (var ship in ships)
            {
                Details(ship);
            }

            foreach (var gone in _shipInfoLabels.Keys.Where(symbol => !current.Contains(symbol)).ToList())
            {
                ForgetDetails(gone);
            }

            // Counts stay at 0 once a role and state no longer occur, so a graph goes down to 0.
            var counts = ships.CountBy(s => (s.Role, s.State)).ToDictionary(c => c.Key, c => c.Value);
            _shipCountLabels.UnionWith(counts.Keys);
            foreach (var (role, state) in _shipCountLabels)
            {
                _ships.WithLabels(role, state).Set(counts.GetValueOrDefault((role, state)));
            }
        }
    }

    /// <inheritdoc />
    public void Contracts(IReadOnlyCollection<ContractMetricsSample> deliverables)
    {
        lock (_lock)
        {
            foreach (var deliverable in deliverables)
            {
                _contractUnitsRequired.WithLabels(deliverable.Contract, deliverable.TradeSymbol).Set(deliverable.UnitsRequired);
                _contractUnitsFulfilled.WithLabels(deliverable.Contract, deliverable.TradeSymbol).Set(deliverable.UnitsFulfilled);
                if (deliverable.Deadline != default)
                {
                    _contractDeadline.WithLabels(deliverable.Contract).Set(deliverable.Deadline.ToUnixTimeSeconds());
                }
            }

            var currentDeliverables = deliverables.Select(d => (d.Contract, d.TradeSymbol)).ToHashSet();
            foreach (var gone in _deliverables.Where(d => !currentDeliverables.Contains(d)).ToList())
            {
                _contractUnitsRequired.RemoveLabelled(gone.Contract, gone.TradeSymbol);
                _contractUnitsFulfilled.RemoveLabelled(gone.Contract, gone.TradeSymbol);
                _deliverables.Remove(gone);
            }

            _deliverables.UnionWith(currentDeliverables);

            var currentContracts = deliverables.Where(d => d.Deadline != default).Select(d => d.Contract).ToHashSet(StringComparer.Ordinal);
            foreach (var gone in _contracts.Where(c => !currentContracts.Contains(c)).ToList())
            {
                _contractDeadline.RemoveLabelled(gone);
                _contracts.Remove(gone);
            }

            _contracts.UnionWith(currentContracts);
        }
    }

    /// <inheritdoc />
    public void Construction(IReadOnlyCollection<ConstructionMetricsSample> materials)
    {
        lock (_lock)
        {
            foreach (var material in materials)
            {
                _constructionUnitsRequired.WithLabels(material.Site, material.TradeSymbol).Set(material.UnitsRequired);
                _constructionUnitsFulfilled.WithLabels(material.Site, material.TradeSymbol).Set(material.UnitsFulfilled);
            }

            var current = materials.Select(material => (material.Site, material.TradeSymbol)).ToHashSet();
            foreach (var gone in _constructionMaterials.Where(material => !current.Contains(material)).ToList())
            {
                _constructionUnitsRequired.RemoveLabelled(gone.Site, gone.TradeSymbol);
                _constructionUnitsFulfilled.RemoveLabelled(gone.Site, gone.TradeSymbol);
                _constructionMaterials.Remove(gone);
            }

            _constructionMaterials.UnionWith(current);
        }
    }

    /// <inheritdoc />
    public void Markets(IReadOnlyCollection<MarketMetricsSample> markets)
    {
        lock (_lock)
        {
            var currentMarkets = new HashSet<(string System, string Waypoint, string WaypointType)>();
            var currentGoods = new HashSet<(string System, string Waypoint, string Good, string Kind)>();
            foreach (var market in markets)
            {
                var key = (market.System, market.Waypoint, market.WaypointType);
                _marketObserved.WithLabels(key.System, key.Waypoint, key.WaypointType).Set(market.ObservedAt.ToUnixTimeSeconds());
                currentMarkets.Add(key);

                foreach (var good in market.Goods)
                {
                    var goodKey = (market.System, market.Waypoint, good.Symbol, good.Type);
                    string[] labels = [goodKey.System, goodKey.Waypoint, goodKey.Symbol, goodKey.Type];
                    _marketPurchasePrice.WithLabels(labels).Set(good.PurchasePrice);
                    _marketSellPrice.WithLabels(labels).Set(good.SellPrice);
                    _marketTradeVolume.WithLabels(labels).Set(good.TradeVolume);
                    SetOrRemove(_marketSupply, labels, SupplyLevel(good.Supply));
                    SetOrRemove(_marketActivity, labels, ActivityLevel(good.Activity));
                    currentGoods.Add(goodKey);
                }
            }

            foreach (var gone in _markets.Where(market => !currentMarkets.Contains(market)).ToList())
            {
                _marketObserved.RemoveLabelled(gone.System, gone.Waypoint, gone.WaypointType);
                _markets.Remove(gone);
            }

            foreach (var gone in _marketGoods.Where(good => !currentGoods.Contains(good)).ToList())
            {
                string[] labels = [gone.System, gone.Waypoint, gone.Good, gone.Kind];
                _marketPurchasePrice.RemoveLabelled(labels);
                _marketSellPrice.RemoveLabelled(labels);
                _marketTradeVolume.RemoveLabelled(labels);
                _marketSupply.RemoveLabelled(labels);
                _marketActivity.RemoveLabelled(labels);
                _marketGoods.Remove(gone);
            }

            _markets.UnionWith(currentMarkets);
            _marketGoods.UnionWith(currentGoods);
        }
    }

    /// <inheritdoc />
    public void Shipyards(IReadOnlyCollection<ShipyardMetricsSample> shipyards)
    {
        lock (_lock)
        {
            var currentShipyards = new HashSet<(string System, string Waypoint, string WaypointType)>();
            var currentShips = new HashSet<(string System, string Waypoint, string ShipType)>();
            foreach (var shipyard in shipyards)
            {
                var key = (shipyard.System, shipyard.Waypoint, shipyard.WaypointType);
                _shipyardObserved.WithLabels(key.System, key.Waypoint, key.WaypointType).Set(shipyard.ObservedAt.ToUnixTimeSeconds());
                currentShipyards.Add(key);

                var priced = shipyard.Ships
                    .GroupBy(ship => ship.Type, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                foreach (var shipType in shipyard.ShipTypes.Concat(priced.Keys).Distinct(StringComparer.Ordinal))
                {
                    var shipKey = (shipyard.System, shipyard.Waypoint, shipType);
                    string[] labels = [shipyard.System, shipyard.Waypoint, shipType];
                    _shipyardShipType.WithLabels(labels).Set(1);
                    if (priced.TryGetValue(shipType, out var ship))
                    {
                        _shipyardShipPrice.WithLabels(labels).Set(ship.PurchasePrice);
                        SetOrRemove(_shipyardShipSupply, labels, SupplyLevel(ship.Supply));
                        _shipyardShipFuelCapacity.WithLabels(labels).Set(ship.FuelCapacity);
                        _shipyardShipCargoCapacity.WithLabels(labels).Set(ship.CargoCapacity);
                        ShipyardShipInfo(shipKey, (ship.Can, ship.Equipment));
                    }
                    else
                    {
                        ForgetShipyardShipDetails(shipKey);
                    }

                    currentShips.Add(shipKey);
                }
            }

            foreach (var gone in _shipyards.Where(shipyard => !currentShipyards.Contains(shipyard)).ToList())
            {
                _shipyardObserved.RemoveLabelled(gone.System, gone.Waypoint, gone.WaypointType);
                _shipyards.Remove(gone);
            }

            foreach (var gone in _shipyardShips.Where(ship => !currentShips.Contains(ship)).ToList())
            {
                _shipyardShipType.RemoveLabelled(gone.System, gone.Waypoint, gone.ShipType);
                ForgetShipyardShipDetails(gone);
                _shipyardShips.Remove(gone);
            }

            _shipyards.UnionWith(currentShipyards);
            _shipyardShips.UnionWith(currentShips);
        }
    }

    /// <summary>
    /// A ship type's one info series at a shipyard (what it could do, its equipment), which replaces the one it had.
    /// Under the lock.
    /// </summary>
    private void ShipyardShipInfo((string System, string Waypoint, string ShipType) ship, (string Can, string Equipment) info)
    {
        if (_shipyardShipInfoLabels.TryGetValue(ship, out var previous) && previous != info)
        {
            _shipyardShipInfo.RemoveLabelled(ship.System, ship.Waypoint, ship.ShipType, previous.Can, previous.Equipment);
        }

        _shipyardShipInfo.WithLabels(ship.System, ship.Waypoint, ship.ShipType, info.Can, info.Equipment).Set(1);
        _shipyardShipInfoLabels[ship] = info;
    }

    /// <summary>
    /// Removes what a shipyard listed of a ship type beyond the type: its price, supply, tank, hold and info. Under the
    /// lock.
    /// </summary>
    private void ForgetShipyardShipDetails((string System, string Waypoint, string ShipType) ship)
    {
        string[] labels = [ship.System, ship.Waypoint, ship.ShipType];
        _shipyardShipPrice.RemoveLabelled(labels);
        _shipyardShipSupply.RemoveLabelled(labels);
        _shipyardShipFuelCapacity.RemoveLabelled(labels);
        _shipyardShipCargoCapacity.RemoveLabelled(labels);
        if (_shipyardShipInfoLabels.Remove(ship, out var info))
        {
            _shipyardShipInfo.RemoveLabelled(ship.System, ship.Waypoint, ship.ShipType, info.Can, info.Equipment);
        }
    }

    /// <inheritdoc />
    public void Systems(IReadOnlyCollection<SpaceTraders.Application.Exploring.SystemSample> systems)
    {
        lock (_lock)
        {
            SampledGauge[] all =
            [
                _systemInfo, _systemJumps, _systemExplored, _systemConnection, _systemFacilities, _systemWaypoints,
                _systemGatheringSites, _systemRawGoodPrice, _systemRawGoodSupply, _systemTradeMargin, _systemTradeVolume,
            ];
            foreach (var gauge in all)
            {
                gauge.Begin();
            }

            foreach (var system in systems)
            {
                _systemInfo.Set(1, system.System, system.State, system.Gate, system.GateState);
                if (system.Jumps is { } jumps)
                {
                    _systemJumps.Set(jumps, system.System);
                }

                if (system.ExploredAt is { } explored)
                {
                    _systemExplored.Set(explored.ToUnixTimeSeconds(), system.System);
                }

                foreach (var to in system.Connections)
                {
                    _systemConnection.Set(1, system.System, to);
                }

                _systemFacilities.Set(system.Markets, system.System, "market");
                _systemFacilities.Set(system.Shipyards, system.System, "shipyard");
                _systemFacilities.Set(system.Uncharted, system.System, "uncharted");
                foreach (var (type, count) in system.WaypointTypes)
                {
                    _systemWaypoints.Set(count, system.System, type);
                }

                foreach (var (good, sites) in system.GatheringSites)
                {
                    _systemGatheringSites.Set(sites, system.System, good);
                }

                foreach (var raw in system.RawGoods)
                {
                    _systemRawGoodPrice.Set(raw.Price, system.System, raw.Good, raw.Market);
                    if (SupplyLevel(raw.Supply) is { } supply)
                    {
                        _systemRawGoodSupply.Set(supply, system.System, raw.Good);
                    }
                }

                foreach (var trade in system.Trades)
                {
                    _systemTradeMargin.Set(trade.Margin, system.System, trade.Good, trade.BuyAt, trade.SellAt);
                    _systemTradeVolume.Set(trade.Volume, system.System, trade.Good, trade.BuyAt, trade.SellAt);
                }
            }

            foreach (var gauge in all)
            {
                gauge.End();
            }
        }
    }

    /// <inheritdoc />
    public void SupplyChain(IReadOnlyDictionary<string, IReadOnlyList<string>> madeFrom)
    {
        var goods = madeFrom.Keys.Concat(madeFrom.Values.SelectMany(inputs => inputs)).Distinct(StringComparer.Ordinal);
        var current = goods
            .Select(good => (
                Good: good,
                MadeFrom: string.Join(", ", madeFrom.GetValueOrDefault(good, []).Order(StringComparer.Ordinal)),
                UsedFor: string.Join(", ", madeFrom.Where(chain => chain.Value.Contains(good, StringComparer.Ordinal)).Select(chain => chain.Key).Order(StringComparer.Ordinal))))
            .ToHashSet();

        lock (_lock)
        {
            foreach (var gone in _supplyChainLabels.Where(series => !current.Contains(series)).ToList())
            {
                _supplyChain.RemoveLabelled(gone.Good, gone.MadeFrom, gone.UsedFor);
                _supplyChainLabels.Remove(gone);
            }

            foreach (var series in current)
            {
                _supplyChain.WithLabels(series.Good, series.MadeFrom, series.UsedFor).Set(1);
                _supplyChainLabels.Add(series);
            }
        }
    }

    /// <inheritdoc />
    public void Settings(IReadOnlyCollection<SettingMetricsSample> settings)
    {
        var current = settings
            .GroupBy(sample => sample.Setting, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (group.First().Value, group.First().Description), StringComparer.Ordinal);

        lock (_lock)
        {
            foreach (var (setting, labels) in _settingLabels.Where(series => current.GetValueOrDefault(series.Key) != series.Value).ToList())
            {
                _settingInfo.RemoveLabelled(setting, labels.Value, labels.Description);
                _settingLabels.Remove(setting);
            }

            foreach (var (setting, labels) in current)
            {
                _settingInfo.WithLabels(setting, labels.Value, labels.Description).Set(1);
                _settingLabels[setting] = labels;
            }
        }
    }

    /// <inheritdoc />
    public void Roles(IReadOnlyCollection<RoleMetricsSample> roles)
    {
        var current = roles
            .GroupBy(sample => sample.Ship, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var estimates = current.Values
            .SelectMany(sample => sample.CreditsPerHour.Select(estimate => (sample.Ship, Role: estimate.Key, estimate.Value)))
            .ToList();

        lock (_lock)
        {
            foreach (var (ship, labels) in _roleLabels
                .Where(series => !current.TryGetValue(series.Key, out var sample) || (sample.Role, sample.Reason) != series.Value)
                .ToList())
            {
                _roleInfo.RemoveLabelled(ship, labels.Role, labels.Reason);
                _roleLabels.Remove(ship);
            }

            foreach (var sample in current.Values)
            {
                _roleInfo.WithLabels(sample.Ship, sample.Role, sample.Reason).Set(1);
                _roleLabels[sample.Ship] = (sample.Role, sample.Reason);
            }

            var now = estimates.Select(estimate => (estimate.Ship, estimate.Role)).ToHashSet();
            foreach (var gone in _roleEstimates.Where(series => !now.Contains(series)).ToList())
            {
                _roleCreditsPerHour.RemoveLabelled(gone.Ship, gone.Role);
                _roleEstimates.Remove(gone);
            }

            foreach (var (ship, role, value) in estimates)
            {
                _roleCreditsPerHour.WithLabels(ship, role).Set(value);
                _roleEstimates.Add((ship, role));
            }
        }
    }

    /// <inheritdoc />
    public void PurchaseNeeds(IReadOnlyCollection<PurchaseNeedMetricsSample> needs)
    {
        var current = needs
            .GroupBy(sample => sample.Plan, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);

        lock (_lock)
        {
            foreach (var (plan, labels) in _purchaseNeedLabels
                .Where(series => !current.TryGetValue(series.Key, out var sample) || !Labels(sample).SequenceEqual(series.Value))
                .ToList())
            {
                _purchaseNeed.RemoveLabelled(labels);
                _purchaseNeedLabels.Remove(plan);
            }

            foreach (var sample in current.Values)
            {
                var labels = Labels(sample);
                _purchaseNeed.WithLabels(labels).Set(sample.Price);
                _purchaseNeedLabels[sample.Plan] = labels;
            }
        }

        static string[] Labels(PurchaseNeedMetricsSample sample)
            => [sample.Plan, sample.Tier, sample.Position.ToString(System.Globalization.CultureInfo.InvariantCulture), sample.ShipType, sample.Shipyard];
    }

    /// <summary>A yes-or-no label: <c>true</c> or <c>false</c>.</summary>
    private static string Flag(bool value) => value ? "true" : "false";

    /// <summary>The system a waypoint is in: its symbol up to the last dash, such as <c>X1-DC53</c> for <c>X1-DC53-H51</c>.</summary>
    private static string SystemOf(string waypointSymbol)
    {
        var lastDash = waypointSymbol.LastIndexOf('-');
        return lastDash > 0 ? waypointSymbol[..lastDash] : waypointSymbol;
    }

    /// <summary>A supply level as a number, so a graph can show it: 1 SCARCE to 5 ABUNDANT; none when unknown.</summary>
    private static double? SupplyLevel(string supply) => supply.ToUpperInvariant() switch
    {
        "SCARCE" => 1,
        "LIMITED" => 2,
        "MODERATE" => 3,
        "HIGH" => 4,
        "ABUNDANT" => 5,
        _ => null,
    };

    /// <summary>An activity as a number: 0 RESTRICTED, 1 WEAK, 2 GROWING, 3 STRONG; none when unknown.</summary>
    private static double? ActivityLevel(string activity) => activity.ToUpperInvariant() switch
    {
        "RESTRICTED" => 0,
        "WEAK" => 1,
        "GROWING" => 2,
        "STRONG" => 3,
        _ => null,
    };

    private static void SetOrRemove(Gauge gauge, string[] labels, double? value)
    {
        if (value is { } known)
        {
            gauge.WithLabels(labels).Set(known);
        }
        else
        {
            gauge.RemoveLabelled(labels);
        }
    }

    /// <summary>
    /// Where a ship is and what it does (one series), what it can do (one series), when it arrives, and its hold. Under
    /// the lock.
    /// </summary>
    private void Details(ShipMetricsSample ship)
    {
        var info = (ship.Location, ship.Activity);
        if (_shipInfoLabels.TryGetValue(ship.Ship, out var previous) && previous != info)
        {
            _shipInfo.RemoveLabelled(ship.Ship, previous.Location, previous.Activity);
        }

        _shipInfo.WithLabels(ship.Ship, ship.Location, ship.Activity).Set(1);
        _shipInfoLabels[ship.Ship] = info;

        if (_shipCapabilityLabels.TryGetValue(ship.Ship, out var could) && !string.Equals(could, ship.Capabilities, StringComparison.Ordinal))
        {
            _shipCapabilities.RemoveLabelled(ship.Ship, could);
        }

        _shipCapabilities.WithLabels(ship.Ship, ship.Capabilities).Set(1);
        _shipCapabilityLabels[ship.Ship] = ship.Capabilities;

        if (ship.ArrivesAt != default)
        {
            _shipArrival.WithLabels(ship.Ship).Set(ship.ArrivesAt.ToUnixTimeSeconds());
            _shipsInTransit.Add(ship.Ship);
        }
        else if (_shipsInTransit.Remove(ship.Ship))
        {
            _shipArrival.RemoveLabelled(ship.Ship);
        }

        var goods = ship.Cargo
            .Where(item => item.Units > 0)
            .GroupBy(item => item.Symbol, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Units), StringComparer.Ordinal);
        if (_shipGoods.TryGetValue(ship.Ship, out var aboard))
        {
            foreach (var gone in aboard.Where(good => !goods.ContainsKey(good)))
            {
                _shipCargoUnits.RemoveLabelled(ship.Ship, gone);
            }
        }

        foreach (var (good, units) in goods)
        {
            _shipCargoUnits.WithLabels(ship.Ship, good).Set(units);
        }

        _shipGoods[ship.Ship] = new HashSet<string>(goods.Keys, StringComparer.Ordinal);
        _shipCargoCapacity.WithLabels(ship.Ship).Set(ship.CargoCapacity);
        _shipValue.WithLabels(ship.Ship).Set(ship.Value);
    }

    /// <summary>Removes a ship's details once it is gone. Under the lock.</summary>
    private void ForgetDetails(string ship)
    {
        if (_shipInfoLabels.Remove(ship, out var info))
        {
            _shipInfo.RemoveLabelled(ship, info.Location, info.Activity);
            _shipCargoCapacity.RemoveLabelled(ship);
            _shipValue.RemoveLabelled(ship);
        }

        if (_shipCapabilityLabels.Remove(ship, out var can))
        {
            _shipCapabilities.RemoveLabelled(ship, can);
        }

        if (_shipsInTransit.Remove(ship))
        {
            _shipArrival.RemoveLabelled(ship);
        }

        if (_shipGoods.Remove(ship, out var goods))
        {
            foreach (var good in goods)
            {
                _shipCargoUnits.RemoveLabelled(ship, good);
            }
        }
    }

    /// <summary>
    /// A gauge whose series are all set in one sample: <see cref="Begin"/>, then <see cref="Set"/> each, then <see cref="End"/>
    /// removes the series the sample didn't set. Under the lock.
    /// </summary>
    private sealed class SampledGauge(Gauge gauge)
    {
        private readonly Dictionary<string, string[]> _series = new(StringComparer.Ordinal);
        private readonly HashSet<string> _set = new(StringComparer.Ordinal);

        public void Begin() => _set.Clear();

        public void Set(double value, params string[] labels)
        {
            var key = string.Join('\u001f', labels);
            gauge.WithLabels(labels).Set(value);
            _series[key] = labels;
            _set.Add(key);
        }

        public void End()
        {
            foreach (var (key, labels) in _series.Where(series => !_set.Contains(series.Key)).ToList())
            {
                gauge.RemoveLabelled(labels);
                _series.Remove(key);
            }
        }
    }
}
