using System.Collections.Concurrent;

namespace SpaceTraders.Application.Goals;

/// <summary>
/// Lets one goal step at a time run for a ship (B46), and keeps every other write of the ship's goal out of a step (PLAN.md
/// slice 6.62, D147). Ship steps run in their own loop beside the plans', and an arrival steps the ship it docks; a plan that
/// gives a ship a goal while its step works would see the step write over it, or act on a ship the step is moving.
/// </summary>
public interface IShipGoalStepGuard
{
    /// <summary>Claims the ship for one step, without waiting.</summary>
    /// <param name="shipSymbol">The ship.</param>
    /// <returns>False when another step or write for the ship holds it.</returns>
    bool TryEnter(string shipSymbol);

    /// <summary>
    /// Claims the ship for a write, waiting until the step or write that holds it ends (<see cref="StepGuardedShipGoalRepository"/>).
    /// </summary>
    /// <param name="shipSymbol">The ship.</param>
    /// <param name="timeout">How long to wait at most.</param>
    /// <param name="cancellationToken">Stops the wait.</param>
    /// <returns>True when the ship is claimed; false when it was held for longer than <paramref name="timeout"/>, and nothing is claimed.</returns>
    Task<bool> EnterAsync(string shipSymbol, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the code running now holds the ship (claimed it with <see cref="TryEnter"/> further up the same call): a step
    /// writes its own ship's goal without waiting for itself.
    /// </summary>
    /// <param name="shipSymbol">The ship.</param>
    /// <returns>True inside the ship's own step.</returns>
    bool IsHeldHere(string shipSymbol);

    /// <summary>Releases the ship after its step or write.</summary>
    /// <param name="shipSymbol">The ship.</param>
    void Exit(string shipSymbol);
}

/// <inheritdoc />
/// <remarks>
/// A singleton, in memory: the bot runs as one pod. A step doesn't wait: one that finds its ship held is skipped, and the next
/// round of steps, about 5 seconds later, takes the ship's next step. A write waits, as a step takes seconds and a plan's
/// decision would otherwise wait for its next pass. Who holds a ship is kept per call chain (<see cref="AsyncLocal{T}"/>),
/// set by <see cref="TryEnter"/>, which is synchronous so that the setting reaches the rest of the caller's step.
/// </remarks>
public sealed class ShipGoalStepGuard : IShipGoalStepGuard
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _held = new(StringComparer.OrdinalIgnoreCase);
    private readonly AsyncLocal<string> _heldHere = new();

    /// <inheritdoc />
    public bool TryEnter(string shipSymbol)
    {
        if (!_held.TryAdd(shipSymbol, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)))
        {
            return false;
        }

        _heldHere.Value = shipSymbol;
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> EnterAsync(string shipSymbol, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var mine = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var until = TimeProvider.System.GetUtcNow() + timeout;
        while (!_held.TryAdd(shipSymbol, mine))
        {
            if (!_held.TryGetValue(shipSymbol, out var holder))
            {
                continue;
            }

            var left = until - TimeProvider.System.GetUtcNow();
            if (left <= TimeSpan.Zero)
            {
                return false;
            }

            // Waiting claims nothing: a wait that times out or is cancelled leaves the ship as it was.
            try
            {
                await holder.Task.WaitAsync(left, cancellationToken);
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public bool IsHeldHere(string shipSymbol) => string.Equals(_heldHere.Value, shipSymbol, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Exit(string shipSymbol)
    {
        if (IsHeldHere(shipSymbol))
        {
            _heldHere.Value = string.Empty;
        }

        if (_held.TryRemove(shipSymbol, out var holder))
        {
            holder.TrySetResult();
        }
    }
}
