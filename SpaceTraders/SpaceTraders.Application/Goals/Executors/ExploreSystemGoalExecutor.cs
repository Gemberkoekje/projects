using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Executor for <see cref="ExploreSystemGoal"/> (exploring, asked on 2026-10-04): the command ship visits each market and
/// shipyard of a system it explores once, as the scout plan does at home. It flies through refuelling stops, in BURN where
/// that strands nothing (<see cref="GoalFlight"/>); its arrival stores what the market and the shipyard there sell, and a stop whose market or
/// shipyard wasn't stored since the goal began, such as the gate the ship jumped to, is fetched before it moves on. Before
/// its first flight it waits out the jump's cooldown. The goal keeps its own progress, so the tick and an arrival that step
/// it one after the other can't skip a stop (B45).
/// </summary>
public sealed class ExploreSystemGoalExecutor(
    ISpaceTradersPort port,
    IShipGoalRepository goals,
    IWaypointRepository waypoints,
    IMarketRepository markets,
    IShipyardRepository shipyards,
    IMarketRefresher marketRefresher,
    IWaypointVisitService waypointVisit,
    ITradeContextReader tradeContexts,
    IDockSubCommand dock,
    IMessageBus bus,
    ILogger<ExploreSystemGoalExecutor> logger) : IShipGoalExecutor
{
    /// <inheritdoc />
    public bool CanExecute(ShipGoal goal) => goal is ExploreSystemGoal;

    /// <inheritdoc />
    public async Task<GoalExecutionResult> ExecuteStepAsync(ShipModel ship, ShipGoal goal, ShipGoalContext ctx, CancellationToken ct)
    {
        var explore = (ExploreSystemGoal)goal;
        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival($"In transit in {explore.SystemSymbol}.");
        }

        if (!string.Equals(ship.SystemSymbol, explore.SystemSymbol, StringComparison.OrdinalIgnoreCase)
            || explore.Visited >= explore.Stops.Count)
        {
            await goals.ClearActiveGoalAsync(ship.Symbol, ct);
            return GoalExecutionResult.Completed($"Scouted {explore.SystemSymbol}.");
        }

        var stop = explore.Stops[explore.Visited];
        if (string.Equals(ship.WaypointSymbol, stop, StringComparison.OrdinalIgnoreCase))
        {
            await StoreAsync(explore.SystemSymbol, stop, explore.StartedAt, ct);
            await waypointVisit.MarkVisitedAsync(stop, ct);

            var visited = explore with { Visited = explore.Visited + 1 };
            if (visited.Visited >= visited.Stops.Count)
            {
                await goals.ClearActiveGoalAsync(ship.Symbol, ct);
                return GoalExecutionResult.Completed($"Scouted the {visited.Stops.Count} markets and shipyards of {explore.SystemSymbol}.");
            }

            await goals.SetActiveGoalAsync(ship.Symbol, visited, ct);
            return GoalExecutionResult.Progressing($"Visited {stop} ({visited.Visited}/{visited.Stops.Count}).");
        }

        if (ship.CooldownExpiresAt is { } cooldown && cooldown > TimeProvider.System.GetUtcNow())
        {
            return GoalExecutionResult.WaitingForCooldown($"Waiting for the cooldown before flying to {stop}.", cooldown);
        }

        var context = await tradeContexts.ReadAsync(explore.SystemSymbol, ct);
        return await GoalFlight.TowardsAsync(context.Map, ship, stop, dock, bus, ct);
    }

    /// <summary>
    /// Stores the market and the shipyard at a stop, unless they were stored since <paramref name="since"/>: the arrival
    /// stores them, a jump doesn't. A failure is logged, and the ship moves on.
    /// </summary>
    private async Task StoreAsync(string systemSymbol, string stop, DateTimeOffset since, CancellationToken ct)
    {
        var waypoint = await waypoints.FindAsync(stop, ct);
        if (waypoint?.HasMarket == true && !StoredSince(await markets.GetLastObservedAtAsync(stop, ct), since))
        {
            try
            {
                await marketRefresher.RefreshAsync(systemSymbol, stop, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "ExploreSystemGoalExecutor: couldn't fetch the market at {WaypointSymbol}; the ship moves on.", stop);
            }
        }

        if (waypoint?.HasShipyard == true && !StoredSince(await shipyards.GetLastObservedAtAsync(stop, ct), since))
        {
            try
            {
                await shipyards.UpsertAsync(await port.GetShipyardAsync(systemSymbol, stop, ct), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "ExploreSystemGoalExecutor: couldn't fetch the shipyard at {WaypointSymbol}; the ship moves on.", stop);
            }
        }
    }

    private static bool StoredSince(DateTimeOffset? storedAt, DateTimeOffset since) => storedAt is { } at && at >= since;
}
