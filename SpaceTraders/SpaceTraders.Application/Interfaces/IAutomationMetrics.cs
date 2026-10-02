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

    /// <summary>Adds the time a request waited for the local request budget (<c>spacetraders_api_rate_limit_wait_seconds_total</c>).</summary>
    void RateLimitWait(TimeSpan wait);

    /// <summary>Counts a message Wolverine handled without an error (<c>spacetraders_messages_handled_total{type}</c>).</summary>
    void MessageHandled(string messageType);

    /// <summary>Adds credits earned (<c>spacetraders_credits_earned_total{source}</c>), by ledger category.</summary>
    void CreditsEarned(string source, long amount);

    /// <summary>Adds credits spent (<c>spacetraders_credits_spent_total{category}</c>), by ledger category.</summary>
    void CreditsSpent(string category, long amount);

    /// <summary>Adds units a ship extracted (<c>spacetraders_extracted_units_total{ship,good}</c>).</summary>
    void Extracted(string shipSymbol, string tradeSymbol, int units);

    /// <summary>Adds units a ship jettisoned (<c>spacetraders_jettisoned_units_total{ship,good}</c>).</summary>
    void Jettisoned(string shipSymbol, string tradeSymbol, int units);

    /// <summary>Sets whether an anomaly is active (<c>spacetraders_anomaly_active{rule,subject}</c>).</summary>
    void Anomaly(string rule, string subject, bool active);

    /// <summary>Records when the server resets next (<c>spacetraders_server_next_reset_timestamp_seconds</c>).</summary>
    void NextServerReset(DateTimeOffset next);

    /// <summary>Records the agent's credits as cached (<c>spacetraders_agent_credits</c>).</summary>
    void Credits(long credits);

    /// <summary>
    /// Records every ship's state (<c>spacetraders_ships{role,state}</c> and
    /// <c>spacetraders_ship_status_since_timestamp_seconds{ship,role,state,goal,reason}</c>), where it
    /// is and what it does (<c>spacetraders_ship_info{ship,location,activity}</c>), when it arrives
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

    /// <summary>When it arrives, while in transit; <c>default</c> otherwise.</summary>
    public DateTimeOffset ArrivesAt { get; init; }

    /// <summary>The units its hold takes.</summary>
    public int CargoCapacity { get; init; }

    /// <summary>What its hold carries, per good.</summary>
    public IReadOnlyList<CargoItemModel> Cargo { get; init; } = [];
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
