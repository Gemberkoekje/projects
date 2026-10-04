namespace SpaceTraders.Application.Exploring;

/// <summary>What the explore plan is doing with the command ship.</summary>
public enum ExploreStatus
{
    /// <summary>The command ship has other work; the plan takes it when its trip ends and there is a system to explore.</summary>
    Waiting = 0,

    /// <summary>The command ship is on its way to a system not explored yet, or scouting one.</summary>
    Exploring = 1,

    /// <summary>Nothing reachable is left to explore: the command ship is on its way home.</summary>
    Returning = 2,

    /// <summary>Every system the active gates reach is explored, and the command ship is home with its other work.</summary>
    Done = 3,
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

/// <summary>
/// The explore plan (asked on 2026-10-04): the systems it knows, what it knows of their gates, and which it has explored.
/// One row in <c>plan_states</c>.
/// </summary>
public sealed record ExplorePlanState
{
    /// <summary>The command ship, which explores.</summary>
    public required string ShipSymbol { get; init; }

    /// <summary>The headquarters' system, where the command ship comes home to.</summary>
    public required string HomeSystemSymbol { get; init; }

    /// <summary>What the plan is doing with the command ship.</summary>
    public required ExploreStatus Status { get; init; }

    /// <summary>The system the command ship is exploring or on its way to; empty when none.</summary>
    public string TargetSystemSymbol { get; init; } = string.Empty;

    /// <summary>Why the plan waits, in a word for the journal (<c>waiting_for_credits</c>, <c>no_way_home</c>); empty when it doesn't.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>The systems it knows: home, and every system a known gate connects to.</summary>
    public IReadOnlyList<KnownSystem> Systems { get; init; } = [];

    /// <summary>When the state last changed.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
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
    /// When its waypoints were last asked for, once the command ship was there and none were cached, whatever the answer;
    /// null when they never were.
    /// </summary>
    public DateTimeOffset? WaypointsCheckedAt { get; init; }

    /// <summary>
    /// When the command ship was given its markets and shipyards to scout; null before. Once that goal has ended the system
    /// counts as explored, even where a market or shipyard couldn't be fetched, so the ship doesn't go back for it forever.
    /// </summary>
    public DateTimeOffset? ScoutingSince { get; init; }

    /// <summary>When the command ship had visited each of its markets and shipyards; null while it hasn't.</summary>
    public DateTimeOffset? ExploredAt { get; init; }
}
