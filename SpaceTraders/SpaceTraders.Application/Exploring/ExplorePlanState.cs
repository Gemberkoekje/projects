using SpaceTraders.Application.Services;

namespace SpaceTraders.Application.Exploring;

/// <summary>What the explore plan is doing with a ship that explores: the command ship, or an explorer.</summary>
public enum ExploreStatus
{
    /// <summary>
    /// The ship has other work; the plan takes it when its trip ends and there is a system to explore. An explorer with no
    /// system left trades meanwhile (slice 6.30, D102).
    /// </summary>
    Waiting = 0,

    /// <summary>The ship is on its way to a system not explored yet, or scouting one.</summary>
    Exploring = 1,

    /// <summary>
    /// The command ship is on its way home: nothing reachable is left to explore, or an explorer explores now. An explorer with
    /// nothing left, in a system the gates don't reach from home, is on its way back to one they do (PLAN.md slice 6.31).
    /// </summary>
    Returning = 2,

    /// <summary>
    /// The command ship is home with its other work: every system the active gates reach is explored, or an explorer
    /// explores them (slice 6.30, D98).
    /// </summary>
    Done = 3,

    /// <summary>
    /// The command ship is on its way to the shipyard that sells the next explorer, or waits there until it is bought (slice
    /// 6.30, D98; every explorer since slice 6.32, D108).
    /// </summary>
    FetchingExplorer = 4,
}

/// <summary>What the plan knows about a system's jump gate.</summary>
public enum GateState
{
    /// <summary>Not looked at yet, or the last look failed.</summary>
    Unknown = 0,

    /// <summary>Built: ships can jump to and from it.</summary>
    Active = 1,

    /// <summary>Still under construction: no ship can jump to or from it until it is built.</summary>
    UnderConstruction = 2,

    /// <summary>The system has no jump gate.</summary>
    None = 3,
}

/// <summary>Where the next explorer's purchase stands (PLAN.md slice 6.30, D98, D102).</summary>
public enum ExplorerPurchaseStatus
{
    /// <summary>No explorer is wanted beyond those there are.</summary>
    None = 0,

    /// <summary>One was bought this pass.</summary>
    Bought = 1,

    /// <summary>Something earlier in the order ships are bought in comes first (D43).</summary>
    WaitingForAnotherPurchase = 2,

    /// <summary>It would leave less than the credit reserve; the credits are saved up for it.</summary>
    WaitingForCredits = 3,

    /// <summary>
    /// None of our ships is at the shipyard, and no probe of ours can answer the purchase's call: the command ship flies there
    /// to buy it (D30, D98; every explorer since slice 6.32, D108).
    /// </summary>
    CommandShipFetchesIt = 4,

    /// <summary>
    /// None of our ships is at the shipyard yet: a probe of ours in its system, or on its way there, answers the purchase's
    /// call (D30, slice 6.32, D108).
    /// </summary>
    WaitingForAShipThere = 5,

    /// <summary>No shipyard the gates reach is known to sell one.</summary>
    NoShipyardSellsOne = 6,
}

/// <summary>
/// The explore plan (asked on 2026-10-04): the systems it knows, what it knows of their gates, and which it has explored; what
/// it does with the command ship, and since slice 6.30 with each explorer, and how many explorers it wants. One row in
/// <c>plan_states</c>.
/// </summary>
public sealed record ExplorePlanState
{
    /// <summary>The command ship, which explores while there is no explorer.</summary>
    public required string ShipSymbol { get; init; }

    /// <summary>The headquarters' system, where the command ship comes home to.</summary>
    public required string HomeSystemSymbol { get; init; }

    /// <summary>What the plan is doing with the command ship.</summary>
    public required ExploreStatus Status { get; init; }

    /// <summary>The system the command ship is exploring or on its way to; empty when none.</summary>
    public string TargetSystemSymbol { get; init; } = string.Empty;

    /// <summary>Why the plan waits with the command ship, in a word for the journal (<c>waiting_for_credits</c>, <c>no_way_home</c>); empty when it doesn't.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>The systems it knows: home, and every system a known gate connects to.</summary>
    public IReadOnlyList<KnownSystem> Systems { get; init; } = [];

    /// <summary>The explorers (slice 6.30, D98), and what the plan does with each.</summary>
    public IReadOnlyList<ExploringShip> Explorers { get; init; } = [];

    /// <summary>
    /// The systems the plan knows, hasn't explored and reaches from home through built gates (slice 6.30, D102), as the last
    /// pass counted them.
    /// </summary>
    public int SystemsLeft { get; init; }

