namespace SpaceTraders.Application.Automation;

public static partial class PlanTypes
{
    public const string SpareTime = "SpareTime";
}

/// <summary>What a ship that gathers in its spare time does, in the spare-time plan's view (slice 6.8).</summary>
public enum SpareTimeActivity
{
    None = 0,

    /// <summary>On a spare-time trip, filling its hold at its source.</summary>
    Gathering = 1,

    /// <summary>On a spare-time trip, selling its hold.</summary>
    Selling = 2,

    /// <summary>Working for another plan: a survey, a trade or the contract.</summary>
    Busy = 3,

    /// <summary>Free, with nowhere it can gather and sell, or a full hold that no market it can reach buys.</summary>
    Waiting = 4,
}

/// <summary>
/// The spare-time plan's view after its last pass (PLAN.md slice 6.8): every ship that gathers in its spare time, and
/// what it does. Written only when it changes.
/// </summary>
public sealed record SpareTimePlanState
{
    public required Guid PlanId { get; init; }

    /// <summary>The ships that gather in their spare time, by symbol.</summary>
    public required IReadOnlyList<SpareTimeShipState> Ships { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>One ship in the spare-time plan's view.</summary>
public sealed record SpareTimeShipState
{
    public required string ShipSymbol { get; init; }

    public required SpareTimeActivity Activity { get; init; }

    /// <summary>
    /// Where it gathers: its trip's asteroid or gas giant. Empty for a ship that is busy or waits. The
    /// <c>ShipLeftIdle</c> rule reads it (D13): a ship with no goal while the plan lists a source for it is idle while
    /// work waits.
    /// </summary>
    public string SourceWaypointSymbol { get; init; } = string.Empty;
}
