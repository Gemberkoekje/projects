using Prometheus;
using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.API.Services;

/// <summary>
/// Exports <see cref="IAutomationMetrics"/> to Prometheus. It defines every <c>spacetraders_*</c>
/// metric when it is created, so a scrape lists them all, also before they have a value. Every
/// counter series reaches Prometheus at 0 before it counts (<see cref="ZeroFirstCounter"/>, B43).
/// </summary>
/// <remarks>Thread-safe: the per-ship and per-contract series it tracks are guarded by a lock.</remarks>
public sealed class PrometheusAutomationMetrics : IAutomationMetrics
{
    private readonly ZeroFirstCounter _goalBreakerTrips;
    private readonly Gauge _databaseSizeBytes;
    private readonly ZeroFirstCounter _goalSteps;
    private readonly ZeroFirstCounter _apiRequests;
    private readonly ZeroFirstCounter _apiThrottled;
    private readonly ZeroFirstCounter _rateLimitWaitSeconds;
    private readonly ZeroFirstCounter _messagesHandled;
    private readonly ZeroFirstCounter _creditsEarned;
    private readonly ZeroFirstCounter _creditsSpent;
    private readonly Gauge _anomalyActive;
    private readonly Gauge _nextServerReset;
    private readonly Gauge _credits;
    private readonly Gauge _ships;
    private readonly Gauge _shipStatusSince;
    private readonly Gauge _contractUnitsRequired;
    private readonly Gauge _contractUnitsFulfilled;
    private readonly Gauge _contractDeadline;
    private readonly ZeroFirstCounter _extractedUnits;
    private readonly ZeroFirstCounter _jettisonedUnits;
    private readonly Gauge _shipInfo;
    private readonly Gauge _shipArrival;
    private readonly Gauge _shipCargoUnits;
    private readonly Gauge _shipCargoCapacity;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, string[]> _shipLabels = new(StringComparer.Ordinal);
    private readonly HashSet<(string Role, string State)> _shipCountLabels = [];
    private readonly HashSet<(string Contract, string TradeSymbol)> _deliverables = [];
    private readonly HashSet<string> _contracts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Location, string Activity)> _shipInfoLabels = new(StringComparer.Ordinal);
    private readonly HashSet<string> _shipsInTransit = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _shipGoods = new(StringComparer.Ordinal);

    /// <summary>Defines the metrics in <paramref name="registry"/> (the default registry in the host).</summary>
    public PrometheusAutomationMetrics(CollectorRegistry registry)
    {
        var metrics = Metrics.WithCustomRegistry(registry);

        _goalBreakerTrips = ZeroFirst(
            "spacetraders_goal_breaker_trips_total",
            "Goals blocked by the per-ship circuit breaker for taking too many steps in a minute.",
            "ship");
        _databaseSizeBytes = metrics.CreateGauge(
            "spacetraders_db_size_bytes",
            "Size of the bot's Postgres database (pg_database_size), read every 5 minutes.");
        _goalSteps = ZeroFirst(
            "spacetraders_goal_steps_total",
            "Goal steps run, by goal kind.",
            "kind");
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
            "Seconds that requests waited for the local request budget (2 per second plus a burst of 30 per minute).");
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
        _anomalyActive = metrics.CreateGauge(
            "spacetraders_anomaly_active",
            "1 while an anomaly is active, 0 once it cleared.",
            "rule",
            "subject");
        _nextServerReset = metrics.CreateGauge(
            "spacetraders_server_next_reset_timestamp_seconds",
            "When the SpaceTraders server resets next (Unix time), as the server said at startup.");
        _credits = metrics.CreateGauge(
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
        _extractedUnits = ZeroFirst(
            "spacetraders_extracted_units_total",
            "Units ships extracted, by ship and good: each extraction's yield, before what isn't wanted is jettisoned.",
            "ship",
            "good");
        _jettisonedUnits = ZeroFirst(
            "spacetraders_jettisoned_units_total",
            "Units ships jettisoned, by ship and good: extracted goods their work doesn't need.",
            "ship",
            "good");
        _shipInfo = metrics.CreateGauge(
            "spacetraders_ship_info",
            "One series per ship, always 1: where it is (its waypoint and the waypoint's type; in transit, an arrow and where it goes) and what the bot has it do.",
            "ship",
            "location",
            "activity");
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

        // Counters reach Prometheus at 0 first, so increase() and rate() see their first increment (B43).
        ZeroFirstCounter ZeroFirst(string name, string help, params string[] labelNames)
            => new(metrics.CreateCounter(name, help, labelNames), registry);
    }

    /// <inheritdoc />
    public void GoalBreakerTripped(string shipSymbol) => _goalBreakerTrips.Inc(1, shipSymbol);

    /// <inheritdoc />
    public void DatabaseSize(long bytes) => _databaseSizeBytes.Set(bytes);

    /// <inheritdoc />
    public void GoalStep(string goalKind) => _goalSteps.Inc(1, goalKind);

    /// <inheritdoc />
    public void ApiRequest(string method, string endpoint, string status) => _apiRequests.Inc(1, method, endpoint, status);

    /// <inheritdoc />
    public void ApiThrottled(string source) => _apiThrottled.Inc(1, source);

    /// <inheritdoc />
    public void RateLimitWait(TimeSpan wait) => _rateLimitWaitSeconds.Inc(wait.TotalSeconds);

    /// <inheritdoc />
    public void MessageHandled(string messageType) => _messagesHandled.Inc(1, messageType);

    /// <inheritdoc />
    public void CreditsEarned(string source, long amount) => _creditsEarned.Inc(amount, source);

    /// <inheritdoc />
    public void CreditsSpent(string category, long amount) => _creditsSpent.Inc(amount, category);

    /// <inheritdoc />
    public void Extracted(string shipSymbol, string tradeSymbol, int units) => _extractedUnits.Inc(units, shipSymbol, tradeSymbol);

    /// <inheritdoc />
    public void Jettisoned(string shipSymbol, string tradeSymbol, int units) => _jettisonedUnits.Inc(units, shipSymbol, tradeSymbol);

    /// <inheritdoc />
    public void Anomaly(string rule, string subject, bool active) => _anomalyActive.WithLabels(rule, subject).Set(active ? 1 : 0);

    /// <inheritdoc />
    public void NextServerReset(DateTimeOffset next) => _nextServerReset.Set(next.ToUnixTimeSeconds());

    /// <inheritdoc />
    public void Credits(long credits) => _credits.Set(credits);

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

    /// <summary>Where a ship is and what it does (one series), when it arrives, and its hold. Under the lock.</summary>
    private void Details(ShipMetricsSample ship)
    {
        var info = (ship.Location, ship.Activity);
        if (_shipInfoLabels.TryGetValue(ship.Ship, out var previous) && previous != info)
        {
            _shipInfo.RemoveLabelled(ship.Ship, previous.Location, previous.Activity);
        }

        _shipInfo.WithLabels(ship.Ship, ship.Location, ship.Activity).Set(1);
        _shipInfoLabels[ship.Ship] = info;

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
    }

    /// <summary>Removes a ship's details once it is gone. Under the lock.</summary>
    private void ForgetDetails(string ship)
    {
        if (_shipInfoLabels.Remove(ship, out var info))
        {
            _shipInfo.RemoveLabelled(ship, info.Location, info.Activity);
            _shipCargoCapacity.RemoveLabelled(ship);
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
}
