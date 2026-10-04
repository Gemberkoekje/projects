using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Exploring;

/// <summary>The explore plan (asked on 2026-10-04).</summary>
public interface IExplorePlanService
{
    /// <summary>
    /// One pass of the plan: learns one thing about the gates it knows, and gives the command ship its next step when it
    /// is free or exploring.
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
///   <item>No limit: the command ship explores every system the active gates reach, the nearest by jumps first
///   (<see cref="ExploreAtlas"/>). A jump needs the gates at both ends built.</item>
///   <item>It takes the command ship once its trip ends (it is free), before the role board and every plan after it.</item>
///   <item>In a system not explored yet it visits each market and shipyard once (<see cref="ExploreSystemGoal"/>); a system
///   with none is explored as soon as it is there. It charts nothing.</item>
///   <item>A jump keeps the credit floor every ship purchase keeps (<see cref="CreditReserve.FloorSetting"/>, 60,000
///   seeded): until the credits after the antimatter stay at or above it, the plan holds the jump, and leaves a free command
///   ship to its other work.</item>
///   <item>Afterwards it comes home, and the other plans give it work again. They do business at home only for now.</item>
/// </list>
/// What it knows of the gates comes from the API: home's gate, then the connections of each system it explores, and the
/// gates those lead to. A pass asks for one of those at most, as the reads give way to the moves (D19); a gate still under
/// construction is looked at again hourly. A system the ship has just jumped into has its waypoints fetched at once.
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
    ILogger<ExplorePlanService> logger) : IExplorePlanService
{
    /// <summary>The type of the command ship's assignment while it explores.</summary>
    public const string AssignmentType = "Explore";

    /// <summary>The command ship's registration role, its cached type after startup sync.</summary>
    public const string CommandShipType = "COMMAND";

    private const string JumpGateType = "JUMP_GATE";
    private const string Antimatter = "ANTIMATTER";
    private const string WaitingForCredits = "waiting_for_credits";
    private const int WaypointPageSize = 20;

    private static readonly JsonSerializerOptions CompareOptions = new();

    /// <summary>Whether a ship is the command ship, the one that explores.</summary>
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
        var agent = await agents.GetAsync(cancellationToken);
        var ship = (await ships.GetAllAsync(cancellationToken)).FirstOrDefault(IsCommandShip);
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

        state = await LearnAsync(state, ship, now, cancellationToken);
        state = await StepAsync(state, ship, now, cancellationToken);
        await SaveAsync(existing, state, now, cancellationToken);
    }

    /// <summary>
    /// Learns what the next decision needs: the waypoints of a system the ship has just jumped into, at once; each known
    /// system's gate from the cached waypoints; then one look at the API, the most needed first (<see cref="NextLook"/>).
    /// </summary>
    private async Task<ExplorePlanState> LearnAsync(ExplorePlanState state, ShipModel ship, DateTimeOffset now, CancellationToken ct)
    {
        if (ship.LocalStatus != ShipLocalStatus.InTransit && ship.SystemSymbol is { Length: > 0 } here)
        {
            state = Ensure(state, here);
            var known = Find(state, here)!;
            if ((await waypoints.GetBySystemAsync(here, ct)).Count == 0)
            {
                if (known.WaypointsCheckedAt is null || now - known.WaypointsCheckedAt.Value >= ExploreAtlas.RetryAfter)
                {
                    return await FetchSystemAsync(state, here, now, ct);
                }

                return state;
            }
        }

        state = await GatesFromCacheAsync(state, now, ct);
        var look = NextLook(state, now);
        return look switch
        {
            (Look.Gate, var system) => await LookAtGateAsync(state, system, now, ct),
            (Look.Connections, var system) => await LookAtConnectionsAsync(state, system, now, ct),
            _ => state,
        };
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
        await waypoints.UpsertRangeAsync([.. fetched.Select(waypoint => ToCache(waypoint, now))], ct);

        var gate = fetched.FirstOrDefault(waypoint => waypoint.Type.Equals(JumpGateType, StringComparison.OrdinalIgnoreCase));
        return Update(state, systemSymbol, known => known with
        {
            WaypointsCheckedAt = now,
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
    /// explored system never asked for; then whatever is due again, the longest waiting first: a gate under construction
    /// after <see cref="ExploreAtlas.RecheckAfter"/>, a look that failed after <see cref="ExploreAtlas.RetryAfter"/>, an
    /// explored system's connections that came back empty after an hour.
    /// </summary>
    private static (Look Kind, string SystemSymbol) NextLook(ExplorePlanState state, DateTimeOffset now)
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

        var due = new List<(DateTimeOffset Since, Look Kind, string SystemSymbol)>();
        foreach (var system in state.Systems.Where(system => system.GateWaypointSymbol.Length > 0))
        {
            var checkedAt = system.GateCheckedAt ?? DateTimeOffset.MinValue;
            if ((system.Gate == GateState.UnderConstruction && now - checkedAt >= ExploreAtlas.RecheckAfter)
                || (system.Gate == GateState.Unknown && now - checkedAt >= ExploreAtlas.RetryAfter))
            {
                due.Add((checkedAt, Look.Gate, system.SystemSymbol));
            }

            if (system.ExploredAt is not null
                && ExploreAtlas.IsUsable(system, now)
                && (system.Connections ?? []).Count == 0
                && system.ConnectionsCheckedAt is { } asked
                && now - asked >= ExploreAtlas.RecheckAfter)
            {
                due.Add((asked, Look.Connections, system.SystemSymbol));
            }
        }

        return due.Count == 0
            ? (Look.None, string.Empty)
            : due.OrderBy(look => look.Since).ThenBy(look => look.SystemSymbol, StringComparer.Ordinal).Select(look => (look.Kind, look.SystemSymbol)).First();
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

    /// <summary>The command ship's next step, when it is exploring or free (its trip has ended).</summary>
    private async Task<ExplorePlanState> StepAsync(ExplorePlanState state, ShipModel ship, DateTimeOffset now, CancellationToken ct)
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
            if (goal is JumpGoal { Status: GoalStatus.Blocked, StatusReason: JumpGoalExecutor.RefusedReason } refused)
            {
                // The API refused the jump: that gate waits an hour, and the plan chooses again.
                state = Refused(state, refused.DestinationGateWaypointSymbol, now);
                await goals.ClearActiveGoalAsync(ship.Symbol, ct);
            }
            else if (goal is { Status: not GoalStatus.Completed })
            {
                // A step under way; or one the circuit breaker blocked, which waits for someone to look at it.
                return state;
            }

            return await DecideAsync(state, ship, assignment, now, ct);
        }

        if (!FleetRoles.IsFree(ship, goal, hasOpenAssignment: false))
        {
            return state.Status is ExploreStatus.Done ? state : state with { Status = ExploreStatus.Waiting, TargetSystemSymbol = string.Empty };
        }

        return await DecideAsync(state, ship, assignment: null, now, ct);
    }

    /// <summary>
    /// Chooses the command ship's next step: scout the system it is in, if not explored yet; else a jump towards the nearest
    /// system not explored yet, or towards home; at home with nothing left, it is released to the other plans. A free ship
    /// is taken (an <see cref="AssignmentType"/> assignment) only for a step it has to take.
    /// </summary>
    private async Task<ExplorePlanState> DecideAsync(
        ExplorePlanState state,
        ShipModel ship,
        ShipAssignmentDto? assignment,
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
        var known = Find(state, here)!;
        if (known.ExploredAt is null)
        {
            var stops = known.ScoutingSince is null ? await StopsAsync(cached, ship.WaypointSymbol ?? string.Empty, ct) : [];
            if (stops.Count > 0)
            {
                await TakeAsync(ship, assignment, here, now, ct);
                await goals.SetActiveGoalAsync(ship.Symbol, new ExploreSystemGoal { SystemSymbol = here, Stops = stops }, ct);
                logger.LogInformation(
                    "Explore plan: ship {ShipSymbol} scouts the {Count} markets and shipyards of {SystemSymbol}, starting at {Destination}.",
                    ship.Symbol,
                    stops.Count,
                    here,
                    stops[0]);
                return Update(state, here, system => system with { ScoutingSince = now }) with
                {
                    Status = ExploreStatus.Exploring,
                    TargetSystemSymbol = here,
                    Reason = string.Empty,
                };
            }

            state = Update(state, here, system => system with { ExploredAt = now });
            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} explored {SystemSymbol}: {Markets} markets and {Shipyards} shipyards.",
                JournalEvents.SystemExplored,
                ship.Symbol,
                here,
                cached.Count(waypoint => waypoint.HasMarket),
                cached.Count(waypoint => waypoint.HasShipyard));
        }

        var step = ExploreAtlas.Next(state, here, now);
        switch (step.Kind)
        {
            case ExploreStepKind.Explore:
            case ExploreStepKind.ReturnHome:
                var (price, floor, credits) = await JumpCostAsync(step.GateWaypointSymbol, ct);
                if (credits - price < floor)
                {
                    if (state.Reason != WaitingForCredits)
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

                    // Exploring, it waits where it is; a free ship keeps its other work until the credits allow the jump.
                    return state with { Reason = WaitingForCredits, TargetSystemSymbol = step.TargetSystemSymbol };
                }

                if (assignment is null)
                {
                    await TakeAsync(ship, assignment, here, now, ct);
                    logger.LogInformation(
                        "{EventKind:l}: {Plan} plan for ship {ShipSymbol}: {Purpose} {SystemSymbol}, {Jumps} jumps away.",
                        JournalEvents.PlanStarted,
                        AutomationPlan.Explore,
                        ship.Symbol,
                        step.Kind == ExploreStepKind.Explore ? "explores" : "flies home to",
                        step.TargetSystemSymbol,
                        step.Jumps);
                }

                await goals.SetActiveGoalAsync(
                    ship.Symbol,
                    new JumpGoal { GateWaypointSymbol = step.GateWaypointSymbol, DestinationGateWaypointSymbol = step.DestinationGateWaypointSymbol },
                    ct);
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
                if (state.Reason != "no_way_home")
                {
                    logger.LogWarning(
                        "{EventKind:l}: {Plan} plan waits ({Reason}): ship {ShipSymbol} is in {SystemSymbol}, and no active gate leads home to {Destination}.",
                        JournalEvents.PlanBlocked,
                        AutomationPlan.Explore,
                        "no_way_home",
                        ship.Symbol,
                        here,
                        state.HomeSystemSymbol);
                }

                return state with { Status = ExploreStatus.Returning, TargetSystemSymbol = state.HomeSystemSymbol, Reason = "no_way_home" };

            default:
                // Waiting for a look at a gate (one a pass), or a free ship away from home with no way back: it works there.
                return state;
        }
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
    /// The markets and shipyards of a system the plan has none of cached, nearest first from where the ship is, then each
    /// time the nearest to the last.
    /// </summary>
    private async Task<IReadOnlyList<string>> StopsAsync(IReadOnlyList<WaypointCacheModel> system, string from, CancellationToken ct)
    {
        var marketsSeen = (await markets.GetAllFreshnessAsync(ct)).Select(market => market.WaypointSymbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shipyardsSeen = (await shipyards.GetAllFreshnessAsync(ct)).Select(shipyard => shipyard.WaypointSymbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var left = system
            .Where(waypoint => (waypoint.HasMarket && !marketsSeen.Contains(waypoint.Symbol))
                || (waypoint.HasShipyard && !shipyardsSeen.Contains(waypoint.Symbol)))
            .ToList();

        var at = system.FirstOrDefault(waypoint => waypoint.Symbol.Equals(from, StringComparison.OrdinalIgnoreCase));
        var (x, y) = at is null ? (0, 0) : (at.X, at.Y);
        var stops = new List<string>();
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

    /// <summary>Takes a free command ship for exploring: other plans leave a ship with an open assignment alone.</summary>
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

    /// <summary>Records that the API refused a jump to a gate: the plan leaves it alone for an hour, then looks at it again.</summary>
    private static ExplorePlanState Refused(ExplorePlanState state, string gateWaypointSymbol, DateTimeOffset now)
    {
        var system = WaypointSymbols.SystemOf(gateWaypointSymbol);
        return Find(state, system) is null
            ? state
            : Update(state, system, known => known with { JumpRefusedAt = now, Gate = GateState.Unknown, GateCheckedAt = now });
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

    private static ExplorePlanState Ensure(ExplorePlanState state, string systemSymbol)
        => Find(state, systemSymbol) is null
            ? state with { Systems = [.. state.Systems, new KnownSystem { SystemSymbol = systemSymbol }] }
            : state;

    private static ExplorePlanState Update(ExplorePlanState state, string systemSymbol, Func<KnownSystem, KnownSystem> change)
        => state with
        {
            Systems = [.. state.Systems.Select(system => system.SystemSymbol.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase) ? change(system) : system)],
        };

    private static WaypointCacheModel ToCache(WaypointDataModel waypoint, DateTimeOffset now)
        => new(
            waypoint.Symbol,
            waypoint.SystemSymbol,
            waypoint.Type,
            waypoint.X,
            waypoint.Y,
            waypoint.HasMarket,
            waypoint.HasShipyard,
            now,
            waypoint.TraitsJson ?? "[]",
            waypoint.ModifiersJson ?? "[]",
            waypoint.OrbitalsJson,
            waypoint.ParentSymbol,
            waypoint.IsUnderConstruction,
            waypoint.ChartJson);
}
