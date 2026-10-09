using System.Text.Json.Serialization;
using SpaceTraders.Application.Probes;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// The probe plan's view after its last pass (PLAN.md slice 6.3, D29, D30; per system since slice 6.28, D97): which probe
/// watches each market of each system it serves, what the next probe would cost and where, and the shipyards that call for a
/// ship. Written only when it changes.
/// </summary>
public sealed record ProbeDeploymentPlanState
{
    public required Guid PlanId { get; init; }

    /// <summary>The headquarters' system, whose markets come first.</summary>
    public required string SystemSymbol { get; init; }

    /// <summary>The probes, in every system.</summary>
    public required int Probes { get; init; }

    /// <summary>
    /// The systems the probes serve (slice 6.28, D97): home first, then each explored system the built gates reach, the
    /// nearest first; and any other system a probe of ours is in.
    /// </summary>
    public IReadOnlyList<ProbeSystemState> Systems { get; init; } = [];

    /// <summary>Whether a probe was bought in the pass, or why not.</summary>
    public ProbePurchaseStatus Purchase { get; init; }

    /// <summary>
    /// The system the next probe is for: the first short of one, its shipyards first (slice 6.32, D109); empty when none is.
    /// </summary>
    public string NextProbeSystem { get; init; } = string.Empty;

    /// <summary>
    /// What the next probe is for (slice 6.32, D109): a shipyard that sells SHIP_EXPLORER, another shipyard, or a market
    /// (<see cref="ShipyardKind.None"/>).
    /// </summary>
    public ShipyardKind NextProbeFor { get; init; }

    /// <summary>
    /// Where the next probe would be bought: the shipyard where it costs least, the antimatter to its system counted (D97); with
    /// a probe for every market, home's cheapest, as cached. Empty when none is known.
    /// </summary>
    public string NextProbeShipyard { get; init; } = string.Empty;

    /// <summary>
    /// The ship it would be: <c>SHIP_INTERCEPTOR</c> where a shipyard it could come from sells one, else <c>SHIP_PROBE</c>
    /// (slice 6.38, D119); empty when no shipyard is known.
    /// </summary>
    public string NextProbeShipType { get; init; } = string.Empty;

    /// <summary>What a probe costs there, as cached; 0 when no shipyard is known.</summary>
    public long NextProbePrice { get; init; }

    /// <summary>The antimatter of the jumps from that shipyard to the system the probe is for, as last seen at each gate.</summary>
    public long NextProbeAntimatter { get; init; }

    /// <summary>The shipyards where a purchase waits for one of our ships (D30), and the probe sent there.</summary>
    public IReadOnlyList<ProbeCallState> Calls { get; init; } = [];

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>One system in the probe plan's view (slice 6.28).</summary>
public sealed record ProbeSystemState
{
    /// <summary>The system.</summary>
    public required string SystemSymbol { get; init; }

    /// <summary>Whether a way through built gates from home is known now; home is reached.</summary>
    public bool Reached { get; init; }

    /// <summary>How many jumps from home it is, when <see cref="Reached"/>; 0 for home.</summary>
    public int Jumps { get; init; }

    /// <summary>
    /// Whether its probes come in the probe tier of the order ships are bought in: home's, and those of the systems within
    /// <c>Trade.MaxHaulDistance</c> jumps of home (D96, D97). The other systems' come last, after the drones and cargo ships.
    /// </summary>
    public bool InTradeReach { get; init; }

    /// <summary>The probes in the system, and those on their way there.</summary>
    public int Probes { get; init; }

    /// <summary>The system's markets, by symbol.</summary>
    public IReadOnlyList<ProbeMarketState> Markets { get; init; } = [];
}

/// <summary>One market in the probe plan's view.</summary>
public sealed record ProbeMarketState
{
    /// <summary>The market's waypoint.</summary>
    public required string WaypointSymbol { get; init; }

    /// <summary>Whether it is a shipyard, and one that sells SHIP_EXPLORER, where a probe parks first (slice 6.32, D109, D110).</summary>
    public ShipyardKind Shipyard { get; init; }

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

    /// <summary>Every market of every system the probes serve has one, or one on its way (D29, D97).</summary>
    EveryMarketHasOne = 1,

    /// <summary>
    /// No shipyard is known to sell probes, with a price, from where a probe can get to a system short of one: at home, or in
    /// a system with a probe of ours, which a purchase there needs (D30).
    /// </summary>
    NoShipyardSellsProbes = 2,

    /// <summary>A probe would leave less than the credit reserve (<c>FleetExpansion.MinCreditReserve</c>, D29).</summary>
    WaitingForCredits = 3,

    /// <summary>None of our ships is at the shipyard; a probe flies there (D30).</summary>
    WaitingForAShipAtTheShipyard = 4,

    /// <summary>A probe was bought.</summary>
    Bought = 5,

    /// <summary>
    /// A purchase that comes first in the order ships are bought in waits (D43): the contract's drone, a surveyor, a drone
    /// for a scarce mineral, or a cargo ship of <c>Trade.ShipPurchases</c>, which the credits are saved up for; and for a
    /// system beyond the trade reach, the drones and cargo ships that take turns too (D97).
    /// </summary>
    WaitingForAnotherPurchase = 6,

    /// <summary>Every shipyard a probe could be bought at has SHIP_PROBE at SCARCE supply: none is bought there (D97).</summary>
    ShipyardsScarce = 7,

    /// <summary>
    /// Where the next probe costs least can't sell one until a probe of ours, on its way there now, arrives (D30, D97, B72):
    /// it is bought there once that probe is.
    /// </summary>
    WaitingForAProbeToArrive = 8,
}
