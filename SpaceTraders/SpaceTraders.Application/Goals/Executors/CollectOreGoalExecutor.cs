using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Goals;
using SpaceTraders.Domain.ValueObjects;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Executor for <see cref="CollectOreGoal"/>: one round of a collecting shuttle (PLAN.md slice 6.18, D83). It flies to the
/// asteroid, through refuelling stops when it must, and waits there in orbit while the parked drones hand it their ore
/// (<see cref="MineForShuttleGoalExecutor"/>), making no API call, until its hold is full, or until no drone is left there
/// with ore aboard. Then it flies to the market, docks, fetches its hold from the API (the drones' transfers wrote it into
/// the cache from what they handed over), and sells every good the market buys, in batches of the market's trade volume,
/// fetching the market again afterwards (D25). What the market doesn't buy is jettisoned, as nothing else would take it
/// off the shuttle (D42). The round is booked as a trip (D46) and the goal ends; the mining plan gives the next round.
/// </summary>
public sealed class CollectOreGoalExecutor(
    IShipRepository ships,
    IShipGoalRepository goals,
    IAgentRepository agents,
    ISpaceTradersPort port,
    ITradeContextReader tradeContexts,
    IMarketRefresher marketRefresher,
    IDockSubCommand dock,
    IOrbitSubCommand orbit,
    ICargoJettison jettison,
    IMessageBus bus,
    ITripBook trips,
    ILogger<CollectOreGoalExecutor> logger) : IShipGoalExecutor
{
    /// <summary>How long the shuttle waits at the asteroid before it looks at its hold again: the tick steps it anyway.</summary>
    internal static readonly TimeSpan WaitForOre = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public bool CanExecute(ShipGoal goal) => goal is CollectOreGoal;

    /// <inheritdoc />
    public async Task<GoalExecutionResult> ExecuteStepAsync(
        ShipModel ship,
        ShipGoal goal,
        ShipGoalContext ctx,
        CancellationToken ct)
    {
        var round = (CollectOreGoal)goal;

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival("Shuttle is in transit.");
        }

        return round.Selling
            ? await SellStepAsync(ship, round, ct)
            : await CollectStepAsync(ship, round, ct);
    }

    private async Task<GoalExecutionResult> CollectStepAsync(ShipModel ship, CollectOreGoal round, CancellationToken ct)
    {
        if (ship.CargoCapacity > 0 && ship.CargoCurrent >= ship.CargoCapacity)
        {
            await goals.SetActiveGoalAsync(ship.Symbol, round with { Selling = true }, ct);
            return GoalExecutionResult.Progressing($"Hold full; next, selling at {round.SellWaypointSymbol}.");
        }

        if (!IsAt(ship, round.AsteroidWaypointSymbol))
        {
            var context = await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, ct);
            return await GoalFlight.TowardsAsync(context.Map, ship, round.AsteroidWaypointSymbol, dock, bus, ct);
        }

        // The drones hand over in orbit, where they extract: a transfer needs both ships in the same state.
        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            await orbit.ExecuteAsync(ship.Symbol, ct);
            return GoalExecutionResult.Progressing($"Orbiting {round.AsteroidWaypointSymbol} to collect.");
        }

        // A part hold goes when no drone is left there with ore to hand over: nothing more would come.
        if (ship.CargoCurrent > 0 && !await DroneWithOreAsync(round, ct))
        {
            await goals.SetActiveGoalAsync(ship.Symbol, round with { Selling = true }, ct);
            return GoalExecutionResult.Progressing($"No drone with ore left at {round.AsteroidWaypointSymbol}; selling what it holds at {round.SellWaypointSymbol}.");
        }

        return GoalExecutionResult.WaitingForCooldown(
            $"Collecting at {round.AsteroidWaypointSymbol}: {ship.CargoCurrent} of {ship.CargoCapacity}.",
            TimeProvider.System.GetUtcNow() + WaitForOre);
    }

    /// <summary>Whether a drone parked at the asteroid holds ore or mines there still, so more will come.</summary>
    private async Task<bool> DroneWithOreAsync(CollectOreGoal round, CancellationToken ct)
    {
        foreach (var drone in await ships.GetAllAsync(ct))
        {
            if (await goals.GetActiveGoalAsync(drone.Symbol, ct) is MineForShuttleGoal job
                && job.AsteroidWaypointSymbol.Equals(round.AsteroidWaypointSymbol, StringComparison.OrdinalIgnoreCase)
                && IsAt(drone, round.AsteroidWaypointSymbol)
                && drone.LocalStatus != ShipLocalStatus.InTransit)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<GoalExecutionResult> SellStepAsync(ShipModel ship, CollectOreGoal round, CancellationToken ct)
    {
        if (ship.CargoCurrent <= 0)
        {
            await EndRoundAsync(ship.Symbol, round, TripBook.NothingAboard, ct);
            return GoalExecutionResult.Completed("Nothing aboard; the round is done.");
        }

        if (!IsAt(ship, round.SellWaypointSymbol))
        {
            var context = await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, ct);
            return await GoalFlight.TowardsAsync(context.Map, ship, round.SellWaypointSymbol, dock, bus, ct);
        }

        if (ship.LocalStatus == ShipLocalStatus.InOrbit)
        {
            await dock.ExecuteAsync(ship.Symbol, ct);
            return GoalExecutionResult.Progressing($"Docking at {round.SellWaypointSymbol} to sell.");
        }

        // The drones wrote the shuttle's hold from what they handed over; the API's is the one to sell from.
        var hold = await port.GetShipCargoAsync(ship.Symbol, ct);
        await ships.UpdateCargoAsync(ship.Symbol, hold, ct);

        // The arrival has just refreshed the market's prices.
        var map = (await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, ct)).Map;
        var earned = 0L;
        foreach (var item in (hold.Inventory ?? []).Where(item => item.Units > 0).ToList())
        {
            if (!map.TryGetGood(round.SellWaypointSymbol, item.Symbol, out var good) || good.SellPrice <= 0)
            {
                // Nothing else takes cargo off a collecting shuttle (D42).
                await jettison.JettisonAsync(ship with { CargoInventory = hold.Inventory, CargoCurrent = hold.Units }, item, "no_buyer", ct);
                continue;
            }

            // One sale may not exceed the market's trade volume, so a larger load goes in several.
            var batchSize = good.TradeVolume > 0 ? good.TradeVolume : item.Units;
            for (var left = item.Units; left > 0;)
            {
                var batch = Math.Min(left, batchSize);
                var sale = await port.SellCargoAsync(ship.Symbol, item.Symbol, batch, ct);
                await ships.UpdateCargoAsync(ship.Symbol, sale.Cargo, ct);
                await agents.SetCreditsAsync(bus, sale.AgentCredits, ct);

                // The ledger and the credits-earned metric (B7), and the units sold to this market.
                await bus.PublishAsync(new ShipCargoSoldEvent(
                    ship.Symbol,
                    new TradeSymbol(item.Symbol),
                    batch,
                    sale.Revenue,
                    sale.AgentCredits,
                    round.SellWaypointSymbol));

                logger.LogInformation(
                    "{EventKind:l}: ship {ShipSymbol} sold {Units} {TradeSymbol} at {WaypointSymbol} for {Revenue} credits.",
                    JournalEvents.CargoSold,
                    ship.Symbol,
                    batch,
                    item.Symbol,
                    round.SellWaypointSymbol,
                    sale.Revenue);
                earned += sale.Revenue;
                left -= batch;
            }
        }

        // The sales moved the prices: the market again, while the shuttle is still there (D25).
        await marketRefresher.RefreshAfterTradeAsync(ship.SystemSymbol ?? string.Empty, round.SellWaypointSymbol, ship.Symbol, ct);

        await EndRoundAsync(ship.Symbol, round with { Earned = round.Earned + earned }, TripBook.Sold, ct);
        return GoalExecutionResult.Completed($"Sold the round's ore at {round.SellWaypointSymbol}; the round is done.");
    }

    /// <summary>Ends the round: clears its goal, so the mining plan gives the next, and books what it made (D46).</summary>
    private async Task EndRoundAsync(string shipSymbol, CollectOreGoal round, string reason, CancellationToken ct)
    {
        await goals.ClearActiveGoalAsync(shipSymbol, ct);
        await trips.BookAsync(shipSymbol, round, reason, ct);
    }

    private static bool IsAt(ShipModel ship, string waypointSymbol)
        => string.Equals(ship.WaypointSymbol, waypointSymbol, StringComparison.OrdinalIgnoreCase);
}
