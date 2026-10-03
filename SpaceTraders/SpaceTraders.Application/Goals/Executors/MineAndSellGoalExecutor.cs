using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
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
/// Executor for <see cref="MineAndSellGoal"/>: one mining trip (PLAN.md slice 6.4). The ship flies to the
/// asteroid, through refuelling stops when it must, and extracts once per cooldown, with the best survey
/// there for its ore when there is one (<see cref="MineResourceVolumeCommand"/>), until its hold is full.
/// Then it flies to the sell market, docks and sells, in batches of the market's trade volume, and the
/// goal ends: the mining plan chooses the next trip.
/// </summary>
public sealed class MineAndSellGoalExecutor(
    IShipRepository ships,
    IShipGoalRepository goals,
    IAgentRepository agents,
    ISpaceTradersPort port,
    ITradeContextReader tradeContexts,
    IMarketRefresher marketRefresher,
    IDockSubCommand dock,
    IMessageBus bus,
    ILogger<MineAndSellGoalExecutor> logger) : IShipGoalExecutor
{
    /// <inheritdoc />
    public bool CanExecute(ShipGoal goal) => goal is MineAndSellGoal;

    /// <inheritdoc />
    public async Task<GoalExecutionResult> ExecuteStepAsync(
        ShipModel ship,
        ShipGoal goal,
        ShipGoalContext ctx,
        CancellationToken ct)
    {
        var trip = (MineAndSellGoal)goal;

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival("Mining ship is in transit.");
        }

        if (!trip.Selling && ship.CargoCapacity > 0 && ship.CargoCurrent >= ship.CargoCapacity)
        {
            await goals.SetActiveGoalAsync(ship.Symbol, trip with { Selling = true }, ct);
            return GoalExecutionResult.Progressing($"Hold full of {trip.TradeSymbol}; next, selling at {trip.SellWaypointSymbol}.");
        }

        return trip.Selling
            ? await SellStepAsync(ship, trip, ct)
            : await MineStepAsync(ship, trip, ct);
    }

    private async Task<GoalExecutionResult> MineStepAsync(ShipModel ship, MineAndSellGoal trip, CancellationToken ct)
    {
        if (!IsAt(ship, trip.SourceWaypointSymbol))
        {
            var context = await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, ct);
            return await GoalFlight.TowardsAsync(context.Map, ship, trip.SourceWaypointSymbol, dock, bus, ct);
        }

        if (ship.CooldownExpiresAt.HasValue && ship.CooldownExpiresAt.Value > TimeProvider.System.GetUtcNow())
        {
            return GoalExecutionResult.WaitingForCooldown("Waiting for extraction cooldown.", ship.CooldownExpiresAt);
        }

        // One extraction, with the best survey for the ore when there is one; a docked ship orbits first.
        var mined = await bus.InvokeAsync<ShipCommandResult>(
            new MineResourceVolumeCommand(ship.Symbol, trip.TradeSymbol, trip.SourceWaypointSymbol, Math.Max(1, ship.CargoCapacity)),
            ct);
        if (mined is null || !mined.Accepted)
        {
            // The plan chooses again on the next tick; a trip that keeps failing shows as RepeatingError.
            await goals.ClearActiveGoalAsync(ship.Symbol, ct);
            logger.LogWarning(
                "MineAndSellGoalExecutor: ship {ShipSymbol} can't mine {TradeSymbol} at {WaypointSymbol} (state {Status}); the trip is dropped.",
                ship.Symbol,
                trip.TradeSymbol,
                trip.SourceWaypointSymbol,
                mined?.Status ?? ShipLocalStatus.None);
            return GoalExecutionResult.Blocked($"Mining rejected at {trip.SourceWaypointSymbol}.");
        }

        return GoalExecutionResult.Progressing($"Mining {trip.TradeSymbol} at {trip.SourceWaypointSymbol}.");
    }

    private async Task<GoalExecutionResult> SellStepAsync(ShipModel ship, MineAndSellGoal trip, CancellationToken ct)
    {
        var units = (ship.CargoInventory ?? [])
            .Where(item => item.Symbol.Equals(trip.TradeSymbol, StringComparison.OrdinalIgnoreCase))
            .Sum(item => item.Units);
        if (units <= 0)
        {
            await goals.ClearActiveGoalAsync(ship.Symbol, ct);
            return GoalExecutionResult.Completed($"No {trip.TradeSymbol} aboard; the trip is done.");
        }

        if (!IsAt(ship, trip.SellWaypointSymbol))
        {
            var context = await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, ct);
            return await GoalFlight.TowardsAsync(context.Map, ship, trip.SellWaypointSymbol, dock, bus, ct);
        }

        if (ship.LocalStatus == ShipLocalStatus.InOrbit)
        {
            await dock.ExecuteAsync(ship.Symbol, ct);
            return GoalExecutionResult.Progressing($"Docking at sell waypoint {trip.SellWaypointSymbol}.");
        }

        // The arrival has just refreshed the market's prices.
        var map = (await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, ct)).Map;
        if (!map.TryGetGood(trip.SellWaypointSymbol, trip.TradeSymbol, out var good) || good.SellPrice <= 0)
        {
            // The market no longer buys it: the mining plan sells the ore where it fetches most.
            await goals.ClearActiveGoalAsync(ship.Symbol, ct);
            return GoalExecutionResult.Completed($"{trip.SellWaypointSymbol} doesn't buy {trip.TradeSymbol} any more; the trip ends.");
        }

        // One sale may not exceed the market's trade volume, so a larger load goes in several.
        var batchSize = good.TradeVolume > 0 ? good.TradeVolume : units;
        for (var left = units; left > 0;)
        {
            var batch = Math.Min(left, batchSize);
            var sale = await port.SellCargoAsync(ship.Symbol, trip.TradeSymbol, batch, ct);
            await ships.UpdateCargoAsync(ship.Symbol, sale.Cargo, ct);
            await agents.SetCreditsAsync(bus, sale.AgentCredits, ct);

            // The ledger and the credits-earned metric (B7), and the units sold to this market.
            await bus.PublishAsync(new ShipCargoSoldEvent(
                ship.Symbol,
                new TradeSymbol(trip.TradeSymbol),
                batch,
                sale.Revenue,
                sale.AgentCredits,
                trip.SellWaypointSymbol));

            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} sold {Units} {TradeSymbol} at {WaypointSymbol} for {Revenue} credits.",
                JournalEvents.CargoSold,
                ship.Symbol,
                batch,
                trip.TradeSymbol,
                trip.SellWaypointSymbol,
                sale.Revenue);
            left -= batch;
        }

        // The sale moved the price: the market again, while the ship is still there (D25).
        await marketRefresher.RefreshAfterTradeAsync(ship.SystemSymbol ?? string.Empty, trip.SellWaypointSymbol, ship.Symbol, ct);

        await goals.ClearActiveGoalAsync(ship.Symbol, ct);
        return GoalExecutionResult.Completed($"Sold {units} {trip.TradeSymbol} at {trip.SellWaypointSymbol}; the trip is done.");
    }

    private static bool IsAt(ShipModel ship, string waypointSymbol)
        => string.Equals(ship.WaypointSymbol, waypointSymbol, StringComparison.OrdinalIgnoreCase);
}
