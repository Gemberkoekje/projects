using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Probes;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Automation;

/// <summary>The probe plan (PLAN.md slices 6.3 and 6.28).</summary>
public interface IProbeDeploymentPlanService
{
    /// <summary>
    /// One pass of the plan: sends a spare probe to a system short of one, buys a probe while a system it serves has more
    /// markets than probes and the credits allow, sends the nearest free probe to each shipyard that calls for a ship, and
    /// every other free probe to the market of its system that needs one most.
    /// </summary>
    /// <param name="cancellationToken">Stops the pass.</param>
    /// <returns>A task that completes when the pass is done.</returns>
    Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The probe plan (PLAN.md slice 6.3, D29, D30; slice 6.28, D97). Long term every market has a probe of its own, which the
/// market watch uses to keep its prices fresh: those of the headquarters' system first, then those of each explored system
/// the built gates reach, the nearest first (asked on 2026-10-06: "Trade reach as a priority, all explored markets when money
/// allows - I have minimum required credit reserves for a reason. It should also check whether the probes are in SCARCE
/// supply and not buy them if they are."). Each pass:
/// <list type="number">
///   <item>a system with more probes than markets lends its spares to the first system short of one (B69, slice 6.28), before
///   a probe is bought for it;</item>
///   <item>while a system has more markets than probes, it buys a SHIP_PROBE for the first such system, at the shipyard where
///   it costs least with the antimatter of the jumps to that system counted, never where SHIP_PROBE is SCARCE (D97), as long
///   as the credits stay at the reserve (<c>FleetExpansion.MinCreditReserve</c>, D29), and nothing comes before it in the
///   order ships are bought in (<see cref="IPurchaseOrder"/>, D43): home's and those of the systems within
///   <c>Trade.MaxHaulDistance</c> jumps of home at the probe tier, the others' last (<see cref="PurchaseTier.FarProbes"/>).
///   A purchase needs one of our ships at the shipyard (D30), so a shipyard abroad counts once a probe of ours is in its
///   system: the first probe to arrive lets the plan buy the rest there. One bought for another system flies there at once;</item>
///   <item>it flies the free probes of each system (<see cref="ProbePlanner"/>): the nearest to each shipyard where a purchase
///   waits for one of our ships (<see cref="ShipyardCalls"/>, D30); one to park at each shipyard (slice 6.32, D110); the others
///   between nearby markets, the one whose prices are oldest first, until there is a probe for every market; then each market
///   keeps one, and the spares settle at the markets without one (B69).</item>
/// </list>
/// Slice 6.32 (asked on 2026-10-06: "have probes deployed to shipyards with priority, with shipyards with explorer ships being
/// even higher priority than that?"): within each tier the shipyards come first, across the systems (D109), those that sell
/// SHIP_EXPLORER before the others, and those at the probe tier wherever the gates reach them (D111); for the spares lent, the
/// probes bought (<see cref="Wants"/>), and where a probe that comes into a system flies first (<see cref="ProbePlanner.Entry"/>).
/// A probe is any probe, the starting one included (B25); a probe in flight, or with a flight to make, counts for the system
/// it goes to and keeps its market there, so no target is bought or flown to twice (B15). A flight to another system jumps
/// through the gates on the way (<see cref="Goals.Executors.GoalJumps"/>, D101), each jump's antimatter paid only while the
/// credits after it stay at the floor (D63): the plan sends no probe abroad where they wouldn't.
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
    IGateNetwork gates,
    ILogger<ProbeDeploymentPlanService> logger) : IProbeDeploymentPlanService
{
    /// <summary>The ship the plan buys (D29).</summary>
    public const string ProbeShipType = "SHIP_PROBE";

    /// <summary>How old a market's prices may get when <c>Market.RefreshMinutes</c> gives no interval.</summary>
    internal const int DefaultDueMinutes = 5;

    private const string ScarceSupply = "SCARCE";
    private const string AntimatterSymbol = "ANTIMATTER";

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

        var home = WaypointSymbols.SystemOf(agent.HeadquartersSymbol);
        var now = TimeProvider.System.GetUtcNow();
        var minutes = await settings.GetAsync<int>(MarketWatchService.RefreshMinutesSetting, cancellationToken);
        var fleet = await ships.GetAllAsync(cancellationToken);
        var cachedShipyards = await shipyards.GetAllAsync(cancellationToken);
        var pass = new Pass
        {
            Home = home,
            Now = now,
            DueAfter = TimeSpan.FromMinutes(minutes > 0 ? minutes : DefaultDueMinutes),
            Network = await gates.ReadAsync(cancellationToken),
            Credits = agent.Credits,
            Floor = Math.Max(0, await settings.GetAsync<long>(CreditReserve.FloorSetting, cancellationToken)),

            // A market without prices counts as never seen, so a probe goes there first (B62).
            LastSeen = (await markets.GetAllFreshnessAsync(cancellationToken))
                .Where(freshness => freshness.HasPrices)
                .GroupBy(freshness => freshness.WaypointSymbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Max(freshness => freshness.LastObservedAt), StringComparer.OrdinalIgnoreCase),
            Calls = calls.Open(now),
            Shipyards = cachedShipyards,
            ExplorerShipyards = cachedShipyards
                .Where(shipyard => shipyard.ShipTypes.Concat(shipyard.Ships.Select(ship => ship.Type))
                    .Any(type => type.Equals(FleetRoles.ExplorerShipType, StringComparison.OrdinalIgnoreCase)))
                .Select(shipyard => shipyard.WaypointSymbol)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
        };

        var probes = new List<FleetProbe>();
        foreach (var probe in fleet.Where(FleetRoles.IsProbe))
        {
            probes.Add(await ProbeAsync(probe, cancellationToken));
        }

        await ServeAsync(pass, probes, await ReachAsync(cancellationToken), cancellationToken);
        if (pass.Systems.Count == 0 || pass.Systems[0].Markets.Count == 0)
        {
            logger.LogDebug("Probe plan: no markets cached in system {System}.", home);
            return;
        }

        var existing = await plans.GetAsync(cancellationToken);
        var probeSymbols = probes.Select(probe => probe.View.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        pass.ShipsAt = WaypointsWithShips(fleet);
        pass.OthersAt = WaypointsWithShips(fleet.Where(ship => !probeSymbols.Contains(ship.Symbol)));
        if (pass.Network is not null && pass.Systems.Count > 1)
        {
            pass.Antimatter = (await markets.GetAllSnapshotsAsync(cancellationToken))
                .Select(market => (market.WaypointSymbol, Good: market.TradeGoods.FirstOrDefault(good => good.Symbol.Equals(AntimatterSymbol, StringComparison.OrdinalIgnoreCase))))
                .Where(market => market.Good is not null)
                .GroupBy(market => market.WaypointSymbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => (long)group.First().Good!.PurchasePrice, StringComparer.OrdinalIgnoreCase);
        }

        LendSpares(pass);
        var purchase = await BuyProbeAsync(pass, cancellationToken);

        // A purchase where none of our ships is calls for one (D30): the probes answer it in this pass.
        pass.Calls = calls.Open(now);
        foreach (var (shipSymbol, market) in pass.Sends)
        {
            await goals.SetActiveGoalAsync(shipSymbol, new DeployProbeGoal { TargetWaypointSymbol = market }, cancellationToken);
        }

        var moves = new List<(ServedSystem System, ProbeMove Move)>();
        foreach (var system in pass.Systems)
        {
            foreach (var move in ProbePlanner.Plan(Snapshot(pass, system)))
            {
                await SendAsync(move, cancellationToken);
                moves.Add((system, move));
            }
        }

        var state = State(existing, pass, moves, purchase);
        if (existing is null)
        {
            logger.LogInformation(
                "{EventKind:l}: {Plan} plan for system {System}: {Probes} probes for {Markets} markets.",
                JournalEvents.PlanStarted,
                AutomationPlan.ProbeDeployment,
                home,
                state.Probes,
                state.Systems.Sum(system => system.Markets.Count));
        }

        if (purchase.Status == ProbePurchaseStatus.WaitingForCredits && existing?.Purchase != ProbePurchaseStatus.WaitingForCredits)
        {
            logger.LogInformation(
                "{EventKind:l}: {Plan} plan waits ({Reason}): a probe costs {Price} at {WaypointSymbol}, and the purchase must leave the credit reserve.",
                JournalEvents.PlanBlocked,
                AutomationPlan.ProbeDeployment,
                "waiting_for_credits",
                purchase.Price,
                purchase.Shipyard);
        }

        await SaveStateAsync(existing, state, cancellationToken);
    }

    /// <summary>The engine's speed from the cached engine; a probe bought since the last restart has none yet.</summary>
    private static int EngineSpeed(ShipModel ship) => FleetRoles.EngineSpeed(ship, ProbePlanner.DefaultProbeSpeed);

    /// <summary>The waypoints where one of our ships is, probe or not, and not in flight.</summary>
    private static HashSet<string> WaypointsWithShips(IEnumerable<ShipModel> fleet)
        => fleet
            .Where(ship => ship.LocalStatus != ShipLocalStatus.InTransit && !string.IsNullOrWhiteSpace(ship.WaypointSymbol))
            .Select(ship => ship.WaypointSymbol!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How many jumps from home the trade reach goes (D96): <c>Trade.MaxHaulDistance</c>, 5 when it gives none. The probes of
    /// the explored systems within it come at the probe tier, the others' last (D97).
    /// </summary>
    private async Task<int> ReachAsync(CancellationToken cancellationToken)
        => await settings.GetAsync<int>(TradeContextReader.MaxHaulDistanceSetting, cancellationToken) is var jumps and > 0
            ? jumps
            : TradeContextReader.DefaultMaxHaulDistance;

    /// <summary>
    /// The probe as the plan sees it: free, or holding the market it flies to, or will fly to once its goal's next step runs,
    /// in this system or another (slice 6.28); in flight without a goal, where it lands (<see cref="ProbePlanner.Whereabouts"/>).
    /// </summary>
    private async Task<FleetProbe> ProbeAsync(ShipModel probe, CancellationToken cancellationToken)
    {
        var goal = await goals.GetActiveGoalAsync(probe.Symbol, cancellationToken);
        var (waypoint, system) = ProbePlanner.Whereabouts(probe, goal);
        return new FleetProbe(
            new ProbeShip(probe.Symbol, waypoint, FleetRoles.IsFree(probe, goal, hasOpenAssignment: false), EngineSpeed(probe)),
            system,
            probe.SystemSymbol ?? string.Empty);
    }

    /// <summary>
    /// The systems the probes serve (slice 6.28, D97), in the order they get probes: home; then each explored system the built
    /// gates reach from home, the nearest first, those within the trade reach at the probe tier; then any other system a probe
    /// of ours is in or on its way to, where it keeps working but none is bought. A system whose waypoints aren't cached is left
    /// out.
    /// </summary>
    private async Task ServeAsync(Pass pass, IReadOnlyList<FleetProbe> probes, int reach, CancellationToken cancellationToken)
    {
        var jumps = pass.Network is null
            ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [pass.Home] = 0 }
            : ExploreAtlas.Reachable(pass.Network, pass.Home, pass.Now);
        var explored = (pass.Network?.Systems ?? [])
            .Where(system => system.ExploredAt is not null)
            .ToDictionary(system => system.SystemSymbol, StringComparer.OrdinalIgnoreCase);
        var served = new List<(string Symbol, bool Reached, int Jumps)> { (pass.Home, true, 0) };
        served.AddRange(jumps
            .Where(system => !system.Key.Equals(pass.Home, StringComparison.OrdinalIgnoreCase) && explored.ContainsKey(system.Key))
            .OrderBy(system => system.Value)
            .ThenBy(system => system.Key, StringComparer.Ordinal)
            .Select(system => (system.Key, true, system.Value)));
        served.AddRange(probes
            .Select(probe => probe.System)
            .Where(system => system.Length > 0 && !served.Any(known => known.Symbol.Equals(system, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .Select(system => (system, false, 0)));

        foreach (var (symbol, reached, away) in served)
        {
            var systemWaypoints = await waypoints.GetBySystemAsync(symbol, cancellationToken);
            if (systemWaypoints.Count == 0 && pass.Systems.Count > 0)
            {
                continue;
            }

            pass.Systems.Add(new ServedSystem
            {
                Symbol = symbol,
                Reached = reached,
                Jumps = away,
                InTradeReach = reached && away <= reach,
                Gate = explored.TryGetValue(symbol, out var known) && known.GateWaypointSymbol.Length > 0
                    ? known.GateWaypointSymbol
                    : systemWaypoints.FirstOrDefault(waypoint => waypoint.Type.Equals("JUMP_GATE", StringComparison.OrdinalIgnoreCase))?.Symbol ?? string.Empty,
                Positions = systemWaypoints
                    .GroupBy(waypoint => waypoint.Symbol, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => new WaypointPosition(group.First().X, group.First().Y), StringComparer.OrdinalIgnoreCase),

                // Slice 6.32 (D109, D110): a shipyard is a market whose waypoint has one (every shipyard cached so far is), and one
                // that sells SHIP_EXPLORER, as cached, comes before the others.
                Markets =
                [
                    .. systemWaypoints
                        .Where(waypoint => waypoint.HasMarket)
                        .GroupBy(waypoint => waypoint.Symbol, StringComparer.OrdinalIgnoreCase)
                        .OrderBy(group => group.Key, StringComparer.Ordinal)
                        .Select(group => new ProbeMarket(group.Key, pass.LastSeen.GetValueOrDefault(group.Key, DateTimeOffset.MinValue))
                        {
                            Shipyard = pass.ExplorerShipyards.Contains(group.Key) ? ShipyardKind.Explorer
                                : group.Any(waypoint => waypoint.HasShipyard) ? ShipyardKind.Shipyard
                                : ShipyardKind.None,
                        }),
                ],
                Probes = [.. probes.Where(probe => probe.System.Equals(symbol, StringComparison.OrdinalIgnoreCase)).Select(probe => probe.View)],
                Present = probes.Count(probe => probe.System.Equals(symbol, StringComparison.OrdinalIgnoreCase) && probe.In.Equals(symbol, StringComparison.OrdinalIgnoreCase)),
            });
        }
    }

    private static ProbeSnapshot Snapshot(Pass pass, ServedSystem system) => new()
    {
        Positions = system.Positions,
        Markets = system.Markets,
        Probes = system.Probes,
        ShipsAt = pass.ShipsAt,
        Calls = pass.Calls,
        Now = pass.Now,
        DueAfter = pass.DueAfter,
    };

    /// <summary>
    /// The systems short of a probe, there or on its way, in the order they get one, each once at its first step (slice 6.32,
    /// D109: "Across systems"), with the tier the purchase of its next probe stands at and what the probe is for:
    /// <list type="number">
    ///   <item>fewer probes than shipyards that sell SHIP_EXPLORER, wherever the gates reach it, at the probe tier (D111);</item>
    ///   <item>within the trade reach, at the probe tier: fewer probes than shipyards, then fewer than markets (D29, D97);</item>
    ///   <item>beyond it, at the far-probe tier: the same.</item>
    /// </list>
    /// Each step goes by the systems' order: home, then the nearest first. A system's probes park at its shipyards before they
    /// roam, those that sell explorers first (D110, <see cref="ProbePlanner"/>), so its counts say which still lack one.
    /// </summary>
    private static List<ProbeWant> Wants(Pass pass)
    {
        var steps = new (Func<ServedSystem, bool> IsShort, ShipyardKind For, PurchaseTier Tier)[]
        {
            (system => system.Probes.Count < system.ExplorerShipyards, ShipyardKind.Explorer, PurchaseTier.Probes),
            (system => system.InTradeReach && system.Probes.Count < system.Shipyards, ShipyardKind.Shipyard, PurchaseTier.Probes),
            (system => system.InTradeReach && system.Probes.Count < system.Markets.Count, ShipyardKind.None, PurchaseTier.Probes),
            (system => !system.InTradeReach && system.Probes.Count < system.Shipyards, ShipyardKind.Shipyard, PurchaseTier.FarProbes),
            (system => !system.InTradeReach && system.Probes.Count < system.Markets.Count, ShipyardKind.None, PurchaseTier.FarProbes),
        };

        var wants = new List<ProbeWant>();
        foreach (var (isShort, kind, tier) in steps)
        {
            wants.AddRange(pass.Systems
                .Where(system => system.Reached && isShort(system) && !wants.Any(want => want.System == system))
                .Select(system => new ProbeWant(system, kind, tier)));
        }

        return wants;
    }

    /// <summary>
    /// B69, slice 6.28: a system with more probes than markets lends the spares its own markets don't need
    /// (<see cref="ProbePlanner.Surplus"/>) to the first system short of one that they can get to (<see cref="Wants"/>), before
    /// the plan buys one for it.
    /// </summary>
    private void LendSpares(Pass pass)
    {
        foreach (var lender in pass.Systems.Where(system => system.Probes.Count > system.Markets.Count).ToList())
        {
            foreach (var spare in ProbePlanner.Surplus(Snapshot(pass, lender)))
            {
                var borrower = Wants(pass)
                    .Select(want => want.System)
                    .FirstOrDefault(system => system != lender && CanJump(pass, lender.Symbol, system.Symbol, out _));
                if (borrower is null)
                {
                    break;
                }

                Send(pass, lender, borrower, spare, "a spare");
            }
        }
    }

    /// <summary>
    /// Whether a probe can get from one system to another: a way through built gates is known (D101), and its antimatter,
    /// as last seen at each gate, leaves the credit floor (D63).
    /// </summary>
    private static bool CanJump(Pass pass, string from, string to, out long antimatter)
    {
        antimatter = 0;
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (pass.Network is null || !ExploreAtlas.TryFindJumps(pass.Network, from, to, pass.Now, out var jumps))
        {
            return false;
        }

        antimatter = jumps.Sum(jump => pass.AntimatterAt(jump.GateWaypointSymbol));
        return pass.Credits - antimatter >= pass.Floor;
    }

    /// <summary>
    /// Gives a probe its flight to another system (<see cref="Pass.Sends"/>): to the market of that system it should see first,
    /// from the system's gate (<see cref="ProbePlanner.Entry"/>). It holds that market from now on, and counts for that system.
    /// A system short of probes always has a market without one; should none be left, the probe stays.
    /// </summary>
    private void Send(Pass pass, ServedSystem from, ServedSystem to, ProbeShip probe, string what)
    {
        var market = ProbePlanner.Entry(Snapshot(pass, to), to.Gate, probe.Speed);
        if (market.Length == 0)
        {
            return;
        }

        pass.Sends.Add((probe.Symbol, market));
        from.Probes.RemoveAll(view => view.Symbol.Equals(probe.Symbol, StringComparison.OrdinalIgnoreCase));
        to.Probes.Add(new ProbeShip(probe.Symbol, market, IsFree: false, probe.Speed));
        logger.LogInformation(
            "Probe plan: probe {ShipSymbol}, {What} in {From}, flies to {WaypointSymbol} in {SystemSymbol}, {Jumps} jumps from home, which has {Probes} probes for {Markets} markets.",
            probe.Symbol,
            what,
            from.Symbol,
            market,
            to.Symbol,
            to.Jumps,
            to.Probes.Count - 1,
            to.Markets.Count);
    }

    /// <summary>
    /// Buys the next probe (D29, D97) for the first system short of one (<see cref="Wants"/>, slice 6.32: the shipyards first),
    /// at the shipyard where it costs least with the antimatter of the jumps there counted, when nothing comes before it in the
    /// order ships are bought in (D43). <see cref="IShipPurchaseService"/> keeps the credit reserve, refuses a probe at SCARCE
    /// supply when it fetches the shipyard again, and calls for a ship when none of ours is at the shipyard (D30). A probe
    /// bought for another system flies there at once.
    /// </summary>
    private async Task<ProbePurchase> BuyProbeAsync(Pass pass, CancellationToken cancellationToken)
    {
        var offers = pass.Shipyards
            .SelectMany(shipyard => shipyard.Ships
                .Where(ship => ship.Type.Equals(ProbeShipType, StringComparison.OrdinalIgnoreCase) && ship.PurchasePrice > 0)
                .Select(ship => new ProbeOffer(
                    shipyard.WaypointSymbol,
                    shipyard.SystemSymbol.Length > 0 ? shipyard.SystemSymbol : WaypointSymbols.SystemOf(shipyard.WaypointSymbol),
                    ship.PurchasePrice,
                    (ship.Supply ?? string.Empty).Equals(ScarceSupply, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(offer => offer.Price)
            .ThenBy(offer => offer.Shipyard, StringComparer.Ordinal)
            .ToList();

        var homeOffer = offers.FirstOrDefault(offer => offer.System.Equals(pass.Home, StringComparison.OrdinalIgnoreCase));
        var wants = Wants(pass);
        if (wants.Count == 0)
        {
            return new ProbePurchase(ProbePurchaseStatus.EveryMarketHasOne, string.Empty, homeOffer?.Shipyard ?? string.Empty, homeOffer?.Price ?? 0, 0);
        }

        var scarce = false;
        (ProbeSource Source, ShipyardKind For)? waiting = null;
        foreach (var want in wants)
        {
            var source = Source(pass, offers, want.System, ref scarce);
            if (source is { Waits: false })
            {
                // A probe for another system, whose shipyard sells this one's for least, is still bought for this one's sake.
                var tier = want.Tier == PurchaseTier.Probes || source.For.InTradeReach ? PurchaseTier.Probes : PurchaseTier.FarProbes;
                return await BuyAsync(pass, source.For, source.Offer, source.Antimatter, tier, want.For, cancellationToken);
            }

            waiting ??= source is null ? null : (source, want.For);
        }

        if (waiting is { Source: var wait, For: var kind })
        {
            return new ProbePurchase(ProbePurchaseStatus.WaitingForAProbeToArrive, wait.For.Symbol, wait.Offer.Shipyard, wait.Offer.Price, wait.Antimatter, kind);
        }

        return new ProbePurchase(
            scarce ? ProbePurchaseStatus.ShipyardsScarce : ProbePurchaseStatus.NoShipyardSellsProbes,
            wants[0].System.Symbol,
            homeOffer?.Shipyard ?? string.Empty,
            homeOffer?.Price ?? 0,
            0,
            wants[0].For);
    }

    /// <summary>
    /// Where the next probe for a system short of one is bought (D97: "the shipyard where it costs least, the antimatter to its
    /// system counted"), or why the plan waits (B72). A shipyard sells only where one of our ships is (D30): at home, and abroad
    /// once a probe of ours is in its system. Going by the cheapest shipyard, the antimatter of the jumps from there counted:
    /// <list type="bullet">
    ///   <item>one that can sell now sells it;</item>
    ///   <item>one in a system a probe of ours is on its way to waits for that probe, and the probes for it are bought there
    ///   once it is: the plan used to buy them where it could, one a pass, at home;</item>
    ///   <item>one in a system with no probe there or on its way waits for that system's first probe, which is bought now, at
    ///   the cheapest shipyard that can sell it (or waited for, as above), and flies there.</item>
    /// </list>
    /// A system's own shipyard counts for it only once a probe of ours is there or on its way.
    /// </summary>
    /// <returns>The purchase, or the wait; null when no shipyard can serve the system.</returns>
    private static ProbeSource? Source(Pass pass, IReadOnlyList<ProbeOffer> offers, ServedSystem system, ref bool scarce)
    {
        foreach (var (offer, antimatter) in Options(pass, offers, system, ownToo: system.Probes.Count > 0, ref scarce))
        {
            var seller = Served(pass, offer.System)!;
            if (CanSell(pass, seller))
            {
                return new ProbeSource(system, offer, antimatter, Waits: false);
            }

            if (IsComing(seller))
            {
                return new ProbeSource(system, offer, antimatter, Waits: true);
            }

            if (seller.Markets.Count > 0 && FirstProbe(pass, offers, seller, ref scarce) is { } first)
            {
                return first;
            }
        }

        return null;
    }

    /// <summary>
    /// The first probe for a system with no probe of ours there or on its way (B72): from the cheapest shipyard elsewhere that
    /// can sell it now, or the wait for one that can once a probe on its way there arrives.
    /// </summary>
    private static ProbeSource? FirstProbe(Pass pass, IReadOnlyList<ProbeOffer> offers, ServedSystem system, ref bool scarce)
    {
        foreach (var (offer, antimatter) in Options(pass, offers, system, ownToo: false, ref scarce))
        {
            var seller = Served(pass, offer.System)!;
            if (CanSell(pass, seller))
            {
                return new ProbeSource(system, offer, antimatter, Waits: false);
            }

            if (IsComing(seller))
            {
                return new ProbeSource(system, offer, antimatter, Waits: true);
            }
        }

        return null;
    }

    /// <summary>
    /// The shipyards a probe for <paramref name="system"/> can come from, the cheapest first with the antimatter of the jumps
    /// counted (D97): those in a system a probe can get to (home's, or one the gates reach), from where a probe can get to
    /// <paramref name="system"/>, not at SCARCE supply (noted in <paramref name="scarce"/>); the system's own only with
    /// <paramref name="ownToo"/>.
    /// </summary>
    private static List<(ProbeOffer Offer, long Antimatter)> Options(Pass pass, IReadOnlyList<ProbeOffer> offers, ServedSystem system, bool ownToo, ref bool scarce)
    {
        var options = new List<(ProbeOffer Offer, long Antimatter)>();
        foreach (var offer in offers)
        {
            var seller = Served(pass, offer.System);
            if (seller is null
                || !(seller.Reached || seller.Present > 0)
                || (!ownToo && seller == system)
                || !CanJump(pass, offer.System, system.Symbol, out var antimatter))
            {
                continue;
            }

            if (offer.Scarce)
            {
                scarce = true;
                continue;
            }

            options.Add((offer, antimatter));
        }

        return [.. options
            .OrderBy(option => option.Offer.Price + option.Antimatter)
            .ThenBy(option => option.Offer.Price)
            .ThenBy(option => option.Offer.Shipyard, StringComparer.Ordinal)];
    }

    private static ServedSystem? Served(Pass pass, string systemSymbol)
        => pass.Systems.FirstOrDefault(served => served.Symbol.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a shipyard in the system can sell a probe now (D30): at home there always is a probe to call, abroad once one of ours is there.</summary>
    private static bool CanSell(Pass pass, ServedSystem seller)
        => seller.Symbol.Equals(pass.Home, StringComparison.OrdinalIgnoreCase) || seller.Present > 0;

    /// <summary>Whether a probe of ours is on its way to the system, none being there yet.</summary>
    private static bool IsComing(ServedSystem system) => system.Present == 0 && system.Probes.Count > 0;

    private async Task<ProbePurchase> BuyAsync(
        Pass pass,
        ServedSystem system,
        ProbeOffer offer,
        long antimatter,
        PurchaseTier tier,
        ShipyardKind wantedFor,
        CancellationToken cancellationToken)
    {
        if (!await purchaseOrder.ReportAsync(
            AutomationPlan.ProbeDeployment,
            new PurchaseNeed(tier, ProbeShipType, offer.Shipyard, offer.Price),
            cancellationToken))
        {
            return new ProbePurchase(ProbePurchaseStatus.WaitingForAnotherPurchase, system.Symbol, offer.Shipyard, offer.Price, antimatter, wantedFor);
        }

        ShipPurchaseResult result;
        try
        {
            result = await shipPurchases.TryPurchaseAsync(ProbeShipType, offer.Shipyard, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The probes fly on: a purchase that keeps failing shows as RepeatingError.
            logger.LogWarning(ex, "Probe plan: buying a {ShipType} at {WaypointSymbol} failed.", ProbeShipType, offer.Shipyard);
            return new ProbePurchase(ProbePurchaseStatus.None, system.Symbol, offer.Shipyard, offer.Price, antimatter, wantedFor);
        }

        var status = result switch
        {
            { IsSuccess: true } => ProbePurchaseStatus.Bought,
            { Failure: ShipPurchaseFailure.OverBudget } => ProbePurchaseStatus.WaitingForCredits,
            { Failure: ShipPurchaseFailure.NoShipAtShipyard } => ProbePurchaseStatus.WaitingForAShipAtTheShipyard,
            { Failure: ShipPurchaseFailure.PriceUnknown } => ProbePurchaseStatus.NoShipyardSellsProbes,
            { Failure: ShipPurchaseFailure.Scarce } => ProbePurchaseStatus.ShipyardsScarce,
            _ => ProbePurchaseStatus.None,
        };
        var price = result.EstimatedCost > 0 ? result.EstimatedCost : offer.Price;
        if (result is { IsSuccess: true, PurchasedShip: { } bought })
        {
            var probe = new ProbeShip(bought.Symbol, offer.Shipyard, IsFree: true, EngineSpeed(bought));
            if (offer.System.Equals(system.Symbol, StringComparison.OrdinalIgnoreCase))
            {
                system.Probes.Add(probe);
            }
            else
            {
                // Bought for another system: it flies there at once, so the system it was bought in doesn't keep it.
                var at = pass.Systems.FirstOrDefault(served => served.Symbol.Equals(offer.System, StringComparison.OrdinalIgnoreCase)) ?? system;
                at.Probes.Add(probe);
                Send(pass, at, system, probe, "bought");
            }
        }

        return new ProbePurchase(status, system.Symbol, offer.Shipyard, price, antimatter, wantedFor);
    }

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
        Pass pass,
        IReadOnlyList<(ServedSystem System, ProbeMove Move)> moves,
        ProbePurchase purchase)
    {
        // Where each probe is, or flies to, once this pass's flights are given.
        var holds = pass.Systems
            .SelectMany(system => system.Probes.Select(probe => (
                probe.Symbol,
                Waypoint: moves.FirstOrDefault(move => string.Equals(move.Move.ShipSymbol, probe.Symbol, StringComparison.Ordinal)).Move?.WaypointSymbol ?? probe.WaypointSymbol)))
            .ToList();
        string ProbeAt(string waypointSymbol) => holds
            .Where(hold => hold.Waypoint.Equals(waypointSymbol, StringComparison.OrdinalIgnoreCase))
            .Select(hold => hold.Symbol)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault() ?? string.Empty;

        return new ProbeDeploymentPlanState
        {
            PlanId = existing?.PlanId ?? Guid.NewGuid(),
            SystemSymbol = pass.Home,
            Probes = holds.Count,
            Systems =
            [
                .. pass.Systems.Select(system => new ProbeSystemState
                {
                    SystemSymbol = system.Symbol,
                    Reached = system.Reached,
                    Jumps = system.Jumps,
                    InTradeReach = system.InTradeReach,
                    Probes = system.Probes.Count,
                    Markets =
                    [
                        .. system.Markets.Select(market =>
                        {
                            var view = new ProbeMarketState
                            {
                                WaypointSymbol = market.WaypointSymbol,
                                Shipyard = market.Shipyard,
                                ProbeSymbol = ProbeAt(market.WaypointSymbol),
                                WatchedByShip = pass.OthersAt.Contains(market.WaypointSymbol),
                            };
                            return view.IsUnwatched
                                ? view with { DueAt = market.LastSeenAt == DateTimeOffset.MinValue ? DateTimeOffset.MinValue : market.LastSeenAt + pass.DueAfter }
                                : view;
                        }),
                    ],
                }),
            ],
            Purchase = purchase.Status,
            NextProbeSystem = purchase.System,
            NextProbeFor = purchase.For,
            NextProbeShipyard = purchase.Shipyard,
            NextProbePrice = purchase.Price,
            NextProbeAntimatter = purchase.Antimatter,
            Calls =
            [
                .. pass.Calls
                    .Where(call => pass.Systems.Any(system => system.Positions.ContainsKey(call.WaypointSymbol)))
                    .Select(call => new ProbeCallState
                    {
                        WaypointSymbol = call.WaypointSymbol,
                        ShipType = call.ShipType,
                        ProbeSymbol = ProbeAt(call.WaypointSymbol),
                    }),
            ],
            CreatedAt = existing?.CreatedAt ?? pass.Now,
            UpdatedAt = pass.Now,
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

    /// <summary>A probe of the fleet: the plan's view of it, the system it counts for, and the system it is in.</summary>
    private sealed record FleetProbe(ProbeShip View, string System, string In);

    /// <summary>A shipyard's offer of a probe, as cached.</summary>
    private sealed record ProbeOffer(string Shipyard, string System, long Price, bool Scarce);

    /// <summary>Where a probe for <see cref="For"/> is bought now, or, with <see cref="Waits"/>, will be once a probe arrives (B72).</summary>
    private sealed record ProbeSource(ServedSystem For, ProbeOffer Offer, long Antimatter, bool Waits);

    /// <summary>What the pass did about the next probe, where it would be bought, and what it is for (slice 6.32).</summary>
    private sealed record ProbePurchase(ProbePurchaseStatus Status, string System, string Shipyard, long Price, long Antimatter, ShipyardKind For = ShipyardKind.None);

    /// <summary>
    /// A system short of a probe (<see cref="Wants"/>): what the probe is for, a shipyard that sells SHIP_EXPLORER, another
    /// shipyard or a market (<see cref="ShipyardKind.None"/>), and the tier of the order ships are bought in its purchase stands at.
    /// </summary>
    private sealed record ProbeWant(ServedSystem System, ShipyardKind For, PurchaseTier Tier);

    /// <summary>A system the probes serve, as one pass sees it.</summary>
    private sealed class ServedSystem
    {
        public required string Symbol { get; init; }

        /// <summary>Whether a way from home through built gates is known now; home is.</summary>
        public required bool Reached { get; init; }

        public required int Jumps { get; init; }

        public required bool InTradeReach { get; init; }

        /// <summary>Its jump gate, where a probe from another system comes in; empty when none is known.</summary>
        public required string Gate { get; init; }

        public required IReadOnlyDictionary<string, WaypointPosition> Positions { get; init; }

        public required IReadOnlyList<ProbeMarket> Markets { get; init; }

        /// <summary>The probes in the system, or on their way there, as this pass leaves them.</summary>
        public required List<ProbeShip> Probes { get; init; }

        /// <summary>How many of them are in the system already, which a purchase at a shipyard there needs (D30).</summary>
        public required int Present { get; init; }

        /// <summary>Its markets that are shipyards (slice 6.32), where a probe parks first.</summary>
        public int Shipyards => Markets.Count(market => market.Shipyard != ShipyardKind.None);

        /// <summary>Its shipyards that sell SHIP_EXPLORER (slice 6.32), where a probe parks before any other.</summary>
        public int ExplorerShipyards => Markets.Count(market => market.Shipyard == ShipyardKind.Explorer);
    }

    /// <summary>What one pass reads and decides, across the systems.</summary>
    private sealed class Pass
    {
        public required string Home { get; init; }

        public required DateTimeOffset Now { get; init; }

        public required TimeSpan DueAfter { get; init; }

        public required ExplorePlanState? Network { get; init; }

        public required long Credits { get; init; }

        public required long Floor { get; init; }

        public required IReadOnlyDictionary<string, DateTimeOffset> LastSeen { get; init; }

        /// <summary>The shipyards where a purchase waits for one of our ships (D30), read again after this pass's purchase.</summary>
        public required IReadOnlyList<ShipyardCall> Calls { get; set; }

        /// <summary>Every shipyard cached, as last seen: where probes are sold, and which shipyards sell explorers.</summary>
        public required IReadOnlyList<ShipyardWaypointDto> Shipyards { get; init; }

        /// <summary>The shipyards that sell SHIP_EXPLORER, as cached (slice 6.32, D109).</summary>
        public required IReadOnlySet<string> ExplorerShipyards { get; init; }

        public List<ServedSystem> Systems { get; } = [];

        /// <summary>The waypoints where one of our ships is, probe or not, and not in flight.</summary>
        public IReadOnlySet<string> ShipsAt { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The waypoints where one of our ships that is no probe is, and not in flight: the market watch keeps those fresh.</summary>
        public IReadOnlySet<string> OthersAt { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>What a unit of ANTIMATTER costs at each market that sells it, as last seen; read only when a probe could jump.</summary>
        public IReadOnlyDictionary<string, long> Antimatter { get; set; } = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The flights to other systems this pass gives, by probe: the market each flies to.</summary>
        public List<(string ShipSymbol, string WaypointSymbol)> Sends { get; } = [];

        /// <summary>What a jump from <paramref name="gate"/> costs: one ANTIMATTER at its market, as last seen; 0 while unknown.</summary>
        public long AntimatterAt(string gate) => Antimatter.GetValueOrDefault(gate);
    }
}
