using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Probes;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Automation;

/// <summary>The probe plan (PLAN.md slice 6.3).</summary>
public interface IProbeDeploymentPlanService
{
    /// <summary>
    /// One pass of the plan: buys a probe while the system has more markets than probes and the credits
    /// allow, sends the nearest free probe to each shipyard that calls for a ship, and every other free probe
    /// to the market that needs one most.
    /// </summary>
    /// <param name="cancellationToken">Stops the pass.</param>
    /// <returns>A task that completes when the pass is done.</returns>
    Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The probe plan (PLAN.md slice 6.3, D29, D30), in the headquarters' system. Long term every market has a
/// probe of its own, which the market watch uses to keep its prices fresh. Each pass:
/// <list type="number">
///   <item>while there are fewer probes than markets, it buys a SHIP_PROBE at the shipyard that sells it for
///   the least, as long as the credits stay at the reserve (<c>FleetExpansion.MinCreditReserve</c>, D29), and nothing
///   comes before it in the order ships are bought in (<see cref="IPurchaseOrder"/>, D43): the contract's drone, a
///   surveyor, a drone for each scarce mineral and the cargo ships of <c>Trade.ShipPurchases</c> go first;</item>
///   <item>it flies the free probes (<see cref="ProbePlanner"/>): the nearest to each shipyard where a purchase
///   waits for one of our ships (<see cref="ShipyardCalls"/>, D30), the others between nearby markets, the
///   one whose prices are oldest first, until there is a probe for every market; then each market keeps one, and
///   the spares settle at the markets without one (B69).</item>
/// </list>
/// A probe is any probe in the system, the starting one included (B25); a probe in flight, or with a flight
/// to make, keeps its market, so no target is bought or flown to twice (B15).
/// </summary>
public sealed class ProbeDeploymentPlanService(
    IProbeDeploymentPlanRepository plans,
    IAgentRepository agents,
    IWaypointRepository waypoints,
    IMarketRepository markets,
    IShipRepository ships,
    IShipGoalRepository goals,
    IShipyardRepository shipyards,
    IShipPurchaseService shipPurchases,
    ShipyardCalls calls,
    ISettingsRepository settings,
    IPurchaseOrder purchaseOrder,
    ILogger<ProbeDeploymentPlanService> logger) : IProbeDeploymentPlanService
{
    /// <summary>The ship the plan buys (D29).</summary>
    public const string ProbeShipType = "SHIP_PROBE";

    /// <summary>How old a market's prices may get when <c>Market.RefreshMinutes</c> gives no interval.</summary>
    internal const int DefaultDueMinutes = 5;

    private static readonly JsonSerializerOptions CompareOptions = new();

    /// <inheritdoc />
    public async Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default)
    {
        // Slice 6.10b (D43): a pass that buys no probe says so; one that would, says so where it would buy.
        await purchaseOrder.ReportAsync(AutomationPlan.ProbeDeployment, PurchaseNeed.None, cancellationToken);

        var agent = await agents.GetAsync(cancellationToken);
        if (agent is null || string.IsNullOrWhiteSpace(agent.HeadquartersSymbol))
        {
            logger.LogDebug("Probe plan: the agent or its headquarters isn't cached yet.");
            return;
        }

        var systemSymbol = SystemOf(agent.HeadquartersSymbol);
        var systemWaypoints = await waypoints.GetBySystemAsync(systemSymbol, cancellationToken);
        var marketSymbols = systemWaypoints
            .Where(waypoint => waypoint.HasMarket)
            .Select(waypoint => waypoint.Symbol)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (marketSymbols.Count == 0)
        {
            logger.LogDebug("Probe plan: no markets cached in system {System}.", systemSymbol);
            return;
        }

        var existing = await plans.GetAsync(cancellationToken);
        var fleet = await ships.GetAllAsync(cancellationToken);
        var (purchase, shipyard, price) = await BuyProbeAsync(systemSymbol, ProbesIn(fleet, systemSymbol).Count, marketSymbols.Count, cancellationToken);
        if (purchase == ProbePurchaseStatus.Bought)
        {
            fleet = await ships.GetAllAsync(cancellationToken);
        }

        var now = TimeProvider.System.GetUtcNow();
        var minutes = await settings.GetAsync<int>(MarketWatchService.RefreshMinutesSetting, cancellationToken);
        var dueAfter = TimeSpan.FromMinutes(minutes > 0 ? minutes : DefaultDueMinutes);
        // A market without prices counts as never seen, so a probe goes there first (B62).
        var lastSeen = (await markets.GetAllFreshnessAsync(cancellationToken))
            .Where(freshness => freshness.HasPrices)
            .GroupBy(freshness => freshness.WaypointSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Max(freshness => freshness.LastObservedAt), StringComparer.OrdinalIgnoreCase);
        var probes = new List<ProbeShip>();
        foreach (var probe in ProbesIn(fleet, systemSymbol))
        {
            probes.Add(await ProbeShipAsync(probe, cancellationToken));
        }

        var snapshot = new ProbeSnapshot
        {
            Positions = systemWaypoints
                .GroupBy(waypoint => waypoint.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => new WaypointPosition(group.First().X, group.First().Y), StringComparer.OrdinalIgnoreCase),
            Markets = [.. marketSymbols.Select(symbol => new ProbeMarket(symbol, lastSeen.GetValueOrDefault(symbol, DateTimeOffset.MinValue)))],
            Probes = probes,
            ShipsAt = fleet
                .Where(ship => ship.LocalStatus != ShipLocalStatus.InTransit && !string.IsNullOrWhiteSpace(ship.WaypointSymbol))
                .Select(ship => ship.WaypointSymbol!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            Calls = calls.Open(now),
            Now = now,
            DueAfter = dueAfter,
        };

        var moves = ProbePlanner.Plan(snapshot);
        foreach (var move in moves)
        {
            await SendAsync(move, cancellationToken);
        }

        var state = State(existing, systemSymbol, snapshot, fleet, moves, purchase, shipyard, price);
        if (existing is null)
        {
            logger.LogInformation(
                "{EventKind:l}: {Plan} plan for system {System}: {Probes} probes for {Markets} markets.",
                JournalEvents.PlanStarted,
                AutomationPlan.ProbeDeployment,
                systemSymbol,
                state.Probes,
                state.Markets.Count);
        }

        if (purchase == ProbePurchaseStatus.WaitingForCredits && existing?.Purchase != ProbePurchaseStatus.WaitingForCredits)
        {
            logger.LogInformation(
                "{EventKind:l}: {Plan} plan waits ({Reason}): a probe costs {Price} at {WaypointSymbol}, and the purchase must leave the credit reserve.",
                JournalEvents.PlanBlocked,
                AutomationPlan.ProbeDeployment,
                "waiting_for_credits",
                price,
                shipyard);
        }

        await SaveStateAsync(existing, state, cancellationToken);
    }

    private static List<ShipModel> ProbesIn(IReadOnlyList<ShipModel> fleet, string systemSymbol)
        => [.. fleet.Where(ship => FleetRoles.IsProbe(ship) && string.Equals(ship.SystemSymbol, systemSymbol, StringComparison.OrdinalIgnoreCase))];

    private static string SystemOf(string waypointSymbol)
    {
        var lastDash = waypointSymbol.LastIndexOf('-');
        return lastDash > 0 ? waypointSymbol[..lastDash] : waypointSymbol;
    }

    /// <summary>The engine's speed from the cached engine; a probe bought since the last restart has none yet.</summary>
    private static int EngineSpeed(ShipModel ship) => FleetRoles.EngineSpeed(ship, ProbePlanner.DefaultProbeSpeed);

    /// <summary>
    /// Buys the next probe (D29) while the system has more markets than probes, at the shipyard that sells it
    /// for the least, when nothing comes before it in the order ships are bought in (D43).
    /// <see cref="IShipPurchaseService"/> keeps the credit reserve, and calls for a ship when none of ours is at the
    /// shipyard (D30).
    /// </summary>
    private async Task<(ProbePurchaseStatus Status, string Shipyard, long Price)> BuyProbeAsync(
        string systemSymbol,
        int probes,
        int marketCount,
        CancellationToken cancellationToken)
    {
        var offer = (await shipyards.GetAllAsync(cancellationToken))
            .Where(candidate => candidate.SystemSymbol.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase))
            .SelectMany(candidate => candidate.Ships
                .Where(ship => ship.Type.Equals(ProbeShipType, StringComparison.OrdinalIgnoreCase) && ship.PurchasePrice > 0)
                .Select(ship => (Shipyard: candidate.WaypointSymbol, Price: ship.PurchasePrice)))
            .OrderBy(candidate => candidate.Price)
            .ThenBy(candidate => candidate.Shipyard, StringComparer.Ordinal)
            .ToList();
        var (shipyard, price) = offer.Count > 0 ? offer[0] : (string.Empty, 0L);
        if (probes >= marketCount)
        {
            return (ProbePurchaseStatus.EveryMarketHasOne, shipyard, price);
        }

        if (offer.Count == 0)
        {
            return (ProbePurchaseStatus.NoShipyardSellsProbes, shipyard, price);
        }

        if (!await purchaseOrder.ReportAsync(
            AutomationPlan.ProbeDeployment,
            new PurchaseNeed(PurchaseTier.Probes, ProbeShipType, shipyard, price),
            cancellationToken))
        {
            return (ProbePurchaseStatus.WaitingForAnotherPurchase, shipyard, price);
        }

        ShipPurchaseResult result;
        try
        {
            result = await shipPurchases.TryPurchaseAsync(ProbeShipType, shipyard, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The probes fly on: a purchase that keeps failing shows as RepeatingError.
            logger.LogWarning(ex, "Probe plan: buying a {ShipType} at {WaypointSymbol} failed.", ProbeShipType, shipyard);
            return (ProbePurchaseStatus.None, shipyard, price);
        }

        var status = result switch
        {
            { IsSuccess: true } => ProbePurchaseStatus.Bought,
            { Failure: ShipPurchaseFailure.OverBudget } => ProbePurchaseStatus.WaitingForCredits,
            { Failure: ShipPurchaseFailure.NoShipAtShipyard } => ProbePurchaseStatus.WaitingForAShipAtTheShipyard,
            { Failure: ShipPurchaseFailure.PriceUnknown } => ProbePurchaseStatus.NoShipyardSellsProbes,
            _ => ProbePurchaseStatus.None,
        };
        return (status, shipyard, result.EstimatedCost > 0 ? result.EstimatedCost : price);
    }

    /// <summary>
    /// The probe as the plan sees it: free, or holding the market it flies to, or will fly to once its goal's
    /// next step runs.
    /// </summary>
    private async Task<ProbeShip> ProbeShipAsync(ShipModel probe, CancellationToken cancellationToken)
    {
        var goal = await goals.GetActiveGoalAsync(probe.Symbol, cancellationToken);
        var flight = goal is DeployProbeGoal { Status: not GoalStatus.Completed and not GoalStatus.Blocked } deploy
            ? deploy.TargetWaypointSymbol
            : string.Empty;
        var waypoint = probe.LocalStatus == ShipLocalStatus.InTransit
            ? FirstOf(probe.DestWaypointSymbol, flight, probe.WaypointSymbol)
            : FirstOf(flight, probe.WaypointSymbol);
        return new ProbeShip(probe.Symbol, waypoint, FleetRoles.IsFree(probe, goal, hasOpenAssignment: false), EngineSpeed(probe));
    }

    private static string FirstOf(params string?[] symbols)
        => symbols.FirstOrDefault(symbol => !string.IsNullOrWhiteSpace(symbol)) ?? string.Empty;

    private async Task SendAsync(ProbeMove move, CancellationToken cancellationToken)
    {
        await goals.SetActiveGoalAsync(
            move.ShipSymbol,
            new DeployProbeGoal { TargetWaypointSymbol = move.WaypointSymbol, ForPurchase = move.ForPurchase },
            cancellationToken);
        if (move.ForPurchase)
        {
            logger.LogInformation(
                "{EventKind:l}: probe {ShipSymbol} flies to {WaypointSymbol}, where a {ShipType} purchase waits for one of our ships.",
                JournalEvents.ProbeCalled,
                move.ShipSymbol,
                move.WaypointSymbol,
                move.ShipType);
            return;
        }

        logger.LogDebug(
            "Probe plan: probe {ShipSymbol} flies to {WaypointSymbol}, the market that needs a probe most.",
            move.ShipSymbol,
            move.WaypointSymbol);
    }

    private static ProbeDeploymentPlanState State(
        ProbeDeploymentPlanState? existing,
        string systemSymbol,
        ProbeSnapshot snapshot,
        IReadOnlyList<ShipModel> fleet,
        IReadOnlyList<ProbeMove> moves,
        ProbePurchaseStatus purchase,
        string shipyard,
        long price)
    {
        // Where each probe is, or flies to, once this pass's flights are given.
        var holds = snapshot.Probes
            .Select(probe => (
                probe.Symbol,
                Waypoint: moves.FirstOrDefault(move => string.Equals(move.ShipSymbol, probe.Symbol, StringComparison.Ordinal))?.WaypointSymbol ?? probe.WaypointSymbol))
            .ToList();
        var probeSymbols = snapshot.Probes.Select(probe => probe.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shipsAt = fleet
            .Where(ship => !probeSymbols.Contains(ship.Symbol)
                && ship.LocalStatus != ShipLocalStatus.InTransit
                && !string.IsNullOrWhiteSpace(ship.WaypointSymbol))
            .Select(ship => ship.WaypointSymbol!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string ProbeAt(string waypointSymbol) => holds
            .Where(hold => hold.Waypoint.Equals(waypointSymbol, StringComparison.OrdinalIgnoreCase))
            .Select(hold => hold.Symbol)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault() ?? string.Empty;

        return new ProbeDeploymentPlanState
        {
            PlanId = existing?.PlanId ?? Guid.NewGuid(),
            SystemSymbol = systemSymbol,
            Probes = snapshot.Probes.Count,
            Markets =
            [
                .. snapshot.Markets.Select(market =>
                {
                    var view = new ProbeMarketState
                    {
                        WaypointSymbol = market.WaypointSymbol,
                        ProbeSymbol = ProbeAt(market.WaypointSymbol),
                        WatchedByShip = shipsAt.Contains(market.WaypointSymbol),
                    };
                    return view.IsUnwatched
                        ? view with { DueAt = market.LastSeenAt == DateTimeOffset.MinValue ? DateTimeOffset.MinValue : market.LastSeenAt + snapshot.DueAfter }
                        : view;
                }),
            ],
            Purchase = purchase,
            NextProbeShipyard = shipyard,
            NextProbePrice = price,
            Calls =
            [
                .. snapshot.Calls
                    .Where(call => snapshot.Positions.ContainsKey(call.WaypointSymbol))
                    .Select(call => new ProbeCallState
                    {
                        WaypointSymbol = call.WaypointSymbol,
                        ShipType = call.ShipType,
                        ProbeSymbol = ProbeAt(call.WaypointSymbol),
                    }),
            ],
            CreatedAt = existing?.CreatedAt ?? snapshot.Now,
            UpdatedAt = snapshot.Now,
        };
    }

    /// <summary>Records the view. Only a change is written: the tick runs every 5 seconds.</summary>
    private async Task SaveStateAsync(ProbeDeploymentPlanState? existing, ProbeDeploymentPlanState state, CancellationToken cancellationToken)
    {
        if (existing is not null && Comparable(existing) == Comparable(state))
        {
            return;
        }

        await plans.UpsertAsync(state, cancellationToken);
    }

    private static string Comparable(ProbeDeploymentPlanState state)
        => JsonSerializer.Serialize(state with { PlanId = Guid.Empty, CreatedAt = default, UpdatedAt = default }, CompareOptions);
}
