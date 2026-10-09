using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Probes;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Exploring;

/// <summary>The explore plan (asked on 2026-10-04).</summary>
public interface IExplorePlanService
{
    /// <summary>
    /// One pass of the plan: learns one thing about the gates it knows, buys an explorer when it wants one, and gives the
    /// command ship and each explorer their next step when they are free or exploring.
    /// </summary>
    /// <param name="cancellationToken">Stops the pass.</param>
    /// <returns>A task that completes when the pass is done.</returns>
    Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The explore plan, asked on 2026-10-04: "if an active jump gate goes to a system that isn't explored yet, the COMMAND
/// ship should go through that jump gate. If there are markets or shipyard there, the COMMAND ship should scout them, as it
/// initially does for the home system, recursively." With your decisions of that day:
/// <list type="bullet">
///   <item>No limit: it explores every system the active gates reach, the nearest by jumps first (<see cref="ExploreAtlas"/>).
///   A jump needs the gates at both ends built.</item>
///   <item>It takes a ship once its trip ends (it is free), before the role board and every plan after it.</item>
///   <item>In a system not explored yet the ship visits each market and shipyard once (<see cref="ExploreSystemGoal"/>); a
///   system with none is explored as soon as it is there.</item>
///   <item>A jump keeps the credit floor every ship purchase keeps (<see cref="CreditReserve.FloorSetting"/>, 60,000
///   seeded): until the credits after the antimatter stay at or above it, the plan holds the jump, and leaves a free ship to
///   its other work.</item>
///   <item>Afterwards the command ship comes home, and the other plans give it work again.</item>
/// </list>
/// Slice 6.30 (D98, D99, D102, D103) adds the explorers, and an order:
/// <list type="bullet">
///   <item>Every exploring ship takes a system within 5 jumps of home before any beyond it, the nearest to it first in each
///   (D103: "first the systems within 5 jumps are explored before going further"); since slice 6.33 (D114) ring by ring: 1 to 5
///   jumps from home, then 6 to 10, then 11 to 15 (<see cref="ExploreAtlas.Ring"/>), each <c>Explore.RingWidth</c> wide (5;
///   slice 6.35, D117), the trade reach's width until then.</item>
///   <item>It wants one explorer for every <c>Explore.SystemsPerExplorer</c> (10) systems left to explore, or part of that, at
///   most <c>Explore.MaxExplorers</c> (5; 0 for no cap): the systems it knows, hasn't explored and reaches from home through
///   built gates (<see cref="ExploreAtlas.SystemsLeft"/>).</item>
///   <item>It buys them at the cheapest shipyard the gates reach that sells one, within the credit reserve, each at
///   <see cref="PurchaseTier.Explorer"/>, before the probes (D98; the further ones too since slice 6.33, D113). A probe of ours
///   in the shipyard's system, or on its way there, answers the purchase's call for a ship (D30); with none, the command ship
///   fetches the explorer (slice 6.32, D108).</item>
///   <item>The explorers explore, each the nearest system no other exploring ship has taken; once there is one, the command
///   ship finishes its step, comes home and is released (D60). An explorer with no system left to take is released where it
///   is, and trades from there (D102) until one turns up.</item>
///   <item>In each system it explores, the ship charts every uncharted waypoint that can hold a market or shipyard, the gate
///   first (<see cref="Charting"/>, D99).</item>
/// </list>
/// Slice 6.31 (D100, D101, D104–D107) lets an explorer warp:
/// <list type="bullet">
///   <item>An explorer with a warp drive goes the fastest way, through the gates or by warps (<see cref="SystemWays"/>), and so
///   also to the systems the gates don't reach: behind a gate under construction, or with no gate the plan knows. A warp is
///   fuel-safe: it lands where the ship can refuel, or keeps the fuel to warp back (D100); BURN or CRUISE, never a drift (D104).</item>
///   <item>The systems within the first ring of home through the gates come first, then the nearest by the seconds its way
///   takes, whether by jumps or warps (D106, <see cref="ExploreAtlas.NextByWays"/>). Since slice 6.33 (D114) ring by ring: a
///   system behind a gate under construction in the ring of the jumps through that gate, one found by a scan with no gate known
///   after every ring, and within a ring the nearest by the seconds.</item>
///   <item>Before a warp goes to a system only a warp reaches, its waypoints are fetched (one system a pass, after the gates'
///   first looks and before the looks due again, B78), so the ship lands where it can refuel.</item>
///   <item>With nothing left within its ways, an explorer with a sensor array scans from where it is, once a system, and the
///   systems within its warps join the plan's (D105); with nothing left after that, one in a system the gates don't reach goes
///   back to one they do, and is released there to trade (D102).</item>
///   <item>A system only a warp reaches doesn't count towards the explorers wanted (D107).</item>
/// </list>
/// What it knows of the gates comes from the API: home's gate, then the connections of each system it explores, and the
/// gates those lead to. A pass asks for one of those at most, as the reads give way to the moves (D19); a gate still under
/// construction is looked at again hourly. A system an exploring ship has just jumped into has its waypoints fetched at once.
/// </summary>
public sealed class ExplorePlanService(
    IAgentRepository agents,
    IShipRepository ships,
    IShipGoalRepository goals,
    IShipAssignmentRepository assignments,
    IWaypointRepository waypoints,
    ISystemRepository systems,
    IMarketRepository markets,
    IShipyardRepository shipyards,
    IPlanRepository plans,
    ISettingsRepository settings,
    ISpaceTradersPort port,
    IOrbitSubCommand orbit,
    IShipPurchaseService shipPurchases,
    IPurchaseOrder purchaseOrder,
    JumpRefusals refusals,
    WarpRefusals warpRefusals,
    ILogger<ExplorePlanService> logger) : IExplorePlanService
{
    /// <summary>The type of an exploring ship's assignment.</summary>
    public const string AssignmentType = "Explore";

    /// <summary>The command ship's registration role, its cached type after startup sync.</summary>
    public const string CommandShipType = "COMMAND";

    /// <summary>The setting for how many systems left to explore call for one explorer (D102); 0 buys none.</summary>
    public const string SystemsPerExplorerSetting = "Explore.SystemsPerExplorer";

    /// <summary>The setting for the most explorers the plan buys (D102); 0 for no cap.</summary>
    public const string MaxExplorersSetting = "Explore.MaxExplorers";

    /// <summary>The setting for the width, in jumps from home, of each ring the exploring ships take in turn (D114, D117).</summary>
    public const string RingWidthSetting = "Explore.RingWidth";

    /// <summary>The width of each ring when <see cref="RingWidthSetting"/> gives none (D117: "defaulting to 5").</summary>
    public const int DefaultRingWidth = 5;

    private const string JumpGateType = "JUMP_GATE";
    private const string Antimatter = "ANTIMATTER";
    private const string WaitingForCredits = "waiting_for_credits";
    private const string NoWayHome = "no_way_home";
    private const string NothingToExplore = "nothing_to_explore";
    private const string Scanning = "scanning";
    private const int WaypointPageSize = 20;

    private static readonly JsonSerializerOptions CompareOptions = new();

    /// <summary>Whether a ship is the command ship, the one that explores while there is no explorer.</summary>
    /// <param name="ship">The ship.</param>
    /// <returns>True for the ship whose registration role is COMMAND.</returns>
    public static bool IsCommandShip(ShipModel ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return ship.ShipType.Equals(CommandShipType, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default)
    {
        // Slice 6.30 (D43): a pass that buys no explorer says so; one that would, says so where it would buy.
        await purchaseOrder.ReportAsync(AutomationPlan.Explore, PurchaseNeed.None, cancellationToken);

        var agent = await agents.GetAsync(cancellationToken);
        var fleet = await ships.GetAllAsync(cancellationToken);
        var ship = fleet.FirstOrDefault(IsCommandShip);
        if (agent?.HeadquartersSymbol is not { Length: > 0 } headquarters || ship is null)
        {
            logger.LogDebug("Explore plan: the agent's headquarters or the command ship isn't cached yet.");
            return;
        }

        var now = TimeProvider.System.GetUtcNow();
        var home = WaypointSymbols.SystemOf(headquarters);
        var existing = await plans.GetAsync<ExplorePlanState>(PlanTypes.Explore, cancellationToken);
        var state = existing is not null
            && existing.HomeSystemSymbol.Equals(home, StringComparison.OrdinalIgnoreCase)
            && existing.ShipSymbol.Equals(ship.Symbol, StringComparison.OrdinalIgnoreCase)
                ? existing
                : new ExplorePlanState
                {
                    ShipSymbol = ship.Symbol,
                    HomeSystemSymbol = home,
                    Status = ExploreStatus.Waiting,

                    // Home counts as explored: the scout plan visits its markets.
                    Systems = [new KnownSystem { SystemSymbol = home, ExploredAt = now }],
                    UpdatedAt = now,
                };

        var explorers = fleet.Where(FleetRoles.IsExplorer).OrderBy(explorer => explorer.Symbol, StringComparer.Ordinal).ToList();
        state = await LearnAsync(state, [ship, .. explorers], [.. explorers.Where(Warps.HasDrive)], now, cancellationToken);
        state = await CountAsync(state, now, cancellationToken);
        state = state with { Purchase = await BuyAsync(state, fleet, explorers.Count, now, cancellationToken) };
        var ringWidth = await RingWidthAsync(cancellationToken);
        state = await StepAsync(state, ship, explorers.Count > 0 || state.Purchase.Status == ExplorerPurchaseStatus.Bought, ringWidth, now, cancellationToken);

        // The explorers the fleet has, each with what the plan does with it; one bought this pass joins on the next.
        state = state with
        {
            Explorers = [.. explorers.Select(explorer => FindExplorer(state, explorer.Symbol) ?? new ExploringShip { ShipSymbol = explorer.Symbol, Status = ExploreStatus.Waiting })],
        };
        foreach (var explorer in explorers)
        {
            state = await StepExplorerAsync(state, explorer, ringWidth, now, cancellationToken);
        }

        await SaveAsync(existing, state, now, cancellationToken);
    }

    /// <summary>
    /// Learns what the next decision needs: the waypoints of a system an exploring ship has just jumped into, at once; each
    /// known system's gate from the cached waypoints; then one look at the API, the most needed first (<see cref="NextLook"/>);
    /// with none of those, for the ships with a warp drive, the waypoints of a system only a warp reaches (slice 6.31,
    /// <see cref="LookForWarpsAsync"/>).
    /// </summary>
    private async Task<ExplorePlanState> LearnAsync(
        ExplorePlanState state,
        IReadOnlyList<ShipModel> exploring,
        IReadOnlyList<ShipModel> warping,
        DateTimeOffset now,
        CancellationToken ct)
    {
        foreach (var ship in exploring.Where(ship => ship.LocalStatus != ShipLocalStatus.InTransit && ship.SystemSymbol is { Length: > 0 }))
        {
            var here = ship.SystemSymbol!;
            state = Ensure(state, here);
            var known = Find(state, here)!;
            if ((await waypoints.GetBySystemAsync(here, ct)).Count == 0
                && (known.WaypointsCheckedAt is null || now - known.WaypointsCheckedAt.Value >= ExploreAtlas.RetryAfter))
            {
                return await FetchSystemAsync(state, here, now, ct);
            }
        }

        state = await GatesFromCacheAsync(state, now, ct);
        var look = NextLook(state, now, due: false);
        if (look.Kind == Look.None)
        {
            // B78: a system only a warp reaches, never asked for, comes before the looks due again: every explorer waits for it
            // (ExploreAtlas.NextByWays), and with enough gates under construction, each looked at again hourly, those never ran out.
            var warpLooks = await WarpLooksAsync(state, warping, now, ct);
            if (warpLooks.FirstOrDefault(system => Find(state, system)?.WaypointsCheckedAt is null) is { } waitedFor)
            {
                return await FetchSystemAsync(state, waitedFor, now, ct);
            }

            look = NextLook(state, now, due: true);
            if (look.Kind == Look.None)
            {
                return warpLooks.Count == 0 ? state : await FetchSystemAsync(state, warpLooks[0], now, ct);
            }
        }

        return look switch
        {
            (Look.Gate, var system) => await LookAtGateAsync(state, system, now, ct),
            (Look.Connections, var system) => await LookAtConnectionsAsync(state, system, now, ct),
            _ => state,
        };
    }

    /// <summary>
    /// The systems only a warp reaches whose waypoints are still to be fetched, the nearest to the explored systems and the ships
    /// with a warp drive first (slice 6.31, <see cref="ExploreAtlas.WarpLooks"/>). Each is fetched as a system an exploring ship
    /// has just jumped into: a warp lands where the ship can refuel, so where that is must be known first. One system a pass.
    /// </summary>
    private async Task<IReadOnlyList<string>> WarpLooksAsync(ExplorePlanState state, IReadOnlyList<ShipModel> warping, DateTimeOffset now, CancellationToken ct)
    {
        if (warping.Count == 0)
        {
            return [];
        }

        var positions = (await systems.GetAllAsync(ct))
            .GroupBy(system => system.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (group.First().X, group.First().Y), StringComparer.OrdinalIgnoreCase);
        var fetched = (await waypoints.GetVisitedSystemSymbolsAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ExploreAtlas.WarpLooks(
            state,
            positions,
            fetched,
            [.. warping.Select(ship => (ship.SystemSymbol ?? string.Empty, WayShip.Of(ship, now).MaxWarp))],
            now);
    }

    /// <summary>Fetches a system and all its waypoints, and caches them; the gate among them is as the API says now.</summary>
    private async Task<ExplorePlanState> FetchSystemAsync(ExplorePlanState state, string systemSymbol, DateTimeOffset now, CancellationToken ct)
    {
        SystemDataModel system;
        var fetched = new List<WaypointDataModel>();
        try
        {
            system = await port.GetSystemAsync(systemSymbol, ct);
            var page = 1;
            bool more;
            do
            {
                var answer = await port.GetWaypointsAsync(systemSymbol, page++, WaypointPageSize, ct);
                fetched.AddRange(answer.Items);
                more = answer.Items.Count == WaypointPageSize && fetched.Count < answer.Total;
            }
            while (more);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Explore plan: couldn't fetch the waypoints of {SystemSymbol}; trying again in a few minutes.", systemSymbol);
            return Update(state, systemSymbol, known => known with { WaypointsCheckedAt = now });
        }

        await systems.UpsertAsync(new SystemCacheModel(system.Symbol, system.SectorSymbol, system.Type, system.X, system.Y, now), ct);
        await waypoints.UpsertRangeAsync([.. fetched.Select(waypoint => Charting.ToCache(waypoint, now))], ct);

        var gate = fetched.FirstOrDefault(waypoint => waypoint.Type.Equals(JumpGateType, StringComparison.OrdinalIgnoreCase));
        return Update(state, systemSymbol, known => known with
        {
            WaypointsCheckedAt = now,
            WaypointsFetchedAt = now,
            GateWaypointSymbol = gate?.Symbol ?? string.Empty,
            Gate = gate is null ? GateState.None : gate.IsUnderConstruction ? GateState.UnderConstruction : GateState.Active,
            GateCheckedAt = now,
        });
    }

    /// <summary>
    /// The gate of each known system whose gate isn't known, from its cached waypoints, without a call: home's at first.
    /// Its state is looked at fresh (<see cref="NextLook"/>): the cache keeps what startup sync saw when the agent started.
    /// </summary>
    private async Task<ExplorePlanState> GatesFromCacheAsync(ExplorePlanState state, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var known in state.Systems.Where(system => system.GateWaypointSymbol.Length == 0 && system.Gate == GateState.Unknown).ToList())
        {
            var cached = await waypoints.GetBySystemAsync(known.SystemSymbol, ct);
            if (cached.Count == 0)
            {
                continue;
            }

            var gate = cached.FirstOrDefault(waypoint => waypoint.Type.Equals(JumpGateType, StringComparison.OrdinalIgnoreCase));
            state = Update(state, known.SystemSymbol, system => gate is null
                ? system with { Gate = GateState.None, GateCheckedAt = now }
                : system with { GateWaypointSymbol = gate.Symbol });
        }

        return state;
    }

    private enum Look
    {
        None,
        Gate,
        Connections,
    }

    /// <summary>
    /// The one look this pass, the most needed first: a gate never looked at, nearest home first; the connections of an
    /// explored system never asked for; then, with <paramref name="due"/>, whatever is due again, the longest waiting first: a
    /// gate under construction after <see cref="ExploreAtlas.RecheckAfter"/>, a look that failed after
    /// <see cref="ExploreAtlas.RetryAfter"/>, an explored system's connections that came back empty after an hour. Between the
    /// two come the systems only a warp reaches that were never asked for (B78, <see cref="LearnAsync"/>).
    /// </summary>
    private static (Look Kind, string SystemSymbol) NextLook(ExplorePlanState state, DateTimeOffset now, bool due)
    {
        var jumps = ExploreAtlas.JumpsFromHome(state, now);
        var byDistance = state.Systems
            .OrderBy(system => jumps.TryGetValue(system.SystemSymbol, out var away) ? away : int.MaxValue)
            .ThenBy(system => system.SystemSymbol, StringComparer.Ordinal)
            .ToList();

        var neverLooked = byDistance.FirstOrDefault(system => system.GateWaypointSymbol.Length > 0
            && system.Gate == GateState.Unknown
            && system.GateCheckedAt is null);
        if (neverLooked is not null)
        {
            return (Look.Gate, neverLooked.SystemSymbol);
        }

        var neverAsked = byDistance.FirstOrDefault(system => system.ExploredAt is not null
            && ExploreAtlas.IsUsable(system, now)
            && system.ConnectionsCheckedAt is null);
        if (neverAsked is not null)
        {
            return (Look.Connections, neverAsked.SystemSymbol);
        }

        if (!due)
        {
            return (Look.None, string.Empty);
        }

        var again = new List<(DateTimeOffset Since, Look Kind, string SystemSymbol)>();
        foreach (var system in state.Systems.Where(system => system.GateWaypointSymbol.Length > 0))
        {
            var checkedAt = system.GateCheckedAt ?? DateTimeOffset.MinValue;
            if ((system.Gate == GateState.UnderConstruction && now - checkedAt >= ExploreAtlas.RecheckAfter)
                || (system.Gate == GateState.Unknown && now - checkedAt >= ExploreAtlas.RetryAfter))
            {
                again.Add((checkedAt, Look.Gate, system.SystemSymbol));
            }

            if (system.ExploredAt is not null
                && ExploreAtlas.IsUsable(system, now)
                && (system.Connections ?? []).Count == 0
                && system.ConnectionsCheckedAt is { } asked
                && now - asked >= ExploreAtlas.RecheckAfter)
            {
                again.Add((asked, Look.Connections, system.SystemSymbol));
            }
        }

        return again.Count == 0
            ? (Look.None, string.Empty)
            : again.OrderBy(look => look.Since).ThenBy(look => look.SystemSymbol, StringComparer.Ordinal).Select(look => (look.Kind, look.SystemSymbol)).First();
    }

    /// <summary>Looks at a system's gate: built, or still under construction. A cached gate waypoint is kept up to date.</summary>
    private async Task<ExplorePlanState> LookAtGateAsync(ExplorePlanState state, string systemSymbol, DateTimeOffset now, CancellationToken ct)
    {
        var gateSymbol = Find(state, systemSymbol)!.GateWaypointSymbol;
        WaypointDataModel gate;
        try
        {
            gate = await port.GetWaypointAsync(systemSymbol, gateSymbol, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Explore plan: couldn't look at the jump gate {WaypointSymbol}; trying again in a few minutes.", gateSymbol);
            return Update(state, systemSymbol, known => known with { Gate = GateState.Unknown, GateCheckedAt = now });
        }

        // Only a gate whose system is cached already: a single cached waypoint would read as a fetched system.
        if (await waypoints.FindAsync(gateSymbol, ct) is { } cached && cached.IsUnderConstruction != gate.IsUnderConstruction)
        {
            await waypoints.UpsertRangeAsync([cached with { IsUnderConstruction = gate.IsUnderConstruction }], ct);
        }

        var seen = gate.IsUnderConstruction ? GateState.UnderConstruction : GateState.Active;
        if (Find(state, systemSymbol)!.Gate == GateState.UnderConstruction && seen == GateState.Active)
        {
            logger.LogInformation("Explore plan: the jump gate {WaypointSymbol} of {SystemSymbol} is built.", gateSymbol, systemSymbol);
        }

        return Update(state, systemSymbol, known => known with { Gate = seen, GateCheckedAt = now });
    }

    /// <summary>Asks for the gates an explored system's gate connects to, and adds the systems they are in.</summary>
    private async Task<ExplorePlanState> LookAtConnectionsAsync(ExplorePlanState state, string systemSymbol, DateTimeOffset now, CancellationToken ct)
    {
        var gateSymbol = Find(state, systemSymbol)!.GateWaypointSymbol;
        IReadOnlyList<string> connections;
        try
        {
            connections = (await port.GetJumpGateConnectionsAsync(systemSymbol, gateSymbol, ct)).Connections;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Explore plan: couldn't ask the jump gate {WaypointSymbol} what it connects to; asking again in an hour.", gateSymbol);
            return Update(state, systemSymbol, known => known with { Connections = [], ConnectionsCheckedAt = now });
        }

        state = Update(state, systemSymbol, known => known with { Connections = connections, ConnectionsCheckedAt = now });
        foreach (var connection in connections)
        {
            var connected = WaypointSymbols.SystemOf(connection);
            state = Ensure(state, connected);
            if (Find(state, connected)!.GateWaypointSymbol.Length == 0)
            {
                state = Update(state, connected, known => known with { GateWaypointSymbol = connection, Gate = GateState.Unknown, GateCheckedAt = null });
            }
        }

        return state;
    }

    /// <summary>
    /// The width of each ring around home that is explored in turn (slice 6.33, D114, <see cref="ExploreAtlas.Ring"/>), in jumps:
    /// <c>Explore.RingWidth</c>, 5 when it gives none. Asked on 2026-10-06: "first the systems within 5 jumps are explored before
    /// going further" (D103); the rings were as wide as the trade reach, <c>Trade.MaxHaulDistance</c>, until on 2026-10-07 the
    /// reach of 9999 made one ring of every system (slice 6.35, D117): "Make a separate ring width setting defaulting to 5".
    /// </summary>
    private async Task<int> RingWidthAsync(CancellationToken ct)
        => await settings.GetAsync<int>(RingWidthSetting, ct) is var jumps and > 0
            ? jumps
            : DefaultRingWidth;

    /// <summary>
    /// The systems left to explore and the explorers wanted for them (slice 6.30, D102): asked on 2026-10-06, "I'd like more
    /// explorers to be added when there are more systems to be discovered. Maybe 1 explorer for every 10 undiscovered
    /// systems?" One for every <c>Explore.SystemsPerExplorer</c> systems the gates reach, or part of that ("Reachable, round
    /// up"), at most <c>Explore.MaxExplorers</c> ("Cap as a setting"; 0 for no cap).
    /// </summary>
    private async Task<ExplorePlanState> CountAsync(ExplorePlanState state, DateTimeOffset now, CancellationToken ct)
    {
        var left = ExploreAtlas.SystemsLeft(state, now);
        var perExplorer = await settings.GetAsync<int>(SystemsPerExplorerSetting, ct);
        var cap = await settings.GetAsync<int>(MaxExplorersSetting, ct);
        var wanted = perExplorer > 0 ? (left + perExplorer - 1) / perExplorer : 0;
        return state with { SystemsLeft = left, ExplorersWanted = cap > 0 ? Math.Min(wanted, cap) : wanted };
    }

    /// <summary>
    /// Buys the next explorer while fewer are there than wanted (slice 6.30, D98, D102), at the cheapest shipyard the gates
    /// reach that sells one, when nothing comes before it in the order ships are bought in (<see cref="IPurchaseOrder"/>): each at
    /// <see cref="PurchaseTier.Explorer"/>, before the probes; asked on 2026-10-07 (slice 6.33, D113), "I'd like Explorers (order
    /// 10) to go in front of probes (order 8)", so the further ones no longer wait for the drones and cargo ships that take
    /// turns. <see cref="IShipPurchaseService"/> keeps the credit reserve, and calls for a ship when none of ours is at the
    /// shipyard (D30). Slice 6.32 (D108): a probe of
    /// ours in the shipyard's system, or on its way there, answers the call (<see cref="ExplorerPurchaseStatus.WaitingForAShipThere"/>);
    /// with none, the command ship does, for every explorer (<see cref="ExplorerPurchaseStatus.CommandShipFetchesIt"/>), so a
    /// further one counts in the order even while none of our ships is in the shipyard's system.
    /// </summary>
    private async Task<ExplorerPurchaseState> BuyAsync(ExplorePlanState state, IReadOnlyList<ShipModel> fleet, int owned, DateTimeOffset now, CancellationToken ct)
    {
        if (owned >= state.ExplorersWanted)
        {
            return new ExplorerPurchaseState();
        }

        const PurchaseTier tier = PurchaseTier.Explorer;
        var reach = ExploreAtlas.Reachable(state, state.HomeSystemSymbol, now);
        var offers = (await shipyards.GetAllAsync(ct))
            .SelectMany(shipyard => shipyard.Ships
                .Where(forSale => forSale.Type.Equals(FleetRoles.ExplorerShipType, StringComparison.OrdinalIgnoreCase) && forSale.PurchasePrice > 0)
                .Select(forSale => (
                    Shipyard: shipyard.WaypointSymbol,
                    System: shipyard.SystemSymbol.Length > 0 ? shipyard.SystemSymbol : WaypointSymbols.SystemOf(shipyard.WaypointSymbol),
                    Price: forSale.PurchasePrice,
                    Scarce: ScarceShips.IsScarce(forSale))))
            .Where(candidate => reach.ContainsKey(candidate.System))
            .ToList();

        // D121: never where the shipyard has it SCARCE.
        var offer = offers
            .Where(candidate => !candidate.Scarce)
            .OrderBy(candidate => candidate.Price)
            .ThenBy(candidate => reach[candidate.System])
            .ThenBy(candidate => candidate.Shipyard, StringComparer.Ordinal)
            .FirstOrDefault();
        if (offer.Shipyard is null)
        {
            return new ExplorerPurchaseState
            {
                Status = offers.Count > 0 ? ExplorerPurchaseStatus.ShipyardsScarce : ExplorerPurchaseStatus.NoShipyardSellsOne,
                Tier = tier,
            };
        }

        var purchase = new ExplorerPurchaseState { Tier = tier, ShipyardWaypointSymbol = offer.Shipyard, Price = offer.Price };
        if (!await purchaseOrder.ReportAsync(AutomationPlan.Explore, new PurchaseNeed(tier, FleetRoles.ExplorerShipType, offer.Shipyard, offer.Price), ct))
        {
            return purchase with { Status = ExplorerPurchaseStatus.WaitingForAnotherPurchase };
        }

        ShipPurchaseResult result;
        try
        {
            result = await shipPurchases.TryPurchaseAsync(FleetRoles.ExplorerShipType, offer.Shipyard, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The ships explore on: a purchase that keeps failing shows as RepeatingError.
            logger.LogWarning(ex, "Explore plan: buying a {ShipType} at {WaypointSymbol} failed.", FleetRoles.ExplorerShipType, offer.Shipyard);
            return purchase;
        }

        if (result is { IsSuccess: true, PurchasedShip: { } bought })
        {
            logger.LogInformation(
                "Explore plan: bought explorer {ShipSymbol} at {WaypointSymbol}, the {Count} of the {Wanted} wanted for the {Left} systems left to explore.",
                bought.Symbol,
                offer.Shipyard,
                owned + 1,
                state.ExplorersWanted,
                state.SystemsLeft);
        }

        return purchase with
        {
            Status = result switch
            {
                { IsSuccess: true } => ExplorerPurchaseStatus.Bought,
                { Failure: ShipPurchaseFailure.OverBudget } => ExplorerPurchaseStatus.WaitingForCredits,
                { Failure: ShipPurchaseFailure.NoShipAtShipyard } => await ProbeAnswersAsync(fleet, offer.System, ct)
                    ? ExplorerPurchaseStatus.WaitingForAShipThere
                    : ExplorerPurchaseStatus.CommandShipFetchesIt,
                { Failure: ShipPurchaseFailure.PriceUnknown } => ExplorerPurchaseStatus.NoShipyardSellsOne,
                { Failure: ShipPurchaseFailure.Scarce } => ExplorerPurchaseStatus.ShipyardsScarce,
                _ => ExplorerPurchaseStatus.None,
            },
            Price = result.EstimatedCost > 0 ? result.EstimatedCost : offer.Price,
        };
    }

    /// <summary>
    /// Whether a probe of ours answers the call of a purchase at a shipyard in <paramref name="system"/> (D30, slice 6.32, D108:
    /// "if there isn't a probe there"): one that counts for that system, there or on its way there, as the probe plan counts
    /// it (<see cref="ProbePlanner.Whereabouts"/>), while the probe plan, which answers the calls, is on. A probe that only
    /// passes through, on its way to another system, doesn't.
    /// </summary>
    private async Task<bool> ProbeAnswersAsync(IReadOnlyList<ShipModel> fleet, string system, CancellationToken ct)
    {
        if (!await settings.IsPlanEnabledAsync(AutomationPlan.ProbeDeployment, ct))
        {
            return false;
        }

        foreach (var probe in fleet.Where(FleetRoles.IsProbe))
        {
            var (_, counts) = ProbePlanner.Whereabouts(probe, await goals.GetActiveGoalAsync(probe.Symbol, ct));
            if (counts.Equals(system, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The command ship's next step, when it is exploring, fetching an explorer, or free (its trip has ended).</summary>
    private async Task<ExplorePlanState> StepAsync(ExplorePlanState state, ShipModel ship, bool explorersExplore, int ringWidth, DateTimeOffset now, CancellationToken ct)
    {
        var goal = await goals.GetActiveGoalAsync(ship.Symbol, ct);
        var assignment = await assignments.FindAsync(ship.Symbol, ct);
        var open = assignment is { CompletedAt: null };
        var ours = open && assignment!.AssignmentType.Equals(AssignmentType, StringComparison.OrdinalIgnoreCase);

        if (open && !ours)
        {
            // The scout plan's or the contract's.
            return state.Status is ExploreStatus.Done ? state : state with { Status = ExploreStatus.Waiting, TargetSystemSymbol = string.Empty };
        }

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return state;
        }

        if (ours)
        {
            if (RefusedGate(goal) is { } refused)
            {
                // The API refused the jump: that gate waits an hour, and the plan chooses again.
                state = Refused(state, refused, now);
                await goals.ClearActiveGoalAsync(ship.Symbol, ct);
            }
            else if (goal is { Status: not GoalStatus.Completed })
            {
                // A step under way; or one the circuit breaker blocked, which waits for someone to look at it.
                return state;
            }

            return await DecideAsync(state, ship, assignment, explorersExplore, ringWidth, now, ct);
        }

        if (!FleetRoles.IsFree(ship, goal, hasOpenAssignment: false))
        {
            return state.Status is ExploreStatus.Done ? state : state with { Status = ExploreStatus.Waiting, TargetSystemSymbol = string.Empty };
        }

        return await DecideAsync(state, ship, assignment: null, explorersExplore, ringWidth, now, ct);
    }

    /// <summary>
    /// Chooses the command ship's next step: scout the system it is in, if not explored yet; while an explorer waits for it at
    /// the shipyard, that shipyard (slice 6.30, D98; every explorer since slice 6.32, D108); once an explorer explores, home;
    /// else a jump towards the nearest system not explored yet, or towards home; at home with nothing left, it is released to
    /// the other plans. A free ship is taken (an <see cref="AssignmentType"/> assignment) only for a step it has to take.
    /// </summary>
    private async Task<ExplorePlanState> DecideAsync(
        ExplorePlanState state,
        ShipModel ship,
        ShipAssignmentDto? assignment,
        bool explorersExplore,
        int ringWidth,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var here = ship.SystemSymbol ?? string.Empty;
        var cached = await waypoints.GetBySystemAsync(here, ct);
        if (here.Length == 0 || cached.Count == 0)
        {
            // Its waypoints aren't fetched yet (LearnAsync); the next pass decides.
            return state;
        }

        state = Ensure(state, here);
        if (Find(state, here)!.ExploredAt is null)
        {
            (var scouting, state) = await ScoutAsync(state, ship, assignment, here, cached, now, ct);
            if (scouting)
            {
                return state with { Status = ExploreStatus.Exploring, TargetSystemSymbol = here, Reason = string.Empty };
            }
        }

        // An explorer that no probe of ours can fetch (D98, D108): the command ship fetches it, before it comes home for the
        // explorers that explore. Only while a way there is known: a flight that found none would end at once, on every pass.
        if (Fetches(state)
            && ExploreAtlas.TryFindJumps(refusals.Apply(state), here, WaypointSymbols.SystemOf(state.Purchase.ShipyardWaypointSymbol), now, out _))
        {
            return await FetchAsync(state, ship, assignment, now, ct);
        }

        if (explorersExplore)
        {
            return await HomeAsync(state, ship, assignment, now, ct);
        }

        var step = ExploreAtlas.Next(state, here, now, Taken(state, ship.Symbol), ringWidth);
        switch (step.Kind)
        {
            case ExploreStepKind.Explore:
            case ExploreStepKind.ReturnHome when assignment is not null:
                if (!await JumpAsync(ship, assignment, step, state.Reason, ct))
                {
                    // Exploring, it waits where it is; a free ship keeps its other work until the credits allow the jump.
                    return state with { Reason = WaitingForCredits, TargetSystemSymbol = step.TargetSystemSymbol };
                }

                return state with
                {
                    Status = step.Kind == ExploreStepKind.Explore ? ExploreStatus.Exploring : ExploreStatus.Returning,
                    TargetSystemSymbol = step.TargetSystemSymbol,
                    Reason = string.Empty,
                };

            case ExploreStepKind.Home:
                if (assignment is not null)
                {
                    await assignments.UpsertAsync(assignment with { CompletedAt = now }, ct);
                    logger.LogInformation(
                        "{EventKind:l}: {Plan} plan for ship {ShipSymbol}: every system the active gates reach is explored ({Explored}); it is home, free for other work.",
                        JournalEvents.PlanCompleted,
                        AutomationPlan.Explore,
                        ship.Symbol,
                        state.Systems.Count(system => system.ExploredAt is not null));
                }

                return state with { Status = ExploreStatus.Done, TargetSystemSymbol = string.Empty, Reason = string.Empty };

            case ExploreStepKind.NoWayHome when assignment is not null:
                return NoWayHomeFor(state, ship, here);

            default:
                // Waiting for a look at a gate (one a pass); or a free ship away from home, which works there: a ship whose role
                // is trading takes routes across systems and stays where its last sale leaves it (slice 6.29, D96). Only a
                // ship that explored comes home (D60).
                return state;
        }
    }

    /// <summary>
    /// The command ship's way home once an explorer explores (slice 6.30, D98): it jumps home and is released there to the
    /// other plans (D60). A free command ship is left to its other work.
    /// </summary>
    private async Task<ExplorePlanState> HomeAsync(ExplorePlanState state, ShipModel ship, ShipAssignmentDto? assignment, DateTimeOffset now, CancellationToken ct)
    {
        if (assignment is null)
        {
            return state with { Status = ExploreStatus.Done, TargetSystemSymbol = string.Empty, Reason = string.Empty };
        }

        var step = ExploreAtlas.HomeFrom(state, ship.SystemSymbol ?? string.Empty, now);
        switch (step.Kind)
        {
            case ExploreStepKind.ReturnHome:
                return await JumpAsync(ship, assignment, step, state.Reason, ct)
                    ? state with { Status = ExploreStatus.Returning, TargetSystemSymbol = step.TargetSystemSymbol, Reason = string.Empty }
                    : state with { Reason = WaitingForCredits, TargetSystemSymbol = step.TargetSystemSymbol };

            case ExploreStepKind.Home:
                await assignments.UpsertAsync(assignment with { CompletedAt = now }, ct);
                logger.LogInformation(
                    "{EventKind:l}: {Plan} plan for ship {ShipSymbol}: the explorers explore now; it is home, free for other work.",
                    JournalEvents.PlanCompleted,
                    AutomationPlan.Explore,
                    ship.Symbol);
                return state with { Status = ExploreStatus.Done, TargetSystemSymbol = string.Empty, Reason = string.Empty };

            case ExploreStepKind.NoWayHome:
                return NoWayHomeFor(state, ship, ship.SystemSymbol ?? string.Empty);

            default:
                return state;
        }
    }

    /// <summary>
    /// Whether the command ship fetches the next explorer (D98, slice 6.32, D108): the purchase calls for a ship that no probe of
    /// ours answers; or the command ship is on its way already, and the purchase still waits, for the credits, for the order
    /// ships are bought in, or for a probe that came into the shipyard's system meanwhile. Once on its way it stays with the
    /// purchase rather than flying home and back.
    /// </summary>
    private static bool Fetches(ExplorePlanState state)
        => state.Purchase.Status == ExplorerPurchaseStatus.CommandShipFetchesIt
            || (state.Status == ExploreStatus.FetchingExplorer
                && state.Purchase.Status is ExplorerPurchaseStatus.WaitingForCredits
                    or ExplorerPurchaseStatus.WaitingForAnotherPurchase
                    or ExplorerPurchaseStatus.WaitingForAShipThere);

    /// <summary>
    /// The command ship fetches an explorer (slice 6.30, D98, D30; every explorer since slice 6.32, D108): it flies to the
    /// shipyard, through the gates, and waits there until the purchase is made (<see cref="BuyAsync"/>). It is sent only while
    /// the order ships are bought in and the credits allow the purchase; while they hold it back once it is on its way, it
    /// waits where it is.
    /// </summary>
    private async Task<ExplorePlanState> FetchAsync(ExplorePlanState state, ShipModel ship, ShipAssignmentDto? assignment, DateTimeOffset now, CancellationToken ct)
    {
        var shipyard = state.Purchase.ShipyardWaypointSymbol;
        await TakeAsync(ship, assignment, ship.SystemSymbol ?? string.Empty, now, ct);
        if (!string.Equals(ship.WaypointSymbol, shipyard, StringComparison.OrdinalIgnoreCase)
            && state.Purchase.Status == ExplorerPurchaseStatus.CommandShipFetchesIt)
        {
            await goals.SetActiveGoalAsync(ship.Symbol, new MoveToWaypointGoal { TargetWaypointSymbol = shipyard }, ct);
            if (state.Status != ExploreStatus.FetchingExplorer)
            {
                logger.LogInformation(
                    "{EventKind:l}: {Plan} plan for ship {ShipSymbol}: flies to {WaypointSymbol} to buy an explorer there, for about {Price}; no probe of ours is there to answer the purchase's call.",
                    JournalEvents.PlanStarted,
                    AutomationPlan.Explore,
                    ship.Symbol,
                    shipyard,
                    state.Purchase.Price);
            }
        }

        return state with { Status = ExploreStatus.FetchingExplorer, TargetSystemSymbol = WaypointSymbols.SystemOf(shipyard), Reason = string.Empty };
    }

    private ExplorePlanState NoWayHomeFor(ExplorePlanState state, ShipModel ship, string here)
    {
        if (state.Reason != NoWayHome)
        {
            logger.LogWarning(
                "{EventKind:l}: {Plan} plan waits ({Reason}): ship {ShipSymbol} is in {SystemSymbol}, and no active gate leads home to {Destination}.",
                JournalEvents.PlanBlocked,
                AutomationPlan.Explore,
                NoWayHome,
                ship.Symbol,
                here,
                state.HomeSystemSymbol);
        }

        return state with { Status = ExploreStatus.Returning, TargetSystemSymbol = state.HomeSystemSymbol, Reason = NoWayHome };
    }

    /// <summary>An explorer's next step (slice 6.30), when it is exploring or free (its trip has ended).</summary>
    private async Task<ExplorePlanState> StepExplorerAsync(ExplorePlanState state, ShipModel ship, int ringWidth, DateTimeOffset now, CancellationToken ct)
    {
        var entry = FindExplorer(state, ship.Symbol) ?? new ExploringShip { ShipSymbol = ship.Symbol, Status = ExploreStatus.Waiting };
        var goal = await goals.GetActiveGoalAsync(ship.Symbol, ct);
        var assignment = await assignments.FindAsync(ship.Symbol, ct);
        var open = assignment is { CompletedAt: null };
        var ours = open && assignment!.AssignmentType.Equals(AssignmentType, StringComparison.OrdinalIgnoreCase);

        if (open && !ours)
        {
            return Put(state, entry with { Status = ExploreStatus.Waiting, TargetSystemSymbol = string.Empty });
        }

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return state;
        }

        if (ours)
        {
            if (RefusedGate(goal) is { } refused)
            {
                state = Refused(state, refused, now);
                await goals.ClearActiveGoalAsync(ship.Symbol, ct);
            }
            else if (goal is { Status: not GoalStatus.Completed })
            {
                return state;
            }

            return await DecideExplorerAsync(state, entry, ship, assignment, ringWidth, now, ct);
        }

        if (!FleetRoles.IsFree(ship, goal, hasOpenAssignment: false))
        {
            // On a trip of another plan, a trade between explorations (D102): it is taken back once the trip ends.
            return Put(state, entry with { Status = ExploreStatus.Waiting, TargetSystemSymbol = string.Empty });
        }

        return await DecideExplorerAsync(state, entry, ship, assignment: null, ringWidth, now, ct);
    }

    /// <summary>
    /// Chooses an explorer's next step (slice 6.30): scout the system it is in, if not explored yet; else a jump towards the
    /// nearest system not explored yet that no other exploring ship has taken; with none left, it is released where it is, and
    /// trades from there until one turns up (D102: "they can trade at the location they are at until a new unexplored location
    /// comes up"). Slice 6.31: an explorer with a warp drive takes the first step of the fastest way, a jump or a warp
    /// (<see cref="ExploreAtlas.NextByWays"/>); with nothing left it scans from where it is (D105, <see cref="ScanAsync"/>),
    /// and, in a system the gates don't reach from home, goes back to one they do before it is released.
    /// </summary>
    private async Task<ExplorePlanState> DecideExplorerAsync(
        ExplorePlanState state,
        ExploringShip entry,
        ShipModel ship,
        ShipAssignmentDto? assignment,
        int ringWidth,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var here = ship.SystemSymbol ?? string.Empty;
        var cached = await waypoints.GetBySystemAsync(here, ct);
        if (here.Length == 0 || cached.Count == 0)
        {
            return Put(state, entry);
        }

        state = Ensure(state, here);
        if (Find(state, here)!.ExploredAt is null)
        {
            (var scouting, state) = await ScoutAsync(state, ship, assignment, here, cached, now, ct);
            if (scouting)
            {
                return Put(state, entry with { Status = ExploreStatus.Exploring, TargetSystemSymbol = here, Reason = string.Empty });
            }
        }

        var step = Warps.HasDrive(ship)
            ? ExploreAtlas.NextByWays(state, await ChartAsync(state, now, ct), WayShip.Of(ship, now), now, Taken(state, ship.Symbol), ringWidth)
            : ExploreAtlas.Next(state, here, now, Taken(state, ship.Symbol), ringWidth);
        switch (step.Kind)
        {
            case ExploreStepKind.Explore:
                return Put(state, await GoAsync(ship, assignment, step, entry.Reason, ct)
                    ? entry with { Status = ExploreStatus.Exploring, TargetSystemSymbol = step.TargetSystemSymbol, Reason = string.Empty }
                    : entry with { Reason = WaitingForCredits, TargetSystemSymbol = step.TargetSystemSymbol });

            case ExploreStepKind.Wait:
                // B76: something that could change the choice isn't known yet, a gate an exploration has just shown, or a system's
                // waypoints for a warp; the plan looks, a pass at a time. A free explorer is kept meanwhile, or the role board gives
                // it the trade role and the trading plan a trip, though systems are left to explore (D102).
                if (assignment is null)
                {
                    await TakeAsync(ship, assignment, here, now, ct);
                    logger.LogInformation(
                        "{EventKind:l}: {Plan} plan for ship {ShipSymbol}: waits in {SystemSymbol} until the plan has looked at the gates and systems it has just learned of, then explores.",
                        JournalEvents.PlanStarted,
                        AutomationPlan.Explore,
                        ship.Symbol,
                        here);
                }

                return Put(state, entry);

            default:
                // Slice 6.31 (D105): nothing left within its ways, it scans from here first, once; the next passes fetch what it
                // found within its warps and choose again. The plan keeps the ship meanwhile, or a trade would take it away.
                (var scanning, state) = await ScanAsync(state, ship, now, ct);
                if (scanning)
                {
                    if (assignment is null)
                    {
                        await TakeAsync(ship, assignment, here, now, ct);
                        logger.LogInformation(
                            "{EventKind:l}: {Plan} plan for ship {ShipSymbol}: scans for the systems around {SystemSymbol}, with nothing left within its ways.",
                            JournalEvents.PlanStarted,
                            AutomationPlan.Explore,
                            ship.Symbol,
                            here);
                    }

                    return Put(state, entry with { Status = ExploreStatus.Exploring, TargetSystemSymbol = here, Reason = Scanning });
                }

                if (step.Kind == ExploreStepKind.Rejoin)
                {
                    // Where the gates don't reach from home, no trader goes either: it goes back to the nearest system they do.
                    return Put(state, await GoAsync(ship, assignment, step, entry.Reason, ct)
                        ? entry with { Status = ExploreStatus.Returning, TargetSystemSymbol = step.TargetSystemSymbol, Reason = NothingToExplore }
                        : entry with { Reason = WaitingForCredits, TargetSystemSymbol = step.TargetSystemSymbol });
                }

                if (assignment is not null)
                {
                    await assignments.UpsertAsync(assignment with { CompletedAt = now }, ct);
                    logger.LogInformation(
                        "{EventKind:l}: {Plan} plan for ship {ShipSymbol}: no system is left for it to explore ({Left} left, each taken); it trades from {SystemSymbol} until one turns up.",
                        JournalEvents.PlanCompleted,
                        AutomationPlan.Explore,
                        ship.Symbol,
                        state.SystemsLeft,
                        here);
                }

                return Put(state, entry with { Status = ExploreStatus.Waiting, TargetSystemSymbol = string.Empty, Reason = NothingToExplore });
        }
    }

    /// <summary>
    /// Scouts the system the ship is in, not explored yet: the ship visits each market and shipyard it has none of cached,
    /// and charts each waypoint that is uncharted and can hold one (D99), the gate first, then the nearest; a system with none
    /// of those, or whose scouting goal has ended, counts as explored now.
    /// </summary>
    /// <returns>Whether the ship was given the scouting goal, and the state.</returns>
    private async Task<(bool Scouting, ExplorePlanState State)> ScoutAsync(
        ExplorePlanState state,
        ShipModel ship,
        ShipAssignmentDto? assignment,
        string here,
        IReadOnlyList<WaypointCacheModel> cached,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var stops = Find(state, here)!.ScoutingSince is null ? await StopsAsync(cached, ship.WaypointSymbol ?? string.Empty, ct) : [];
        if (stops.Count > 0)
        {
            await TakeAsync(ship, assignment, here, now, ct);
            await goals.SetActiveGoalAsync(ship.Symbol, new ExploreSystemGoal { SystemSymbol = here, Stops = stops }, ct);
            logger.LogInformation(
                "Explore plan: ship {ShipSymbol} scouts the {Count} markets, shipyards and uncharted waypoints of {SystemSymbol}, starting at {Destination}.",
                ship.Symbol,
                stops.Count,
                here,
                stops[0]);
            return (true, Update(state, here, system => system with { ScoutingSince = now }));
        }

        state = Update(state, here, system => system with { ExploredAt = now });
        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} explored {SystemSymbol}: {Markets} markets and {Shipyards} shipyards.",
            JournalEvents.SystemExplored,
            ship.Symbol,
            here,
            cached.Count(waypoint => waypoint.HasMarket),
            cached.Count(waypoint => waypoint.HasShipyard));
        return (false, state);
    }

    /// <summary>
    /// Gives the ship the first step of <paramref name="step"/>: a jump (<see cref="JumpAsync"/>), or, for an explorer with a
    /// warp drive (slice 6.31), a warp (<see cref="WarpGoal"/>), which buys no antimatter; a free ship is taken for it.
    /// </summary>
    /// <returns>True when the step was given.</returns>
    private async Task<bool> GoAsync(ShipModel ship, ShipAssignmentDto? assignment, ExploreStep step, string reason, CancellationToken ct)
    {
        if (step.WarpWaypointSymbol.Length == 0)
        {
            return await JumpAsync(ship, assignment, step, reason, ct);
        }

        if (assignment is null)
        {
            await TakeAsync(ship, assignment, ship.SystemSymbol ?? string.Empty, TimeProvider.System.GetUtcNow(), ct);
            logger.LogInformation(
                "{EventKind:l}: {Plan} plan for ship {ShipSymbol}: {Purpose} {SystemSymbol}, warping to {Destination} first; {Jumps} jumps and {Warps} warps, about {Minutes} minutes.",
                JournalEvents.PlanStarted,
                AutomationPlan.Explore,
                ship.Symbol,
                step.Kind == ExploreStepKind.Rejoin ? "goes back to" : "explores",
                step.TargetSystemSymbol,
                step.WarpWaypointSymbol,
                step.Jumps,
                step.Warps,
                Math.Round(step.Seconds / 60));
        }

        await goals.SetActiveGoalAsync(ship.Symbol, new WarpGoal { DestinationWaypointSymbol = step.WarpWaypointSymbol }, ct);
        return true;
    }

    /// <summary>
    /// Scans for the systems around an explorer (slice 6.31, D105, asked on 2026-10-06: "Once none is left in its warp reach,
    /// the explorer scans from where it is, caches what it finds, and warps to those systems, nearest first"): once from each
    /// system, by an explorer with a warp drive and a sensor array, after its cooldown. Every system found is cached with its
    /// position; those within its warps (<see cref="WayShip.MaxWarp"/>) join the plan's systems, and their waypoints are fetched,
    /// one a pass, before a warp goes there (<see cref="LookForWarpsAsync"/>). The API scans only from orbit, so a docked ship, as
    /// a trade leaves it (D102), goes into orbit first (B75). A scan that fails is tried again after a few minutes.
    /// </summary>
    /// <returns>Whether it scanned or waits for its cooldown to; and the state.</returns>
    private async Task<(bool Scanning, ExplorePlanState State)> ScanAsync(ExplorePlanState state, ShipModel ship, DateTimeOffset now, CancellationToken ct)
    {
        var here = ship.SystemSymbol ?? string.Empty;
        if (!Warps.HasDrive(ship)
            || !ship.HasSensorArray
            || Find(state, here) is not { } known
            || known.ScannedAt is not null
            || (known.ScanTriedAt is { } tried && now - tried < ExploreAtlas.RetryAfter))
        {
            return (false, state);
        }

        if (ship.CooldownExpiresAt is { } cooldown && cooldown > now)
        {
            return (true, state);
        }

        ScanSystemsActionResult scan;
        try
        {
            if (ship.LocalStatus == ShipLocalStatus.Docked)
            {
                await orbit.ExecuteAsync(ship.Symbol, ct);
            }

            scan = await port.ScanSystemsAsync(ship.Symbol, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Explore plan: ship {ShipSymbol} couldn't scan for the systems around {SystemSymbol}; trying again in a few minutes.", ship.Symbol, here);
            return (false, Update(state, here, system => system with { ScanTriedAt = now }));
        }

        await ships.UpdateCooldownAsync(ship.Symbol, scan.CooldownExpiresAt ?? now.AddSeconds(scan.CooldownSeconds), ct);
        foreach (var found in scan.Systems)
        {
            await systems.UpsertAsync(new SystemCacheModel(found.Symbol, found.SectorSymbol, found.Type, found.X, found.Y, now), ct);
        }

        var maxWarp = WayShip.Of(ship, now).MaxWarp;
        var within = scan.Systems
            .Where(found => !found.Symbol.Equals(here, StringComparison.OrdinalIgnoreCase) && found.Distance <= maxWarp)
            .Select(found => found.Symbol)
            .ToList();
        var added = within.Count(system => Find(state, system) is null);
        foreach (var system in within)
        {
            state = Ensure(state, system);
        }

        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} scanned from {SystemSymbol}: {Systems} systems, {Within} of them within its warps of {MaxWarp}, {Added} new to the plan.",
            JournalEvents.SystemsScanned,
            ship.Symbol,
            here,
            scan.Systems.Count,
            within.Count,
            maxWarp,
            added);
        return (true, Update(state, here, system => system with { ScannedAt = now, ScanTriedAt = now }));
    }

    /// <summary>The chart the ways of a ship with a warp drive are planned on (slice 6.31), with the jumps and warps refused lately.</summary>
    private Task<WayChart> ChartAsync(ExplorePlanState state, DateTimeOffset now, CancellationToken ct)
        => WayChart.ReadAsync(refusals.Apply(state), systems, waypoints, warpRefusals, now, ct);

    /// <summary>
    /// Gives the ship the jump of <paramref name="step"/>, once the credits after the antimatter leave the floor every ship
    /// purchase keeps (D63); a free ship is taken for it. Until then the plan says why, once.
    /// </summary>
    /// <returns>True when the jump was given.</returns>
    private async Task<bool> JumpAsync(ShipModel ship, ShipAssignmentDto? assignment, ExploreStep step, string reason, CancellationToken ct)
    {
        var (price, floor, credits) = await JumpCostAsync(step.GateWaypointSymbol, ct);
        if (credits - price < floor)
        {
            if (reason != WaitingForCredits)
            {
                logger.LogInformation(
                    "{EventKind:l}: {Plan} plan waits ({Reason}): ship {ShipSymbol} would jump from {WaypointSymbol} to {Destination}, towards {SystemSymbol}; the antimatter costs {Price}, and the jump must leave {Floor} of the {Credits} credits.",
                    JournalEvents.PlanBlocked,
                    AutomationPlan.Explore,
                    WaitingForCredits,
                    ship.Symbol,
                    step.GateWaypointSymbol,
                    step.DestinationGateWaypointSymbol,
                    step.TargetSystemSymbol,
                    price,
                    floor,
                    credits);
            }

            return false;
        }

        if (assignment is null)
        {
            await TakeAsync(ship, assignment, ship.SystemSymbol ?? string.Empty, TimeProvider.System.GetUtcNow(), ct);
            logger.LogInformation(
                "{EventKind:l}: {Plan} plan for ship {ShipSymbol}: {Purpose} {SystemSymbol}, {Jumps} jumps away.",
                JournalEvents.PlanStarted,
                AutomationPlan.Explore,
                ship.Symbol,
                step.Kind switch
                {
                    ExploreStepKind.Explore => "explores",
                    ExploreStepKind.Rejoin => "goes back to",
                    _ => "flies home to",
                },
                step.TargetSystemSymbol,
                step.Jumps);
        }

        await goals.SetActiveGoalAsync(
            ship.Symbol,
            new JumpGoal { GateWaypointSymbol = step.GateWaypointSymbol, DestinationGateWaypointSymbol = step.DestinationGateWaypointSymbol },
            ct);
        return true;
    }

    /// <summary>
    /// What a jump from <paramref name="gate"/> would cost (one ANTIMATTER at the gate's market, as last seen there; 0 while
    /// unknown, when the jump executor fetches it on the spot), the floor it must leave, and the credits.
    /// </summary>
    private async Task<(long Price, long Floor, long Credits)> JumpCostAsync(string gate, CancellationToken ct)
    {
        var market = await markets.FindSnapshotByWaypointAsync(gate, ct);
        var price = market?.TradeGoods.FirstOrDefault(good => good.Symbol.Equals(Antimatter, StringComparison.OrdinalIgnoreCase))?.PurchasePrice ?? 0;
        var floor = Math.Max(0, await settings.GetAsync<long>(CreditReserve.FloorSetting, ct));
        var credits = (await agents.GetAsync(ct))?.Credits ?? 0;
        return (price, floor, credits);
    }

    /// <summary>
    /// What a ship visits in a system the plan hasn't explored: the markets and shipyards it has none of cached, and the
    /// waypoints to chart (<see cref="Charting.ShouldChart"/>, D99). An uncharted gate comes first, as it hides its connections;
    /// then the nearest from where the ship is, then each time the nearest to the last.
    /// </summary>
    private async Task<IReadOnlyList<string>> StopsAsync(IReadOnlyList<WaypointCacheModel> system, string from, CancellationToken ct)
    {
        var marketsSeen = (await markets.GetAllFreshnessAsync(ct)).Select(market => market.WaypointSymbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shipyardsSeen = (await shipyards.GetAllFreshnessAsync(ct)).Select(shipyard => shipyard.WaypointSymbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var left = system
            .Where(waypoint => (waypoint.HasMarket && !marketsSeen.Contains(waypoint.Symbol))
                || (waypoint.HasShipyard && !shipyardsSeen.Contains(waypoint.Symbol))
                || Charting.ShouldChart(waypoint))
            .ToList();

        var at = system.FirstOrDefault(waypoint => waypoint.Symbol.Equals(from, StringComparison.OrdinalIgnoreCase));
        var (x, y) = at is null ? (0, 0) : (at.X, at.Y);
        var stops = new List<string>();
        if (left.FirstOrDefault(waypoint => waypoint.Type.Equals(JumpGateType, StringComparison.OrdinalIgnoreCase) && Charting.ShouldChart(waypoint)) is { } gate)
        {
            stops.Add(gate.Symbol);
            left.Remove(gate);
            (x, y) = (gate.X, gate.Y);
        }

        while (left.Count > 0)
        {
            var nearest = left
                .OrderBy(waypoint => ((long)(waypoint.X - x) * (waypoint.X - x)) + ((long)(waypoint.Y - y) * (waypoint.Y - y)))
                .ThenBy(waypoint => waypoint.Symbol, StringComparer.Ordinal)
                .First();
            stops.Add(nearest.Symbol);
            left.Remove(nearest);
            (x, y) = (nearest.X, nearest.Y);
        }

        return stops;
    }

    /// <summary>Takes a free ship for exploring: other plans leave a ship with an open assignment alone.</summary>
    private async Task TakeAsync(ShipModel ship, ShipAssignmentDto? assignment, string here, DateTimeOffset now, CancellationToken ct)
    {
        if (assignment is { CompletedAt: null })
        {
            return;
        }

        await assignments.UpsertAsync(
            new ShipAssignmentDto(
                ShipSymbol: ship.Symbol,
                AssignmentType: AssignmentType,
                OriginWaypoint: ship.WaypointSymbol,
                DestWaypoint: null,
                CargoSymbol: null,
                ContractId: null,
                StepIndex: 0,
                AssignedAt: now,
                CompletedAt: null),
            ct);
        logger.LogDebug("Explore plan: took ship {ShipSymbol} in {SystemSymbol}.", ship.Symbol, here);
    }

    /// <summary>
    /// The gate of a jump the API refused, from the ship's goal that it blocked: an exploring jump (<see cref="JumpGoal"/>), or
    /// the command ship's flight to the explorers' shipyard (<see cref="MoveToWaypointGoal"/>, whose ways leave that gate alone
    /// already); or an explorer's warp the API refused (<see cref="WarpGoal"/>, slice 6.31), whose system the ways leave alone
    /// already. Null for any other goal.
    /// </summary>
    private static string? RefusedGate(ShipGoal? goal) => goal switch
    {
        JumpGoal { Status: GoalStatus.Blocked, StatusReason: JumpGoalExecutor.RefusedReason } jump => jump.DestinationGateWaypointSymbol,
        MoveToWaypointGoal { Status: GoalStatus.Blocked, StatusReason: GoalJumps.RefusedReason } => string.Empty,
        WarpGoal { Status: GoalStatus.Blocked, StatusReason: WarpGoalExecutor.RefusedReason } => string.Empty,
        _ => null,
    };

    /// <summary>Records that the API refused a jump to a gate: the plan leaves it alone for an hour, then looks at it again.</summary>
    private static ExplorePlanState Refused(ExplorePlanState state, string gateWaypointSymbol, DateTimeOffset now)
    {
        if (gateWaypointSymbol.Length == 0)
        {
            return state;
        }

        var system = WaypointSymbols.SystemOf(gateWaypointSymbol);
        return Find(state, system) is null
            ? state
            : Update(state, system, known => known with { JumpRefusedAt = now, Gate = GateState.Unknown, GateCheckedAt = now });
    }

    /// <summary>
    /// The systems the other exploring ships explore or are on their way to (slice 6.30): each exploring ship takes one no
    /// other has.
    /// </summary>
    private static HashSet<string> Taken(ExplorePlanState state, string shipSymbol)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!state.ShipSymbol.Equals(shipSymbol, StringComparison.OrdinalIgnoreCase) && state.Status == ExploreStatus.Exploring && state.TargetSystemSymbol.Length > 0)
        {
            taken.Add(state.TargetSystemSymbol);
        }

        foreach (var explorer in state.Explorers.Where(explorer => !explorer.ShipSymbol.Equals(shipSymbol, StringComparison.OrdinalIgnoreCase)
            && explorer.Status == ExploreStatus.Exploring
            && explorer.TargetSystemSymbol.Length > 0))
        {
            taken.Add(explorer.TargetSystemSymbol);
        }

        return taken;
    }

    /// <summary>Saves the state when it changed: the tick runs every 5 seconds.</summary>
    private async Task SaveAsync(ExplorePlanState? existing, ExplorePlanState state, DateTimeOffset now, CancellationToken ct)
    {
        if (existing is not null
            && JsonSerializer.Serialize(existing with { UpdatedAt = default }, CompareOptions) == JsonSerializer.Serialize(state with { UpdatedAt = default }, CompareOptions))
        {
            return;
        }

        await plans.UpsertAsync(PlanTypes.Explore, state with { UpdatedAt = now }, ct);
    }

    private static KnownSystem? Find(ExplorePlanState state, string systemSymbol)
        => state.Systems.FirstOrDefault(system => system.SystemSymbol.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase));

    private static ExploringShip? FindExplorer(ExplorePlanState state, string shipSymbol)
        => state.Explorers.FirstOrDefault(explorer => explorer.ShipSymbol.Equals(shipSymbol, StringComparison.OrdinalIgnoreCase));

    private static ExplorePlanState Put(ExplorePlanState state, ExploringShip entry)
        => state with
        {
            Explorers = [.. state.Explorers.Select(explorer => explorer.ShipSymbol.Equals(entry.ShipSymbol, StringComparison.OrdinalIgnoreCase) ? entry : explorer)],
        };

    private static ExplorePlanState Ensure(ExplorePlanState state, string systemSymbol)
        => Find(state, systemSymbol) is null
            ? state with { Systems = [.. state.Systems, new KnownSystem { SystemSymbol = systemSymbol }] }
            : state;

    private static ExplorePlanState Update(ExplorePlanState state, string systemSymbol, Func<KnownSystem, KnownSystem> change)
        => state with
        {
            Systems = [.. state.Systems.Select(system => system.SystemSymbol.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase) ? change(system) : system)],
        };
}
