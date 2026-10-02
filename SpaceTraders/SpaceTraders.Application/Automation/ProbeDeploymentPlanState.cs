using System.Text.Json.Serialization;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// The probe plan's view after its last pass (PLAN.md slice 6.3, D29, D30): which probe watches each market
/// of the headquarters' system, what the next probe would cost, and the shipyards that call for a ship.
/// Written only when it changes.
/// </summary>
public sealed record ProbeDeploymentPlanState
{
    public required Guid PlanId { get; init; }

    /// <summary>The headquarters' system, where the probes work.</summary>
    public required string SystemSymbol { get; init; }

    /// <summary>The probes in the system.</summary>
    public required int Probes { get; init; }

    /// <summary>The system's markets, by symbol.</summary>
    public required IReadOnlyList<ProbeMarketState> Markets { get; init; }

    /// <summary>Whether a probe was bought in the pass, or why not.</summary>
    public ProbePurchaseStatus Purchase { get; init; }

    /// <summary>The shipyard that sells probes for the least, as cached; empty when none is known.</summary>
    public string NextProbeShipyard { get; init; } = string.Empty;

    /// <summary>What a probe costs there, as cached; 0 when no shipyard is known.</summary>
    public long NextProbePrice { get; init; }

    /// <summary>The shipyards where a purchase waits for one of our ships (D30), and the probe sent there.</summary>
    public IReadOnlyList<ProbeCallState> Calls { get; init; } = [];

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>One market in the probe plan's view.</summary>
public sealed record ProbeMarketState
{
    /// <summary>The market's waypoint.</summary>
    public required string WaypointSymbol { get; init; }

    /// <summary>The probe at the market, or on its way there; empty when there is none.</summary>
    public string ProbeSymbol { get; init; } = string.Empty;

    /// <summary>Whether another of our ships is at the market, so that the market watch keeps it fresh.</summary>
    public bool WatchedByShip { get; init; }

    /// <summary>
    /// For a market without a probe or a ship: when its prices are due again (last seen plus the interval),
    /// <see cref="DateTimeOffset.MinValue"/> when they were never seen. Left at its default for a watched
    /// market, whose time changes with every refresh.
    /// </summary>
    public DateTimeOffset DueAt { get; init; }

    /// <summary>Whether no probe and no ship watches the market.</summary>
    [JsonIgnore]
    public bool IsUnwatched => ProbeSymbol.Length == 0 && !WatchedByShip;
}

/// <summary>A shipyard where a purchase waits for one of our ships (D30).</summary>
public sealed record ProbeCallState
{
    /// <summary>The shipyard's waypoint.</summary>
    public required string WaypointSymbol { get; init; }

    /// <summary>The ship the purchase waits to buy.</summary>
    public required string ShipType { get; init; }

    /// <summary>The probe at the shipyard or on its way there; empty while every probe is in flight.</summary>
    public string ProbeSymbol { get; init; } = string.Empty;
}

/// <summary>Why the probe plan did or didn't buy a probe in its last pass.</summary>
public enum ProbePurchaseStatus
{
    /// <summary>Not judged.</summary>
    None = 0,

    /// <summary>There are as many probes as markets (D29).</summary>
    EveryMarketHasOne = 1,

    /// <summary>No shipyard in the system is known to sell probes, with a price.</summary>
    NoShipyardSellsProbes = 2,

    /// <summary>A probe would leave less than the credit reserve (<c>FleetExpansion.MinCreditReserve</c>, D29).</summary>
    WaitingForCredits = 3,

    /// <summary>None of our ships is at the shipyard; a probe flies there (D30).</summary>
    WaitingForAShipAtTheShipyard = 4,

    /// <summary>A probe was bought.</summary>
    Bought = 5,
}