    /// <summary>
    /// The explorers it wants (D102): one for every <c>Explore.SystemsPerExplorer</c> systems left, or part of that, at most
    /// <c>Explore.MaxExplorers</c>.
    /// </summary>
    public int ExplorersWanted { get; init; }

    /// <summary>Where the next explorer's purchase stands, as the last pass left it.</summary>
    public ExplorerPurchaseState Purchase { get; init; } = new();

    /// <summary>When the state last changed.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>An explorer, as the explore plan sees it (PLAN.md slice 6.30).</summary>
public sealed record ExploringShip
{
    /// <summary>The explorer.</summary>
    public required string ShipSymbol { get; init; }

    /// <summary>
    /// What the plan does with it: <see cref="ExploreStatus.Exploring"/>, <see cref="ExploreStatus.Waiting"/> while it trades
    /// (D102), or <see cref="ExploreStatus.Returning"/> on its way back to a system the gates reach (slice 6.31).
    /// </summary>
    public required ExploreStatus Status { get; init; }

    /// <summary>The system it explores or is on its way to; empty when none.</summary>
    public string TargetSystemSymbol { get; init; } = string.Empty;

    /// <summary>Why the plan waits with it, in a word (<c>waiting_for_credits</c>, <c>nothing_to_explore</c>); empty when it doesn't.</summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>The next explorer's purchase, as a pass of the explore plan left it (PLAN.md slice 6.30).</summary>
public sealed record ExplorerPurchaseState
{
    /// <summary>Where it stands.</summary>
    public ExplorerPurchaseStatus Status { get; init; }

    /// <summary>Its place in the order ships are bought in: <see cref="PurchaseTier.Explorer"/> for the first, <see cref="PurchaseTier.MoreExplorers"/> after it.</summary>
    public PurchaseTier Tier { get; init; }

    /// <summary>The shipyard it would be bought at; empty when none.</summary>
    public string ShipyardWaypointSymbol { get; init; } = string.Empty;

    /// <summary>What the shipyard asks, as cached; 0 when unknown.</summary>
    public long Price { get; init; }
}

/// <summary>A system the explore plan knows: home, or one a known gate connects to.</summary>
public sealed record KnownSystem
{
    /// <summary>The system.</summary>
    public required string SystemSymbol { get; init; }

    /// <summary>Its jump gate's waypoint; empty while it isn't known, or when it has none.</summary>
    public string GateWaypointSymbol { get; init; } = string.Empty;

    /// <summary>What is known of its gate.</summary>
    public GateState Gate { get; init; }

    /// <summary>When its gate was last looked at, whatever the answer; null when it never was.</summary>
    public DateTimeOffset? GateCheckedAt { get; init; }

    /// <summary>The gates its gate connects to, by waypoint; null until they were asked for.</summary>
    public IReadOnlyList<string>? Connections { get; init; }

    /// <summary>When its connections were last asked for, whatever the answer; null when they never were.</summary>
    public DateTimeOffset? ConnectionsCheckedAt { get; init; }

    /// <summary>When the API last refused a jump to its gate; the gate is left alone for a while after that.</summary>
    public DateTimeOffset? JumpRefusedAt { get; init; }

    /// <summary>
    /// When its waypoints were last asked for, once an exploring ship was there and none were cached, whatever the answer;
    /// null when they never were.
    /// </summary>
    public DateTimeOffset? WaypointsCheckedAt { get; init; }

    /// <summary>
    /// When its system and waypoints were last fetched without an error (PLAN.md slice 6.31): a system only a warp reaches is
    /// fetched once, even when it has no waypoint to cache; null while that never happened.
    /// </summary>
    public DateTimeOffset? WaypointsFetchedAt { get; init; }

    /// <summary>
    /// When an exploring ship was given its markets and shipyards to scout, and its uncharted waypoints to chart; null before.
    /// Once that goal has ended the system counts as explored, even where a market or shipyard couldn't be fetched, so the ship
    /// doesn't go back for it forever.
    /// </summary>
    public DateTimeOffset? ScoutingSince { get; init; }

    /// <summary>When an exploring ship had visited each of its markets and shipyards; null while none has.</summary>
    public DateTimeOffset? ExploredAt { get; init; }

    /// <summary>
    /// When an explorer scanned for the systems around it from here (PLAN.md slice 6.31, D105), with nothing else left within its
    /// warps; null while none did. A system is scanned from once.
    /// </summary>
    public DateTimeOffset? ScannedAt { get; init; }

    /// <summary>When a scan from here was last tried, whatever the answer: one that failed is tried again after a few minutes.</summary>
    public DateTimeOffset? ScanTriedAt { get; init; }
}
