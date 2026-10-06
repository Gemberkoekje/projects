using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Executor for <see cref="ExploreSystemGoal"/> (exploring, asked on 2026-10-04): the exploring ship visits each market and
/// shipyard of a system it explores once, as the scout plan does at home. It flies through refuelling stops, in BURN where
/// that strands nothing (<see cref="GoalFlight"/>); its arrival stores what the market and the shipyard there sell, and a stop
/// whose market or shipyard wasn't stored since the goal began, such as the gate the ship jumped to, is fetched before it
/// moves on. Before its first flight it waits out the jump's cooldown. The goal keeps its own progress, so the tick and an
/// arrival that step it one after the other can't skip a stop (B45).
/// </summary>
/// <remarks>
/// Slice 6.30 (D99): at a stop nobody has charted, which can hold a market or shipyard (<see cref="Charting.ShouldChart"/>),
/// the ship charts it first. The chart shows the waypoint's traits, which the cache keeps, so a market or shipyard it reveals is
/// stored as at any other stop; its reward, the credits after the chart less those before it, is booked
/// (<see cref="WaypointChartedEvent"/>) and journalled (<c>Charted</c>). A chart that fails, as when another agent charted the
/// waypoint meanwhile, fetches the waypoint again instead, and the ship moves on.
/// </remarks>
public sealed class ExploreSystemGoalExecutor(
    ISpaceTradersPort port,
    IShipGoalRepository goals,
    IWaypointRepository waypoints,
    IMarketRepository markets,
    IShipyardRepository shipyards,
    IAgentRepository agents,
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
            await ChartAsync(ship, explore.SystemSymbol, stop, ct);
            await StoreAsync(explore.SystemSymbol, stop, explore.StartedAt, ct);
            await waypointVisit.MarkVisitedAsync(stop, ct);

            var visited = explore with { Visited = explore.Visited + 1 };
            if (visited.Visited >= visited.Stops.Count)
            {
                await goals.ClearActiveGoalAsync(ship.Symbol, ct);
                return GoalExecutionResult.Completed($"Scouted the {visited.Stops.Count} stops of {explore.SystemSymbol}.");
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
    /// Charts the stop when nobody has and it can hold a market or shipyard (D99), and keeps the waypoint the chart shows. Its
    /// reward is the credits after the chart less those cached before it: the API gives it on its own nowhere.
    /// </summary>
    private async Task ChartAsync(ShipModel ship, string systemSymbol, string stop, CancellationToken ct)
    {
        if (await waypoints.FindAsync(stop, ct) is not { } waypoint || !Charting.ShouldChart(waypoint))
        {
            return;
        }

        var before = (await agents.GetAsync(ct))?.Credits;
        ChartActionResult chart;
        try
        {
            chart = await port.CreateChartAsync(ship.Symbol, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "ExploreSystemGoalExecutor: ship {ShipSymbol} couldn't chart {WaypointSymbol}; it fetches the waypoint instead and moves on.", ship.Symbol, stop);
            await RefetchAsync(systemSymbol, stop, ct);
            return;
        }

        var now = TimeProvider.System.GetUtcNow();
        var charted = Charting.ToCache(chart.Waypoint, now);
        await waypoints.UpsertRangeAsync([charted], ct);
        var reward = chart.AgentCredits is { } after && before is { } was ? Math.Max(0, after - was) : 0;
        if (chart.AgentCredits is { } credits)
        {
            await agents.SetCreditsAsync(bus, credits, ct);
        }

        await bus.PublishAsync(new WaypointChartedEvent(ship.Symbol, stop, reward));
        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} charted {WaypointSymbol}, a {WaypointType}, for {Reward} credits; a market: {HasMarket}, a shipyard: {HasShipyard}.",
            JournalEvents.Charted,
            ship.Symbol,
            stop,
            charted.Type,
            reward,
            charted.HasMarket,
            charted.HasShipyard);
    }

    /// <summary>Fetches a waypoint the ship couldn't chart, so the cache keeps what anyone can see of it now.</summary>
    private async Task RefetchAsync(string systemSymbol, string stop, CancellationToken ct)
    {
        try
        {
            await waypoints.UpsertRangeAsync([Charting.ToCache(await port.GetWaypointAsync(systemSymbol, stop, ct), TimeProvider.System.GetUtcNow())], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogDebug(ex, "ExploreSystemGoalExecutor: couldn't fetch {WaypointSymbol} again either.", stop);
        }
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
