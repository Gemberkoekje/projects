using System.Collections.Concurrent;

namespace SpaceTraders.Application.Services;

/// <summary>
/// Shipyards where a purchase waits for one of our ships (D30). The API sells a ship only to an agent with
/// a ship at the shipyard, so when a plan can afford a ship at a shipyard where none of ours is,
/// <see cref="ShipPurchaseService"/> records a call here instead of making a call to the API that can only
/// fail. The probe plan answers it: the nearest free probe flies there and waits, and the plan's next
/// attempt buys the ship.
/// </summary>
/// <remarks>
/// In memory, a singleton. A plan that still wants the ship calls again on every tick, so a call lasts
/// <see cref="Lifetime"/> after the last one, or longer while the plan's next pass hasn't come (<see cref="GameTicks.Counts"/>,
/// B79): a plan that stops wanting it, because the credits went elsewhere, lets the probe go again. After a restart the
/// plans call again on their first tick.
/// </remarks>
/// <param name="ticks">When the game loop's ticks began.</param>
public sealed class ShipyardCalls(GameTicks ticks)
{
    /// <summary>How long a call stays open at least after the last time a plan made it.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, ShipyardCall> _calls = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the calls with no ticks known: a call stays open for <see cref="Lifetime"/>.</summary>
    public ShipyardCalls()
        : this(new GameTicks())
    {
    }

    /// <summary>Records that a purchase of <paramref name="shipType"/> waits at <paramref name="waypointSymbol"/>.</summary>
    /// <param name="waypointSymbol">The shipyard's waypoint.</param>
    /// <param name="shipType">The ship the plan wants to buy there.</param>
    /// <param name="at">When the plan asked.</param>
    public void Call(string waypointSymbol, string shipType, DateTimeOffset at)
        => _calls.AddOrUpdate(
            waypointSymbol,
            shipyard => new ShipyardCall(shipyard, shipType, at, at),
            (shipyard, open) => !ticks.Counts(open.LastCalledAt, at, Lifetime)
                ? new ShipyardCall(shipyard, shipType, at, at)
                : open with { ShipType = shipType, LastCalledAt = at });

    /// <summary>Closes the call at <paramref name="waypointSymbol"/>: the ship was bought.</summary>
    /// <param name="waypointSymbol">The shipyard's waypoint.</param>
    public void Answer(string waypointSymbol) => _calls.TryRemove(waypointSymbol, out _);

    /// <summary>The calls still open at <paramref name="now"/>, the oldest first.</summary>
    /// <param name="now">The time to judge by.</param>
    /// <returns>The open calls.</returns>
    public IReadOnlyList<ShipyardCall> Open(DateTimeOffset now)
        => [.. _calls.Values
            .Where(call => ticks.Counts(call.LastCalledAt, now, Lifetime))
            .OrderBy(call => call.Since)
            .ThenBy(call => call.WaypointSymbol, StringComparer.Ordinal)];
}

/// <summary>A purchase that waits for one of our ships at a shipyard.</summary>
public sealed record ShipyardCall
{
    /// <summary>Creates a call.</summary>
    /// <param name="WaypointSymbol">The shipyard's waypoint.</param>
    /// <param name="ShipType">The ship a plan wants to buy there.</param>
    /// <param name="Since">When the call was first made.</param>
    /// <param name="LastCalledAt">When a plan last made it.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipyardCall(string WaypointSymbol, string ShipType, DateTimeOffset Since, DateTimeOffset LastCalledAt)
    {
        this.WaypointSymbol = WaypointSymbol;
        this.ShipType = ShipType;
        this.Since = Since;
        this.LastCalledAt = LastCalledAt;
    }

    /// <summary>The shipyard's waypoint.</summary>
    public required string WaypointSymbol { get; init; }

    /// <summary>The ship a plan wants to buy there.</summary>
    public required string ShipType { get; init; }

    /// <summary>When the call was first made.</summary>
    public required DateTimeOffset Since { get; init; }

    /// <summary>When a plan last made it.</summary>
    public required DateTimeOffset LastCalledAt { get; init; }
}
