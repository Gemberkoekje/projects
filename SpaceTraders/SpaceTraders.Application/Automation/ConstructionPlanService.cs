using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Construction;
using SpaceTraders.Application.Health;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Automation;

/// <summary>The construction plan (PLAN.md slice 6.6).</summary>
public interface IConstructionPlanService
{
    /// <summary>
    /// One pass of the plan: gives a trip to every free ship that holds what a jump gate still needs, and a load to every free
    /// builder the money and the order ships are bought in allow; records the sites and why no load was bought.
    /// </summary>
    /// <param name="cancellationToken">Stops the pass.</param>
    /// <returns>A task that completes when the pass is done.</returns>
    Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The construction plan (PLAN.md slice 6.6), asked for on 2026-10-04: "Finishing this jump node should be top priority,
/// as it opens up the rest of the game. Can you implement a special role that works on this jump gate?" Bootstrapped after
/// the mining and siphon plans and before trading, it builds the jump gate of the home system, where the headquarters are,
/// while the gate needs materials (<see cref="IConstructionSites"/>); a gate elsewhere isn't considered (D68):
/// <list type="bullet">
///   <item>a free ship that holds a material the gate still needs takes it there first, whatever its role: it would
///   otherwise be sold, or jettisoned where no market buys it;</item>
///   <item>a free builder with an empty hold takes a load (<see cref="ConstructionPlanner"/>): a ship with the construction
///   role, which every ship that can build has while the gate needs materials, or the largest holds, as many as
///   <c>Construction.Ships</c> when that is above 0 (D65, D93); with the role board off, the plan picks them by the same rule.
///   A load is a full hold, or what the gate still needs once what every other trip carries or goes to buy is counted, at a
///   market whose supply isn't SCARCE or LIMITED (D66) and where no other trip is on its way to buy it (D80), bought in
///   batches of its trade volume (D81);</item>
///   <item>supplying pays nothing, so a load is judged as a ship purchase (D64): it keeps the credit reserve
///   (<see cref="IBudgetPolicy"/>), and comes after the cargo ships in the order ships are bought in
///   (<see cref="PurchaseTier.Construction"/>), which it tells on every pass, so probes and further ships wait until the
///   gate is done. It says whether the load waits for its markets: then the mining plan's drones for the gate's smelters
///   may be bought meanwhile (D92). A trip holds back what its cargo costs from the moment it starts until it buys, as a
///   trade trip does (D57);</item>
///   <item>a builder that gets no load stays free, and the trading plan, which comes next, gives it a trade.</item>
/// </list>
/// </summary>
public sealed class ConstructionPlanService(
    IShipRepository ships,
    IShipGoalRepository goals,
    IShipAssignmentRepository assignments,
    ISettingsRepository settings,
    IPlanRepository plans,
    ITradeContextReader tradeContexts,
    IConstructionSites sites,
    IBudgetPolicy budget,
    IPurchaseOrder purchaseOrder,
    ConstructionRetries retries,
    PassedOverShips passedOver,
    ILogger<ConstructionPlanService> logger) : IConstructionPlanService
{
    /// <summary>A free builder bought no load: a purchase before it in the order ships are bought in (D64).</summary>
    internal const string WaitingForPurchaseOrder = "purchase_order";

    /// <summary>A free builder bought no load: it would dip into the credit reserve (D64).</summary>
    internal const string WaitingForCredits = "waiting_for_credits";

    private const string HeldCargo = "held_cargo";
    private const string Purchase = "purchase";

    private static readonly JsonSerializerOptions CompareOptions = new();

    /// <inheritdoc />
    public async Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default)
    {
        var now = TimeProvider.System.GetUtcNow();
        var board = await FleetRoleBoard.ReadAsync(settings, plans, cancellationToken);

        // B71: the goals before the ships, so a trip that ends meanwhile leaves no builder free with the load it supplied.
        var read = await FleetGoals.ReadAsync(ships, goals, cancellationToken);
        var fleet = read.Fleet;
        var withAssignment = (await assignments.GetAllActiveAsync(cancellationToken))
            .Where(assignment => !assignment.CompletedAt.HasValue)
            .Select(assignment => assignment.ShipSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var free = new List<ShipModel>();
        var trips = new List<SupplyConstructionGoal>();
        foreach (var ship in fleet)
        {
            var goal = read.GoalOf(ship.Symbol);
            if (goal is SupplyConstructionGoal trip)
            {
                trips.Add(trip);
            }

            if (!FleetRoles.IsProbe(ship) && FleetRoles.IsFree(ship, goal, withAssignment.Contains(ship.Symbol)))
            {
                free.Add(ship);
            }
        }

        var builders = await BuildersAsync(board, fleet, cancellationToken);
        var pass = new Pass(free, trips, builders);

        // D68: only the home system's jump gate, and only while one of our ships is there to build it.
        var home = await sites.HomeSystemAsync(cancellationToken);
        if (home.Length > 0 && fleet.Any(ship => !FleetRoles.IsProbe(ship) && string.Equals(ship.SystemSymbol, home, StringComparison.OrdinalIgnoreCase)))
        {
            var homeSites = await sites.NeedingMaterialsAsync(cancellationToken);
            if (homeSites.Count > 0)
            {
                var map = (await tradeContexts.ReadAsync(home, cancellationToken)).Map;
                foreach (var site in homeSites)
                {
                    await DeliverHeldMaterialsAsync(pass, site, home, now, cancellationToken);
                    pass.Sites.Add((site, map, home));
                }
            }
        }

        await BuyLoadsAsync(pass, cancellationToken);

        // B63: the trading plan, later in the tick, gives a route only to a builder this pass had no load for.
        passedOver.Record(
            AutomationPlan.Construction,
            builders.Select(ship => ship.Symbol),
            pass.FreeBuilders().Select(ship => ship.Symbol));

        await SaveStateAsync(pass, now, cancellationToken);
    }

    /// <summary>
    /// The ships that build (D65, D93): with the role board on, those with the construction role; with it off, those of each
    /// system that can and don't survey (D20), the largest holds, as many as <c>Construction.Ships</c> when that is above 0.
    /// </summary>
    private async Task<IReadOnlyList<ShipModel>> BuildersAsync(FleetRoleBoard board, IReadOnlyList<ShipModel> fleet, CancellationToken cancellationToken)
    {
        if (board.RolesOn)
        {
            return [.. fleet.Where(board.IsBuilder).OrderByDescending(ship => ship.CargoCapacity).ThenBy(ship => ship.Symbol, StringComparer.Ordinal)];
        }

        var count = await settings.ThresholdAsync(RoleSettings.ConstructionShipsSetting, RoleSettings.DefaultConstructionShips, cancellationToken);
        return [.. fleet
            .Where(ship => !board.IsSurveyor(ship))
            .GroupBy(ship => ship.SystemSymbol ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .SelectMany(system => ConstructionPlanner.PickBuilders(system, count))];
    }

    /// <summary>
    /// Every free ship in the site's system that holds a material the site still needs takes it there, whatever its role:
    /// as much of it as the site still needs. A ship whose supply the site refused lately waits (<see cref="ConstructionRetries"/>).
    /// </summary>
    private async Task DeliverHeldMaterialsAsync(Pass pass, ConstructionSiteModel site, string system, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var ship in pass.Free
            .Where(ship => string.Equals(ship.SystemSymbol, system, StringComparison.OrdinalIgnoreCase) && ship.CargoCurrent > 0)
            .OrderBy(ship => ship.Symbol, StringComparer.Ordinal)
            .ToList())
        {
            var needs = ConstructionPlanner.Needs(site, pass.Trips);
            if (!ConstructionPlanner.TryFindDelivery(ship, needs, out var tradeSymbol, out var units)
                || !retries.MayTry(ship.Symbol, tradeSymbol, now))
            {
                continue;
            }

            var goal = new SupplyConstructionGoal
            {
                TradeSymbol = tradeSymbol,
                ConstructionSiteWaypointSymbol = site.WaypointSymbol,
                BuyWaypointSymbol = ship.WaypointSymbol ?? string.Empty,
                Units = units,
                CargoBought = true,
            };
            await goals.SetActiveGoalAsync(ship.Symbol, goal, cancellationToken);
            pass.Started(ship, goal);
            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} takes the {Units} {TradeSymbol} it holds from {BuyWaypoint} to {WaypointSymbol} ({Reason}).",
                JournalEvents.ConstructionStarted,
                ship.Symbol,
                units,
                tradeSymbol,
                goal.BuyWaypointSymbol,
                site.WaypointSymbol,
                HeldCargo);
        }
    }

    /// <summary>
    /// Tells the order ships are bought in what construction would buy next (D64), and gives each free builder with an empty
    /// hold the first load its credits pay for, while nothing comes before it in the order.
    /// </summary>
    private async Task BuyLoadsAsync(Pass pass, CancellationToken cancellationToken)
    {
        var spendable = (await budget.EvaluateAsync(0, cancellationToken)).SpendableCredits;

        // D80: one buyer of a material at a market at a time, a trade trip or a construction trip, those this pass starts too.
        var tradeTrips = (await goals.GetActiveTradeGoalsAsync(cancellationToken)).Values.ToList();
        HeldBuys HeldBuysNow() => HeldBuys.Of(tradeTrips, pass.Trips);

        var need = PurchaseNeed.None;
        foreach (var (site, map, system) in pass.Sites)
        {
            if (pass.Builders.FirstOrDefault(builder => string.Equals(builder.SystemSymbol, system, StringComparison.OrdinalIgnoreCase)) is { } first
                && NextLoad(map, first, site, ConstructionPlanner.Needs(site, pass.Trips), spendable, HeldBuysNow()) is { } next)
            {
                // D92: a load that waits for its markets lets the gate's miners be bought meanwhile.
                need = new PurchaseNeed(PurchaseTier.Construction, next.Load.TradeSymbol, next.Load.BuyWaypointSymbol, next.Load.Cost)
                {
                    WaitsForMarkets = next.WaitsForMarkets,
                };
                break;
            }
        }

        if (!await purchaseOrder.ReportAsync(AutomationPlan.Construction, need, cancellationToken))
        {
            if (need.Tier != PurchaseTier.None && pass.FreeBuilders().Any(builder => builder.CargoCurrent == 0))
            {
                pass.Waiting = WaitingForPurchaseOrder;
            }

            return;
        }

        // The builders a load waits for, on a trip or not: the ShipLeftIdle rule counts it as work for them once they are
        // free, which only a plan that has stopped would leave undone. A free one that holds other cargo waits for the trading
        // plan to sell it.
        foreach (var (site, map, system) in pass.Sites)
        {
            var needs = ConstructionPlanner.Needs(site, pass.Trips);
            pass.Ready.AddRange(pass.Builders
                .Where(builder => string.Equals(builder.SystemSymbol, system, StringComparison.OrdinalIgnoreCase)
                    && !(builder.CargoCurrent > 0 && pass.FreeBuilders().Any(other => other.Symbol.Equals(builder.Symbol, StringComparison.OrdinalIgnoreCase)))
                    && ConstructionPlanner.Loads(map, AsIfEmptyWhereItGoes(builder), site.WaypointSymbol, needs, HeldBuysNow()).Any(load => load.Cost <= spendable))
                .Select(builder => builder.Symbol));
        }

        foreach (var (site, map, system) in pass.Sites)
        {
            foreach (var builder in pass.FreeBuilders()
                .Where(builder => builder.CargoCurrent == 0 && string.Equals(builder.SystemSymbol, system, StringComparison.OrdinalIgnoreCase))
                .ToList())
            {
                var needs = ConstructionPlanner.Needs(site, pass.Trips);
                var heldBuys = HeldBuysNow();
                var loads = ConstructionPlanner.Loads(map, builder, site.WaypointSymbol, needs, heldBuys);
                var load = loads.FirstOrDefault(candidate => candidate.Cost <= spendable);
                if (load is null)
                {
                    pass.Waiting = loads.Count > 0 ? WaitingForCredits : ConstructionPlanner.WhyNoLoad(map, builder, site.WaypointSymbol, needs, heldBuys);
                    continue;
                }

                var goal = new SupplyConstructionGoal
                {
                    TradeSymbol = load.TradeSymbol,
                    ConstructionSiteWaypointSymbol = site.WaypointSymbol,
                    BuyWaypointSymbol = load.BuyWaypointSymbol,
                    Units = load.Units,
                    ReservedCredits = load.CargoCost,
                };
                await goals.SetActiveGoalAsync(builder.Symbol, goal, cancellationToken);
                pass.Started(builder, goal);
                spendable -= load.CargoCost;
                logger.LogInformation(
                    "{EventKind:l}: ship {ShipSymbol} buys {Units} {TradeSymbol} at {BuyWaypoint} ({BuyPrice} each) for {WaypointSymbol}: about {Cost} credits with {FuelCost} for fuel ({Reason}).",
                    JournalEvents.ConstructionStarted,
                    builder.Symbol,
                    load.Units,
                    load.TradeSymbol,
                    load.BuyWaypointSymbol,
                    load.UnitPrice,
                    site.WaypointSymbol,
                    load.Cost,
                    load.FuelCost,
                    Purchase);
            }
        }
    }

    /// <summary>
    /// What a builder would buy next, for the order ships are bought in: as if its hold were empty and it were where it is
    /// going, the first load the credits pay for, else the first it may buy, else the first it would buy once the supply
    /// allows it and no other trip is on its way to buy it there, which waits for its markets (D92). None when no market it
    /// can reach sells what the site needs.
    /// </summary>
    private static (ConstructionLoad Load, bool WaitsForMarkets)? NextLoad(TradeMarketMap map, ShipModel builder, ConstructionSiteModel site, IReadOnlyList<MaterialNeed> needs, long spendable, HeldBuys heldBuys)
    {
        var empty = AsIfEmptyWhereItGoes(builder);
        var loads = ConstructionPlanner.Loads(map, empty, site.WaypointSymbol, needs, heldBuys);
        if ((loads.FirstOrDefault(load => load.Cost <= spendable) ?? loads.FirstOrDefault()) is { } load)
        {
            return (load, false);
        }

        return ConstructionPlanner.Loads(map, empty, site.WaypointSymbol, needs, strict: false).FirstOrDefault() is { } later
            ? (later, true)
            : null;
    }

    /// <summary>
    /// A builder as it will be for its next load: where it is going, its trip's cargo supplied. A ship in transit lands with
    /// the fuel the flight leaves it (the API takes a flight's fuel when it sets off) and docks to supply or sell its cargo,
    /// so where fuel is sold it fills its tank before it flies on (B65): from the gate with the fuel left after the flight
    /// there, no market was in reach, and the order heard of no load while the builder flew one.
    /// </summary>
    private static ShipModel AsIfEmptyWhereItGoes(ShipModel builder)
        => builder.LocalStatus == ShipLocalStatus.InTransit
            ? builder with
            {
                WaypointSymbol = MiningPlanner.Position(builder),
                Status = "DOCKED",
                DestWaypointSymbol = null,
                ArrivesAt = null,
                CargoCurrent = 0,
                CargoInventory = [],
            }
            : builder with { CargoCurrent = 0, CargoInventory = [] };

    /// <summary>Records the sites, the builders and why no load was bought. Only a change is written.</summary>
    private async Task SaveStateAsync(Pass pass, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var state = new ConstructionPlanState
        {
            Sites = [.. pass.Sites.Select(entry => new ConstructionSiteState
            {
                WaypointSymbol = entry.Site.WaypointSymbol,
                Materials = [.. ConstructionPlanner.Needs(entry.Site, pass.Trips).Select(need => new ConstructionMaterialState
                {
                    TradeSymbol = need.TradeSymbol,
                    Required = need.Required,
                    Fulfilled = need.Fulfilled,
                    OnTheWay = need.OnTheWay,
                })],
            })],
            BuilderShipSymbols = [.. pass.Builders.Select(builder => builder.Symbol)],
            ReadyShipSymbols = [.. pass.Ready.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)],
            Waiting = pass.Waiting,
            UpdatedAt = now,
        };

        var existing = await plans.GetAsync<ConstructionPlanState>(PlanTypes.Construction, cancellationToken);
        if (existing is not null
            && JsonSerializer.Serialize(existing with { UpdatedAt = default }, CompareOptions) == JsonSerializer.Serialize(state with { UpdatedAt = default }, CompareOptions))
        {
            return;
        }

        await plans.UpsertAsync(PlanTypes.Construction, state, cancellationToken);
    }

    /// <summary>What one pass works with: the free ships, the construction trips, the builders, and what it found.</summary>
    private sealed class Pass(List<ShipModel> free, List<SupplyConstructionGoal> trips, IReadOnlyList<ShipModel> builders)
    {
        public List<ShipModel> Free { get; } = free;

        public List<SupplyConstructionGoal> Trips { get; } = trips;

        public IReadOnlyList<ShipModel> Builders { get; } = builders;

        public List<(ConstructionSiteModel Site, TradeMarketMap Map, string System)> Sites { get; } = [];

        public List<string> Ready { get; } = [];

        public string Waiting { get; set; } = string.Empty;

        /// <summary>The builders that are free now.</summary>
        public IEnumerable<ShipModel> FreeBuilders()
            => Free.Where(ship => Builders.Any(builder => builder.Symbol.Equals(ship.Symbol, StringComparison.OrdinalIgnoreCase)));

        /// <summary>A ship took a construction trip: it is no longer free, and the trip's units are on their way.</summary>
        public void Started(ShipModel ship, SupplyConstructionGoal trip)
        {
            Free.RemoveAll(other => other.Symbol.Equals(ship.Symbol, StringComparison.OrdinalIgnoreCase));
            Trips.Add(trip);
        }
    }
}
