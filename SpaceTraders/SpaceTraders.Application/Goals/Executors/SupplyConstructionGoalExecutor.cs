using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Construction;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Events.Ships;
using SpaceTraders.Domain.Goals;
using SpaceTraders.Domain.ValueObjects;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Executor for <see cref="SupplyConstructionGoal"/>: one construction trip (PLAN.md slice 6.6). The ship flies to the buy
/// market (<see cref="GoalFlight"/>: in CRUISE, through refuelling stops), buys its load in one purchase, flies to the
/// construction site and supplies it there. A ship can't change course in flight, so it checks the load again at the
/// market, with the prices its arrival has just fetched: when the site no longer needs it, the market's supply has dropped
/// to SCARCE or LIMITED (D66), its trade volume no longer takes the load at once (D67), or the load would dip into the
/// credit reserve (D64), it drops the trip (<c>ConstructionDropped</c>) and the construction plan chooses again. Supplying
/// pays nothing: the trip books what its cargo and fuel cost as a loss (D46).
/// </summary>
public sealed class SupplyConstructionGoalExecutor(
    IShipRepository ships,
    IShipGoalRepository goals,
    IAgentRepository agents,
    ISpaceTradersPort port,
    ITradeContextReader tradeContexts,
    IMarketRefresher marketRefresher,
    IDockSubCommand dock,
    IMessageBus bus,
    ITripBook trips,
    IBudgetPolicy budget,
    IConstructionSites sites,
    ConstructionRetries retries,
    ILogger<SupplyConstructionGoalExecutor> logger) : IShipGoalExecutor
{
    private const string NotSoldHere = "not_sold_here";
    private const string NotFullHold = "not_full_hold";
    private const string OverBudget = "over_budget";

    /// <inheritdoc />
    public bool CanExecute(ShipGoal goal) => goal is SupplyConstructionGoal;

    /// <inheritdoc />
    public async Task<GoalExecutionResult> ExecuteStepAsync(ShipModel ship, ShipGoal goal, ShipGoalContext ctx, CancellationToken ct)
    {
        var trip = (SupplyConstructionGoal)goal;
        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival("Construction ship is in transit.");
        }

        return trip.CargoBought
            ? await SupplyStepAsync(ship, trip, ct)
            : await BuyStepAsync(ship, trip, ct);
    }

    private async Task<GoalExecutionResult> BuyStepAsync(ShipModel ship, SupplyConstructionGoal trip, CancellationToken ct)
    {
        var system = ship.SystemSymbol ?? string.Empty;
        if (!IsAt(ship, trip.BuyWaypointSymbol))
        {
            return await FlyTowardsAsync(ship, trip.BuyWaypointSymbol, ct);
        }

        if (ship.LocalStatus == ShipLocalStatus.InOrbit)
        {
            await dock.ExecuteAsync(ship.Symbol, ct);
            return GoalExecutionResult.Progressing($"Docking at buy waypoint {trip.BuyWaypointSymbol}.");
        }

        // What the site still needs of the material, less what the other construction trips carry or go to buy.
        var site = await sites.FindAsync(trip.ConstructionSiteWaypointSymbol, ct);
        var others = (await goals.GetActiveConstructionGoalsAsync(ct))
            .Where(other => !other.Key.Equals(ship.Symbol, StringComparison.OrdinalIgnoreCase))
            .Select(other => other.Value);
        var remaining = site is null || site.IsComplete
            ? 0
            : ConstructionPlanner.Needs(site, others).FirstOrDefault(need => need.TradeSymbol.Equals(trip.TradeSymbol, StringComparison.OrdinalIgnoreCase))?.Remaining ?? 0;
        var units = Math.Min(Math.Min(trip.Units, ship.CargoCapacity - ship.CargoCurrent), remaining);
        if (units <= 0)
        {
            return await DropAsync(ship, trip, TripBook.NotNeeded, ct);
        }

        var map = (await tradeContexts.ReadAsync(system, ct)).Map;
        if (!map.TryGetGood(trip.BuyWaypointSymbol, trip.TradeSymbol, out var good) || good.PurchasePrice <= 0)
        {
            return await DropAsync(ship, trip, NotSoldHere, ct);
        }

        if (MiningPlanner.IsLowSupply(good.Supply))
        {
            return await DropAsync(ship, trip, ConstructionPlanner.LowSupply, ct);
        }

        if (good.TradeVolume < units)
        {
            return await DropAsync(ship, trip, NotFullHold, ct);
        }

        // D64: a load keeps the credit reserve, as a ship purchase does; what this trip holds back is its own to spend.
        var decision = await budget.EvaluateAsync(0, ct);
        var spendable = Math.Max(0, decision.AvailableCredits - Math.Max(0, decision.ReservedCredits - TripReservations.HeldBack(trip)));
        if ((long)units * good.PurchasePrice > spendable)
        {
            return await DropAsync(ship, trip, OverBudget, ct);
        }

        var result = await port.BuyCargoAsync(ship.Symbol, trip.TradeSymbol, units, ct);
        await ships.UpdateCargoAsync(ship.Symbol, result.Cargo, ct);
        await agents.SetCreditsAsync(bus, result.AgentCredits, ct);

        // The ledger books it apart from trading's purchases, and counts the units bought from this market. The port's
        // "revenue" is the transaction's total.
        await bus.PublishAsync(new CargoPurchasedEvent(
            ship.Symbol,
            new TradeSymbol(trip.TradeSymbol),
            units,
            result.Revenue,
            result.AgentCredits,
            trip.BuyWaypointSymbol)
        {
            ForConstruction = true,
        });

        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} bought {Units} {TradeSymbol} at {WaypointSymbol} for {Cost} credits.",
            JournalEvents.CargoBought,
            ship.Symbol,
            units,
            trip.TradeSymbol,
            trip.BuyWaypointSymbol,
            result.Revenue);

        // The purchase moved the price: the market again, while the ship is still there (D25).
        await marketRefresher.RefreshAfterTradeAsync(system, trip.BuyWaypointSymbol, ship.Symbol, ct);

        await goals.SetActiveGoalAsync(
            ship.Symbol,
            trip with
            {
                CargoBought = true,
                Units = units,
                PricePaidPerUnit = result.Revenue / units,
                Spent = trip.Spent + result.Revenue,
            },
            ct);
        return GoalExecutionResult.Progressing(
            $"Bought {units} {trip.TradeSymbol} at {trip.BuyWaypointSymbol}; next, supplying {trip.ConstructionSiteWaypointSymbol}.");
    }

    private async Task<GoalExecutionResult> SupplyStepAsync(ShipModel ship, SupplyConstructionGoal trip, CancellationToken ct)
    {
        var system = ship.SystemSymbol ?? string.Empty;
        var aboard = (ship.CargoInventory ?? [])
            .Where(item => item.Symbol.Equals(trip.TradeSymbol, StringComparison.OrdinalIgnoreCase))
            .Sum(item => item.Units);
        if (aboard <= 0)
        {
            await EndTripAsync(ship.Symbol, trip, TripBook.NothingAboard, ct);
            return GoalExecutionResult.Completed($"No {trip.TradeSymbol} aboard; nothing left to supply.");
        }

        if (!IsAt(ship, trip.ConstructionSiteWaypointSymbol))
        {
            return await FlyTowardsAsync(ship, trip.ConstructionSiteWaypointSymbol, ct);
        }

        if (ship.LocalStatus == ShipLocalStatus.InOrbit)
        {
            await dock.ExecuteAsync(ship.Symbol, ct);
            return GoalExecutionResult.Progressing($"Docking at construction site {trip.ConstructionSiteWaypointSymbol}.");
        }

        // The site as last seen; when that says it needs no more of it, the API is asked once more before giving up.
        var site = await sites.FindAsync(trip.ConstructionSiteWaypointSymbol, ct);
        var needed = StillNeeded(site, trip.TradeSymbol, aboard);
        if (needed <= 0)
        {
            site = await sites.FetchAsync(system, trip.ConstructionSiteWaypointSymbol, ct) ?? site;
            needed = StillNeeded(site, trip.TradeSymbol, aboard);
        }

        if (needed <= 0)
        {
            return await DropAsync(ship, trip, TripBook.NotNeeded, ct);
        }

        var units = Math.Min(aboard, needed);
        SupplyConstructionActionResult result;
        try
        {
            result = await port.SupplyConstructionAsync(system, trip.ConstructionSiteWaypointSymbol, ship.Symbol, trip.TradeSymbol, units, ct);
        }
        catch (ConstructionRefusedException refused)
        {
            // Supplied again, it fails again: the plan doesn't offer this ship the material for a while, and the site is
            // fetched again to learn what it needs now.
            retries.Refused(ship.Symbol, trip.TradeSymbol, TimeProvider.System.GetUtcNow());
            await sites.FetchAsync(system, trip.ConstructionSiteWaypointSymbol, ct);
            logger.LogWarning(
                refused,
                "{EventKind:l}: ship {ShipSymbol} drops its {TradeSymbol} trip at {WaypointSymbol}: the site refused {Units} ({Reason}); it keeps them aboard.",
                JournalEvents.ConstructionDropped,
                ship.Symbol,
                trip.TradeSymbol,
                trip.ConstructionSiteWaypointSymbol,
                units,
                refused.Reason);
            await EndTripAsync(ship.Symbol, trip, refused.Reason, ct);
            return GoalExecutionResult.Completed($"The site refused the {trip.TradeSymbol} ({refused.Reason}).");
        }

        await ships.UpdateCargoAsync(ship.Symbol, result.Cargo, ct);
        await sites.RecordAsync(result.Construction, system, ct);
        await bus.PublishAsync(new ConstructionSuppliedEvent(
            ship.Symbol,
            system,
            trip.ConstructionSiteWaypointSymbol,
            trip.TradeSymbol,
            units,
            result.Construction.IsComplete,
            Guid.Empty,
            Guid.Empty,
            TimeProvider.System.GetUtcNow()));

        var material = result.Construction.Materials.FirstOrDefault(item => item.TradeSymbol.Equals(trip.TradeSymbol, StringComparison.OrdinalIgnoreCase));
        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} supplied {Units} {TradeSymbol} to {WaypointSymbol}: {Fulfilled} of {Required}.",
            JournalEvents.ConstructionSupplied,
            ship.Symbol,
            units,
            trip.TradeSymbol,
            trip.ConstructionSiteWaypointSymbol,
            material?.Fulfilled ?? 0,
            material?.Required ?? 0);

        await EndTripAsync(ship.Symbol, trip, TripBook.Supplied, ct);
        return GoalExecutionResult.Completed(
            $"Supplied {units} {trip.TradeSymbol} to {trip.ConstructionSiteWaypointSymbol}; the trip is done.");
    }

    /// <summary>
    /// The units of a material the site still needs, as last seen: what it requires less what it has; for a site not cached,
    /// what the ship holds, and the API decides.
    /// </summary>
    private static int StillNeeded(ConstructionSiteModel? site, string tradeSymbol, int aboard)
    {
        if (site is null)
        {
            return aboard;
        }

        var material = site.Materials.FirstOrDefault(item => item.TradeSymbol.Equals(tradeSymbol, StringComparison.OrdinalIgnoreCase));
        return site.IsComplete || material is null ? 0 : Math.Max(0, material.Required - material.Fulfilled);
    }

    private async Task<GoalExecutionResult> FlyTowardsAsync(ShipModel ship, string destination, CancellationToken ct)
    {
        var map = (await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, ct)).Map;
        return await GoalFlight.TowardsAsync(map, ship, destination, dock, bus, ct);
    }

    /// <summary>Gives the trip up: the construction plan chooses again, and a cargo aboard is offered to the site again.</summary>
    private async Task<GoalExecutionResult> DropAsync(ShipModel ship, SupplyConstructionGoal trip, string reason, CancellationToken ct)
    {
        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} drops its {TradeSymbol} trip at {WaypointSymbol} for {SiteWaypoint} ({Reason}).",
            JournalEvents.ConstructionDropped,
            ship.Symbol,
            trip.TradeSymbol,
            ship.WaypointSymbol,
            trip.ConstructionSiteWaypointSymbol,
            reason);
        await EndTripAsync(ship.Symbol, trip, reason, ct);
        return GoalExecutionResult.Completed($"The {trip.TradeSymbol} trip for {trip.ConstructionSiteWaypointSymbol} can't be carried on ({reason}); dropped.");
    }

    /// <summary>Ends the trip: clears its goal, so the construction plan chooses the next, and books what it cost (D46).</summary>
    private async Task EndTripAsync(string shipSymbol, SupplyConstructionGoal trip, string reason, CancellationToken ct)
    {
        await goals.ClearActiveGoalAsync(shipSymbol, ct);
        await trips.BookAsync(shipSymbol, trip, reason, ct);
    }

    private static bool IsAt(ShipModel ship, string waypointSymbol)
        => string.Equals(ship.WaypointSymbol, waypointSymbol, StringComparison.OrdinalIgnoreCase);
}
