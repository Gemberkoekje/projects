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
/// with ore aboard. Then it sells what it holds, since slice 6.53 (D142, asked on 2026-11-08 with "Gate chain first, per
/// good") each good where it is wanted most: at a market that makes a good of the jump gate's chain from it, the nearest step
/// first, else where it fetches most after fuel, a market that makes something from it first (D91,
/// <see cref="TradeRoutePlanner.TryFindBestSale"/>). At each market it docks, fetches its hold from the API (the drones'
/// transfers wrote it into the cache from what they handed over), sells there every good whose best market that is, in
/// batches of the market's trade volume, fetches the market again afterwards (D25), and flies on to the next good's market.
/// What no market it can reach buys is jettisoned, as nothing else would take it off the shuttle (D42). The round is booked
/// as a trip (D46) and the goal ends; the plan gives the next round.
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
            var full = await StartSellingAsync(ship, round, ct);
            return GoalExecutionResult.Progressing($"Hold full; next, selling at {full.SellWaypointSymbol}.");
        }

        if (!IsAt(ship, round.AsteroidWaypointSymbol))
        {
            // It burns out there only when that leaves the fuel to carry the ore on to the market (D84).
            var context = await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, ct);
            return await GoalFlight.TowardsAsync(context.Map, ship, round.AsteroidWaypointSymbol, dock, bus, ct, onward: round.SellWaypointSymbol);
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
            var part = await StartSellingAsync(ship, round, ct);
            return GoalExecutionResult.Progressing($"No drone with ore left at {round.AsteroidWaypointSymbol}; selling what it holds at {part.SellWaypointSymbol}.");
        }

        return GoalExecutionResult.WaitingForCooldown(
            $"Collecting at {round.AsteroidWaypointSymbol}: {ship.CargoCurrent} of {ship.CargoCapacity}.",
            TimeProvider.System.GetUtcNow() + WaitForOre);
    }

    /// <summary>
    /// Turns the round to selling (D142): its first market is where the good aboard that is wanted most sells best
    /// (<see cref="TryChooseNextSale"/>); the point's market while none it can reach buys anything aboard.
    /// </summary>
    private async Task<CollectOreGoal> StartSellingAsync(ShipModel ship, CollectOreGoal round, CancellationToken ct)
    {
        var map = (await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, ct)).Map;
        var selling = round with
        {
            Selling = true,
            SellWaypointSymbol = TryChooseNextSale(map, ship, out var market) ? market : round.SellWaypointSymbol,
        };
        await goals.SetActiveGoalAsync(ship.Symbol, selling, ct);
        return selling;
    }

    /// <summary>
    /// Where a collecting shuttle sells next (PLAN.md slice 6.53, D142): of the goods aboard, the one whose best sale feeds the
    /// jump gate's chain, the nearest step first, else the one whose best sale fetches most; its best market, by
    /// <see cref="TradeRoutePlanner.TryFindBestSale"/> with the gate first.
    /// </summary>
    /// <param name="map">The shuttle's system.</param>
    /// <param name="ship">The shuttle, where it is now, with its hold.</param>
    /// <param name="market">Where it sells next.</param>
    /// <returns>False when no market it can reach buys anything aboard.</returns>
    internal static bool TryChooseNextSale(TradeMarketMap map, ShipModel ship, out string market)
    {
        market = string.Empty;
        var best = (Step: int.MaxValue, Revenue: long.MinValue);
        foreach (var item in (ship.CargoInventory ?? []).Where(item => item.Units > 0))
        {
            if (!TradeRoutePlanner.TryFindBestSale(map, ship, item.Symbol, item.Units, out var sale, supplyFirst: true, gateFirst: true))
            {
                continue;
            }

            var step = map.ConstructionFeedOf(sale.WaypointSymbol, item.Symbol) is { Step: > 0 } feed && sale.NetRevenue > 0 ? feed.Step : int.MaxValue;
            if (market.Length == 0 || step < best.Step || (step == best.Step && sale.NetRevenue > best.Revenue))
            {
                market = sale.WaypointSymbol;
                best = (step, sale.NetRevenue);
            }
        }

        return market.Length > 0;
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

        // The arrival has just refreshed the market's prices. D142: a good sells here when this is where it is wanted most.
        var map = (await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, ct)).Map;
        var here = ship with { CargoInventory = hold.Inventory, CargoCurrent = hold.Units };
        var earned = 0L;
        var left = hold;
        foreach (var item in (hold.Inventory ?? []).Where(item => item.Units > 0).ToList())
        {
            if (!map.TryGetGood(round.SellWaypointSymbol, item.Symbol, out var good)
                || good.SellPrice <= 0
                || (TradeRoutePlanner.TryFindBestSale(map, here, item.Symbol, item.Units, out var best, supplyFirst: true, gateFirst: true)
                    && !best.WaypointSymbol.Equals(round.SellWaypointSymbol, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // One sale may not exceed the market's trade volume, so a larger load goes in several.
            var batchSize = good.TradeVolume > 0 ? good.TradeVolume : item.Units;
            for (var unsold = item.Units; unsold > 0;)
            {
                var batch = Math.Min(unsold, batchSize);
                var sale = await port.SellCargoAsync(ship.Symbol, item.Symbol, batch, ct);
                await ships.UpdateCargoAsync(ship.Symbol, sale.Cargo, ct);
                left = sale.Cargo;
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
                unsold -= batch;
            }
        }

        // The sales moved the prices: the market again, while the shuttle is still there (D25).
        if (earned > 0)
        {
            await marketRefresher.RefreshAfterTradeAsync(ship.SystemSymbol ?? string.Empty, round.SellWaypointSymbol, ship.Symbol, ct);
        }

        var sold = round with { Earned = round.Earned + earned };
        if (left.Units <= 0)
        {
            await EndRoundAsync(ship.Symbol, sold, TripBook.Sold, ct);
            return GoalExecutionResult.Completed($"Sold the round's cargo, the last at {round.SellWaypointSymbol}; the round is done.");
        }

        // D142: the next good's market; what no market in reach buys goes overboard, as nothing else takes it off (D42).
        var rest = ship with { CargoInventory = left.Inventory, CargoCurrent = left.Units };
        if (TryChooseNextSale(map, rest, out var next) && !next.Equals(round.SellWaypointSymbol, StringComparison.OrdinalIgnoreCase))
        {
            await goals.SetActiveGoalAsync(ship.Symbol, sold with { SellWaypointSymbol = next }, ct);
            return GoalExecutionResult.Progressing($"Sold here what sells best at {round.SellWaypointSymbol}; next, {next}.");
        }

        foreach (var item in (left.Inventory ?? []).Where(item => item.Units > 0).ToList())
        {
            await jettison.JettisonAsync(rest, item, "no_buyer", ct);
        }

        await EndRoundAsync(ship.Symbol, sold, TripBook.Sold, ct);
        return GoalExecutionResult.Completed($"Sold the round's cargo at {round.SellWaypointSymbol}; what no market buys went overboard.");
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
