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
/// Executor for <see cref="SiphonAndSellGoal"/>: one siphon trip (PLAN.md slice 6.7), as a mining trip
/// (<see cref="MineAndSellGoalExecutor"/>). The ship flies to the gas giant, through refuelling stops when it
/// must, and siphons once per cooldown (<see cref="SiphonResourcesCommand"/>), keeping every gas a market buys
/// (D33), until its hold is full. Then it flies to the sell market, docks and sells the trip's gas, in batches of
/// the market's trade volume, and the goal ends: the siphon plan sells the other gases and chooses the next trip.
/// </summary>
public sealed class SiphonAndSellGoalExecutor(
    IShipRepository ships,
    IShipGoalRepository goals,
    IAgentRepository agents,
    ISpaceTradersPort port,
    ITradeContextReader tradeContexts,
    IMarketRefresher marketRefresher,
    IDockSubCommand dock,
    IMessageBus bus,
    ILogger<SiphonAndSellGoalExecutor> logger) : IShipGoalExecutor
{
    /// <inheritdoc />
    public bool CanExecute(ShipGoal goal) => goal is SiphonAndSellGoal;

    /// <inheritdoc />
    public async Task<GoalExecutionResult> ExecuteStepAsync(
        ShipModel ship,
        ShipGoal goal,
        ShipGoalContext ctx,
        CancellationToken ct)
    {
        var trip = (SiphonAndSellGoal)goal;

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival("Siphon ship is in transit.");
        }

        if (!trip.Selling && ship.CargoCapacity > 0 && ship.CargoCurrent >= ship.CargoCapacity)
        {
            await goals.SetActiveGoalAsync(ship.Symbol, trip with { Selling = true }, ct);
            return GoalExecutionResult.Progressing($"Hold full; next, selling {trip.TradeSymbol} at {trip.SellWaypointSymbol}.");
        }

        return trip.Selling
            ? await SellStepAsync(ship, trip, ct)
            : await SiphonStepAsync(ship, trip, ct);
    }

    private async Task<GoalExecutionResult> SiphonStepAsync(ShipModel ship, SiphonAndSellGoal trip, CancellationToken ct)
    {
        if (!IsAt(ship, trip.SourceWaypointSymbol))
        {
            var context = await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, ct);
            return await GoalFlight.TowardsAsync(context.Map, ship, trip.SourceWaypointSymbol, dock, bus, ct);
        }

        if (ship.CooldownExpiresAt.HasValue && ship.CooldownExpiresAt.Value > TimeProvider.System.GetUtcNow())
        {
            return GoalExecutionResult.WaitingForCooldown("Waiting for siphon cooldown.", ship.CooldownExpiresAt);
        }

        // One siphon; a docked ship orbits first.
        var siphoned = await bus.InvokeAsync<ShipCommandResult>(
            new SiphonResourcesCommand(ship.Symbol, trip.TradeSymbol, trip.SourceWaypointSymbol),
            ct);
        if (siphoned is null || !siphoned.Accepted)
        {
            // The plan chooses again on the next tick; a trip that keeps failing shows as RepeatingError.
            await goals.ClearActiveGoalAsync(ship.Symbol, ct);
            logger.LogWarning(
                "SiphonAndSellGoalExecutor: ship {ShipSymbol} can't siphon at {WaypointSymbol} (state {Status}); the trip is dropped.",
                ship.Symbol,
                trip.SourceWaypointSymbol,
                siphoned?.Status ?? ShipLocalStatus.None);
            return GoalExecutionResult.Blocked($"Siphoning rejected at {trip.SourceWaypointSymbol}.");
        }

        return GoalExecutionResult.Progressing($"Siphoning at {trip.SourceWaypointSymbol} for {trip.TradeSymbol}.");
    }

    private async Task<GoalExecutionResult> SellStepAsync(ShipModel ship, SiphonAndSellGoal trip, CancellationToken ct)
    {
        var units = (ship.CargoInventory ?? [])
            .Where(item => item.Symbol.Equals(trip.TradeSymbol, StringComparison.OrdinalIgnoreCase))
            .Sum(item => item.Units);
        if (units <= 0)
        {
            // A hold of other gases: the siphon plan sells them where each fetches most.
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
            // The market no longer buys it: the siphon plan sells the gas where it fetches most.
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

            // The ledger and the credits-earned metric (B7).
            await bus.PublishAsync(new ShipCargoSoldEvent(
                ship.Symbol,
                new TradeSymbol(trip.TradeSymbol),
                batch,
                sale.Revenue,
                sale.AgentCredits));

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
