using System.Collections.Concurrent;

namespace SpaceTraders.Application.Goals;

/// <summary>
/// Lets one goal step at a time run for a ship (B46). The tick steps every ship every 5 seconds, and an
/// arrival steps the ship it docks; both could act on one ship at once (B45 was the scout plan's case),
/// and a trade step would then buy or sell twice.
/// </summary>
public interface IShipGoalStepGuard
{
    /// <summary>Claims the ship for one step.</summary>
    /// <param name="shipSymbol">The ship.</param>
    /// <returns>False when another step for the ship is running.</returns>
    bool TryEnter(string shipSymbol);

    /// <summary>Releases the ship after its step.</summary>
    /// <param name="shipSymbol">The ship.</param>
    void Exit(string shipSymbol);
}

/// <inheritdoc />
/// <remarks>
/// A singleton, in memory: the bot runs as one pod. It doesn't wait: a step that finds its ship busy
/// is skipped, and the next tick, at most 5 seconds later, takes the ship's next step.
/// </remarks>
public sealed class ShipGoalStepGuard : IShipGoalStepGuard
{
    private readonly ConcurrentDictionary<string, byte> _running = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool TryEnter(string shipSymbol) => _running.TryAdd(shipSymbol, 0);

    /// <inheritdoc />
    public void Exit(string shipSymbol) => _running.TryRemove(shipSymbol, out _);
}
