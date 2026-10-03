using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.SpareTime;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Goals;
using SpaceTraders.Domain.ValueObjects;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Executor for <see cref="GatherAndSellGoal"/>: one spare-time trip (PLAN.md slice 6.8). The ship flies to its asteroid
/// or gas giant, through refuelling stops when it must, and mines (<see cref="ExtractResourcesCommand"/>) or siphons
/// (<see cref="SiphonResourcesCommand"/>) once per cooldown, keeping whatever a market buys, until its hold is full.
/// Then it sells the hold one good at a time, each where it fetches most after fuel (D36): it chooses the sale and
/// records it in the goal, flies there, docks, sells in batches of the market's trade volume and fetches the market
/// again (D25). What doesn't pay for its fuel stays aboard for the next trip, and the goal ends: the plans choose
/// again. Before the hold is full, a survey or a trade may take the ship off the trip (D34, D37): the survey and
/// trading plans do that. The goal keeps what each sale brought in, and however the trip ends, it is booked with that
/// (<see cref="ITripBook"/>, D46).
/// </summary>
public sealed class GatherAndSellGoalExecutor(
    IShipRepository ships,
    IShipGoalRepository goals,
    IAgentRepository agents,
    ISpaceTradersPort port,
    ITradeContextReader tradeContexts,
    IMarketRefresher marketRefresher,
    IDockSubCommand dock,
    IMessageBus bus,
    ITripBook trips,
    ILogger<GatherAndSellGoalExecutor> logger) : IShipGoalExecutor
{
    /// <inheritdoc />
    public bool CanExecute(ShipGoal goal) => goal is GatherAndSellGoal;

    /// <inheritdoc />
    public async Task<GoalExecutionResult> ExecuteStepAsync(
        ShipModel ship,
        ShipGoal goal,
        ShipGoalContext ctx,
        CancellationToken ct)
    {
        var trip = (GatherAndSellGoal)goal;

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival("Spare-time ship is in transit.");
        }

        if (!trip.Selling && IsFull(ship))
        {
            await goals.SetActiveGoalAsync(ship.Symbol, trip with { Selling = true }, ct);
            return GoalExecutionResult.Progressing("Hold full; next, selling it where each good fetches most.");
        }

        var map = (await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, ct)).Map;
        return trip.Selling
            ? await SellStepAsync(map, ship, trip, ct)
            : await GatherStepAsync(map, ship, trip, ct);
    }

    private async Task<GoalExecutionResult> GatherStepAsync(TradeMarketMap map, ShipModel ship, GatherAndSellGoal trip, CancellationToken ct)
    {
        if (!IsAt(ship, trip.SourceWaypointSymbol))
        {
            return await GoalFlight.TowardsAsync(map, ship, trip.SourceWaypointSymbol, dock, bus, ct);
        }

        if (ship.CooldownExpiresAt.HasValue && ship.CooldownExpiresAt.Value > TimeProvider.System.GetUtcNow())
        {
            return GoalExecutionResult.WaitingForCooldown("Waiting for the cooldown.", ship.CooldownExpiresAt);
        }

        // Everything it would get there would go overboard: the plan chooses another place.
        if (!GatherPlanner.YieldsSellable(map, ship, trip.SourceWaypointSymbol))
        {
            await EndTripAsync(ship.Symbol, trip, TripBook.NoBuyer, ct);
            return GoalExecutionResult.Completed($"No market buys what {trip.SourceWaypointSymbol} yields any more; the trip ends.");
        }

        // One extraction or siphon; a docked ship orbits first.
        var gathered = trip.Siphoning
            ? await bus.InvokeAsync<ShipCommandResult>(new SiphonResourcesCommand(ship.Symbol, GatherPlanner.AnyGood, trip.SourceWaypointSymbol), ct)
            : await bus.InvokeAsync<ShipCommandResult>(new ExtractResourcesCommand(ship.Symbol, trip.SourceWaypointSymbol), ct);
        if (gathered is null || !gathered.Accepted)
        {
            // The plan chooses again on the next tick; a trip that keeps failing shows as RepeatingError.
            logger.LogWarning(
                "GatherAndSellGoalExecutor: ship {ShipSymbol} can't {Action} at {WaypointSymbol} (state {Status}); the trip is dropped.",
                ship.Symbol,
                trip.Siphoning ? "siphon" : "mine",
                trip.SourceWaypointSymbol,
                gathered?.Status ?? ShipLocalStatus.None);
            await EndTripAsync(ship.Symbol, trip, TripBook.Rejected, ct);
            return GoalExecutionResult.Blocked($"Gathering rejected at {trip.SourceWaypointSymbol}.");
        }

        return GoalExecutionResult.Progressing($"{(trip.Siphoning ? "Siphoning" : "Mining")} at {trip.SourceWaypointSymbol} in its spare time.");
    }

    private async Task<GoalExecutionResult> SellStepAsync(TradeMarketMap map, ShipModel ship, GatherAndSellGoal trip, CancellationToken ct)
    {
        var units = UnitsAboard(ship, trip.SellTradeSymbol);
        if (trip.SellTradeSymbol.Length == 0 || units <= 0)
        {
            // The next sale (D36). A full hold sells even where the sale doesn't pay for its fuel: the next trip
            // would have no room.
            if (!TradeRoutePlanner.TryFindBestCargoSale(map, ship, mustSell: IsFull(ship), out var cargo, out var sale))
            {
                await EndTripAsync(ship.Symbol, trip, TripBook.Sold, ct);
                return GoalExecutionResult.Completed(ship.CargoCurrent > 0
                    ? $"Sold what pays for its fuel; {ship.CargoCurrent} units stay aboard for the next trip."
                    : "The hold is sold; the trip is done.");
            }

            trip = trip with { SellTradeSymbol = cargo.Symbol, SellWaypointSymbol = sale.WaypointSymbol };
            await goals.SetActiveGoalAsync(ship.Symbol, trip, ct);
            units = cargo.Units;
        }

        if (!IsAt(ship, trip.SellWaypointSymbol))
        {
            return await GoalFlight.TowardsAsync(map, ship, trip.SellWaypointSymbol, dock, bus, ct);
        }

        if (ship.LocalStatus == ShipLocalStatus.InOrbit)
        {
            await dock.ExecuteAsync(ship.Symbol, ct);
            return GoalExecutionResult.Progressing($"Docking at {trip.SellWaypointSymbol} to sell {trip.SellTradeSymbol}.");
        }

        // The arrival has just refreshed the market's prices.
        if (!map.TryGetGood(trip.SellWaypointSymbol, trip.SellTradeSymbol, out var good) || good.SellPrice <= 0)
        {
            await goals.SetActiveGoalAsync(ship.Symbol, Unchosen(trip), ct);
            return GoalExecutionResult.Progressing($"{trip.SellWaypointSymbol} doesn't buy {trip.SellTradeSymbol} any more; the next step chooses again.");
        }

        // One sale may not exceed the market's trade volume, so a larger load goes in several.
        var batchSize = good.TradeVolume > 0 ? good.TradeVolume : units;
        var earned = 0L;
        for (var left = units; left > 0;)
        {
            var batch = Math.Min(left, batchSize);
            var sold = await port.SellCargoAsync(ship.Symbol, trip.SellTradeSymbol, batch, ct);
            await ships.UpdateCargoAsync(ship.Symbol, sold.Cargo, ct);
            await agents.SetCreditsAsync(bus, sold.AgentCredits, ct);

            // The ledger and the credits-earned metric (B7), and the units sold to this market.
            await bus.PublishAsync(new ShipCargoSoldEvent(
                ship.Symbol,
                new TradeSymbol(trip.SellTradeSymbol),
                batch,
                sold.Revenue,
                sold.AgentCredits,
                trip.SellWaypointSymbol));

            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} sold {Units} {TradeSymbol} at {WaypointSymbol} for {Revenue} credits.",
                JournalEvents.CargoSold,
                ship.Symbol,
                batch,
                trip.SellTradeSymbol,
                trip.SellWaypointSymbol,
                sold.Revenue);
            earned += sold.Revenue;
            left -= batch;
        }

        // The sale moved the price: the market again, while the ship is still there (D25).
        await marketRefresher.RefreshAfterTradeAsync(ship.SystemSymbol ?? string.Empty, trip.SellWaypointSymbol, ship.Symbol, ct);

        // The next step chooses the next sale, from here, or ends the trip; the goal keeps what this one brought in.
        await goals.SetActiveGoalAsync(ship.Symbol, Unchosen(trip) with { Earned = trip.Earned + earned }, ct);
        return GoalExecutionResult.Progressing($"Sold {units} {trip.SellTradeSymbol} at {trip.SellWaypointSymbol}.");
    }

    /// <summary>Ends the trip: clears its goal, so the plans choose again, and books what it made (D46).</summary>
    private async Task EndTripAsync(string shipSymbol, GatherAndSellGoal trip, string reason, CancellationToken ct)
    {
        await goals.ClearActiveGoalAsync(shipSymbol, ct);
        await trips.BookAsync(shipSymbol, trip, reason, ct);
    }

    private static GatherAndSellGoal Unchosen(GatherAndSellGoal trip)
        => trip with { SellTradeSymbol = string.Empty, SellWaypointSymbol = string.Empty };

    private static int UnitsAboard(ShipModel ship, string tradeSymbol)
        => tradeSymbol.Length == 0
            ? 0
            : (ship.CargoInventory ?? []).Where(item => item.Symbol.Equals(tradeSymbol, StringComparison.OrdinalIgnoreCase)).Sum(item => item.Units);

    private static bool IsFull(ShipModel ship) => ship.CargoCapacity > 0 && ship.CargoCurrent >= ship.CargoCapacity;

    private static bool IsAt(ShipModel ship, string waypointSymbol)
        => string.Equals(ship.WaypointSymbol, waypointSymbol, StringComparison.OrdinalIgnoreCase);
}
