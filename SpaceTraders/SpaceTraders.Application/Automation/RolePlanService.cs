using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Construction;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Siphoning;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Automation;

/// <summary>The role board (PLAN.md slice 6.9).</summary>
public interface IRolePlanService
{
    /// <summary>One pass of the board: weighs every ship's role again when that is due.</summary>
    /// <param name="cancellationToken">Stops the pass.</param>
    /// <returns>A task that completes when the pass is done.</returns>
    Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The role board (PLAN.md slice 6.9), asked for on 2026-10-02: "Each ship should have a set of potential roles. … A
/// ship should occasionally consider whether its role is still the best thing it can do. This is not only based on
/// its own potential roles but also of other ships." Bootstrapped before the plans whose ships it gives roles, it
/// weighs every ship's role (<see cref="RolePlanner"/>, with <see cref="RoleEstimator"/>'s estimates) when that is due:
/// <list type="bullet">
///   <item>at the first pass after a start, and every <c>Roles.ReconsiderMinutes</c> (10, D41);</item>
///   <item>at once when a ship joins the fleet, when a plan is switched on or off, or when the contract starts or stops
///   wanting ore;</item>
///   <item>at once, at most once a minute, when a ship with more than one role has had no work for a minute: its role
///   had nothing for it.</item>
/// </list>
/// A new role takes effect when the ship's trip ends: the plans only give work to free ships. Each change is
/// journaled (<c>RoleChanged</c>); the state lists every ship's role, why, and what each role would earn it. One drone
/// per SCARCE or LIMITED mineral and area keeps gathering it (slice 6.10b, D48, D53), as the mining and siphon plans buy
/// one per such mineral and area. While a system's jump gate needs materials and the construction plan is on, the ship
/// with the largest hold builds it (slice 6.6, D65); the gate's completion weighs the roles again at once.
/// </summary>
public sealed class RolePlanService(
    IShipRepository ships,
    IShipGoalRepository goals,
    IShipAssignmentRepository assignments,
    IContractMineralPlanRepository contractPlans,
    IMiningContextReader miningContexts,
    ISettingsRepository settings,
    IPlanRepository plans,
    IGatheringRates rates,
    RoleBoardMemory memory,
    TradeEarnings tradeEarnings,
    IConstructionSites constructionSites,
    IAgentRepository agents,
    ITradeContextReader tradeContexts,
    ILogger<RolePlanService> logger) : IRolePlanService
{
    /// <summary>How long a ship with more than one role may have no work before the board weighs the roles again.</summary>
    internal static readonly TimeSpan NoWorkAfter = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public async Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default)
    {
        var now = TimeProvider.System.GetUtcNow();
        var fleet = await ships.GetAllAsync(cancellationToken);
        var withAssignment = (await assignments.GetAllActiveAsync(cancellationToken))
            .Where(assignment => !assignment.CompletedAt.HasValue)
            .Select(assignment => assignment.ShipSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roleSettings = await RoleSettings.ReadAsync(settings, cancellationToken);
        if (roleSettings.Switches.Contains(AutomationPlan.Construction))
        {
            roleSettings = roleSettings with { ConstructionSystems = await ConstructionSystemsAsync(cancellationToken) };
        }

        // Slice 6.29 (D96): a ship abroad, or on a trade trip that takes it there, only trades; the other plans work at home.
        roleSettings = roleSettings with
        {
            BusinessSystems = BusinessSystems.Of(await agents.GetAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase),
        };

        var contractWantsOre = roleSettings.Switches.Contains(AutomationPlan.Contract)
            && await contractPlans.GetAsync(cancellationToken) is { Status: ContractMineralPlanStatus.Active } contract
            && contract.UnitsFulfilled < contract.UnitsRequired;
        // D83, D86: the shuttles the mining plan designated for its far asteroids collect once a drone is parked there; a
        // change weighs the roles again.
        var collectors = roleSettings.Switches.Contains(AutomationPlan.Mining)
            ? Collectors(await plans.GetAsync<MiningAutomationPlanState>(PlanTypes.MiningAutomation, cancellationToken), fleet)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conditions = Conditions(roleSettings.Switches, contractWantsOre, roleSettings.ConstructionSystems, collectors);

        var state = await plans.GetAsync<RolePlanState>(PlanTypes.Roles, cancellationToken);
        var surveying = (state?.Ships ?? [])
            .Where(ship => ship.Role is FleetRole.Survey or FleetRole.Collect)
            .Select(ship => ship.ShipSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A ship that surveys waits for surveys by design, and a collecting shuttle for its drones' ore (D83); any other ship
        // with a choice of roles that stays without work shows that its role had nothing for it.
        var idle = TimeSpan.Zero;
        var trips = new Dictionary<string, ShipGoal>(StringComparer.OrdinalIgnoreCase);
        foreach (var ship in fleet)
        {
            var goal = await goals.GetActiveGoalAsync(ship.Symbol, cancellationToken);
            if (goal is { Status: not GoalStatus.Blocked and not GoalStatus.Completed })
            {
                trips[ship.Symbol] = goal;
            }
        }

        var abroad = Abroad(trips, roleSettings.BusinessSystems);
        foreach (var ship in fleet)
        {
            var free = FleetRoles.IsFree(ship, trips.GetValueOrDefault(ship.Symbol), withAssignment.Contains(ship.Symbol));
            var freeFor = memory.FreeFor(ship.Symbol, free, now);
            if (!surveying.Contains(ship.Symbol) && roleSettings.Available(ship, contractWantsOre, abroad.Contains(ship.Symbol)).Count > 1 && freeFor > idle)
            {
                idle = freeFor;
            }
        }

        var trigger = Due(state, fleet, roleSettings, contractWantsOre, conditions, idle, now);
        if (trigger is null)
        {
            return;
        }

        if (memory.LastEvaluatedAt is null && state is not null)
        {
            SeedRates(state);
        }

        var candidates = await CandidatesAsync(fleet, roleSettings, contractWantsOre, state, trips, abroad, now, cancellationToken);
        var coverage = await CoverageAsync(fleet, trips, cancellationToken);
        var decisions = RolePlanner.Decide(candidates, contractWantsOre, roleSettings.HeadStart, coverage, collectors, roleSettings.ConstructionShips);
        await SaveAsync(state, candidates, decisions, conditions, now, cancellationToken);
        memory.Evaluated(now);
        logger.LogDebug("Role board: weighed the roles of {Ships} ships ({Trigger}).", decisions.Count, trigger);
    }

    /// <summary>
    /// What an evaluation weighs besides the ships, as text: a change weighs the roles again, so the gate's completion frees
    /// its builder at once (slice 6.6).
    /// </summary>
    private static string Conditions(IReadOnlySet<AutomationPlan> switches, bool contractWantsOre, IReadOnlySet<string> constructionSystems, IReadOnlySet<string> collectors)
        => string.Join(',', switches.Order().Select(plan => plan.ToString()))
            + (contractWantsOre ? "|contract wants ore" : string.Empty)
            + (constructionSystems.Count > 0 ? "|gate needs materials in " + string.Join(',', constructionSystems.Order(StringComparer.Ordinal)) : string.Empty)
            + (collectors.Count > 0 ? "|collectors " + string.Join(',', collectors.Order(StringComparer.Ordinal)) : string.Empty);

    /// <summary>
    /// The shuttles the mining plan designated for its far asteroids (slice 6.18, D83) that collect now, by symbol: those of a
    /// point where one of its drones is parked at the asteroid, there and not in flight. Until one is, the shuttle is a cargo
    /// ship like any other and trades (D86, asked on 2026-10-05, when SPECTER-2B waited idle at A2 for three hours while the
    /// drones drifted to B44: "Trade until parked"); the new role takes effect when its trip ends.
    /// </summary>
    private static HashSet<string> Collectors(MiningAutomationPlanState? state, IReadOnlyList<ShipModel> fleet)
    {
        var parkedAt = fleet
            .Where(ship => ship.LocalStatus != ShipLocalStatus.InTransit && !string.IsNullOrWhiteSpace(ship.WaypointSymbol))
            .ToDictionary(ship => ship.Symbol, ship => ship.WaypointSymbol!, StringComparer.OrdinalIgnoreCase);
        return (state?.CollectionPoints ?? [])
            .Where(point => point.DroneSymbols.Any(drone => parkedAt.TryGetValue(drone, out var at)
                && at.Equals(point.AsteroidWaypointSymbol, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(point => point.ShuttleSymbols)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The ships on a trade trip that ends outside the systems the plans do business in (slice 6.29, D96): it sells abroad, where
    /// only trading has work for them, and a new role takes effect where the trip ends. A ship abroad already counts as abroad
    /// anyway (<see cref="RoleSettings.Available"/>).
    /// </summary>
    private static HashSet<string> Abroad(IReadOnlyDictionary<string, ShipGoal> trips, IReadOnlySet<string> businessSystems)
        => trips
            .Where(trip => businessSystems.Count > 0
                && trip.Value is TradeBetweenMarketsGoal trade
                && !businessSystems.Contains(WaypointSymbols.SystemOf(trade.SellWaypointSymbol)))
            .Select(trip => trip.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The systems whose jump gate still needs materials, as the construction cache has it (slice 6.6): the home system, or
    /// none (D68).
    /// </summary>
    private async Task<IReadOnlySet<string>> ConstructionSystemsAsync(CancellationToken cancellationToken)
        => (await constructionSites.CachedNeedingMaterialsAsync(cancellationToken))
            .Select(site => ConstructionPlanner.SystemOf(site.WaypointSymbol))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Why the roles are due to be weighed again, or null when they aren't.</summary>
    private string? Due(
        RolePlanState? state,
        IReadOnlyList<ShipModel> fleet,
        RoleSettings roleSettings,
        bool contractWantsOre,
        string conditions,
        TimeSpan idle,
        DateTimeOffset now)
    {
        var last = memory.LastEvaluatedAt;
        if (last is null || state is null)
        {
            return "start";
        }

        if (now - last.Value >= roleSettings.Reconsider)
        {
            return "interval";
        }

        if (!string.Equals(state.Conditions, conditions, StringComparison.Ordinal))
        {
            return "plans changed";
        }

        var known = state.Ships.Select(ship => ship.ShipSymbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (fleet.Any(ship => !known.Contains(ship.Symbol) && roleSettings.Available(ship, contractWantsOre).Count > 0))
        {
            return "new ship";
        }

        return idle >= NoWorkAfter && now - last.Value >= NoWorkAfter ? "a ship without work" : null;
    }

    /// <summary>
    /// Every ship but the probes, with the roles it could take and its best trips in each, system by system. A ship's trade
    /// trips are those the trading plan would give it as a trader, across the systems within the trade reach (slice 6.29).
    /// </summary>
    private async Task<IReadOnlyList<RoleCandidate>> CandidatesAsync(
        IReadOnlyList<ShipModel> fleet,
        RoleSettings roleSettings,
        bool contractWantsOre,
        RolePlanState? state,
        IReadOnlyDictionary<string, ShipGoal> trips,
        IReadOnlySet<string> abroad,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var current = (state?.Ships ?? [])
            .GroupBy(ship => ship.ShipSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Role, StringComparer.OrdinalIgnoreCase);

        var candidates = new List<RoleCandidate>();
        foreach (var system in fleet
            .Where(ship => !FleetRoles.IsProbe(ship))
            .GroupBy(ship => ship.SystemSymbol ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            RoleContext? context = null;
            foreach (var ship in system.OrderBy(ship => ship.Symbol, StringComparer.Ordinal))
            {
                var roles = roleSettings.Available(ship, contractWantsOre, abroad.Contains(ship.Symbol));
                var options = new List<RoleOption>();
                if (roles.Any(role => role is not FleetRole.Survey and not FleetRole.Construct) && system.Key.Length > 0)
                {
                    if (context is null)
                    {
                        var mining = await miningContexts.ReadAsync(system.Key, cancellationToken);
                        // B67, D87: the trips under way hold their routes and buys, and trading's own earnings cap its estimates.
                        context = new RoleContext(
                            mining,
                            roleSettings.MinProfitPerUnit,
                            roleSettings.FuelReserveCredits,
                            new ChainValues(mining.Map, roleSettings.ChainShare),
                            rates)
                        {
                            Trips = trips,
                            TradeCreditsPerHourAtMost = tradeEarnings.PerHour(now),
                            TradeMap = (await tradeContexts.ReadReachAsync(system.Key, cancellationToken)).Map,
                        };
                    }

                    // Slice 6.29 (D96, D58): a drone trades in its own system between its trips; any other ship as a trader would.
                    var estimates = FleetRoles.IsMiningDrone(ship) || FleetRoles.IsSiphoner(ship) ? context with { TradeMap = context.Map } : context;
                    foreach (var role in roles.Where(role => role is not FleetRole.Survey and not FleetRole.Construct))
                    {
                        options.AddRange(RoleEstimator.Options(estimates, ship, role, RolePlanner.MaxOptionsPerRole));
                    }
                }

                candidates.Add(new RoleCandidate(ship, roles, current.GetValueOrDefault(ship.Symbol, FleetRole.None), options));
            }
        }

        return candidates;
    }

    /// <summary>
    /// The SCARCE or LIMITED minerals of each system with drones, per area (D48, D53): the ores its mining drones could serve
    /// a market short of, and the gases its siphon drones could, each with those drones and the ships whose trip covers it
    /// now.
    /// </summary>
    private async Task<IReadOnlyList<MineralCoverage>> CoverageAsync(
        IReadOnlyList<ShipModel> fleet,
        IReadOnlyDictionary<string, ShipGoal> trips,
        CancellationToken cancellationToken)
    {
        var tanks = fleet
            .GroupBy(ship => ship.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().FuelCapacity, StringComparer.OrdinalIgnoreCase);
        List<(string Ship, CoveringTrip Trip)> mining = [];
        List<(string Ship, CoveringTrip Trip)> siphoning = [];
        foreach (var (ship, goal) in trips)
        {
            if (goal is MineAndSellGoal mine)
            {
                mining.Add((ship, new CoveringTrip(mine.TradeSymbol, mine.SellWaypointSymbol, tanks.GetValueOrDefault(ship))));
            }
            else if (goal is SiphonAndSellGoal siphon)
            {
                siphoning.Add((ship, new CoveringTrip(siphon.TradeSymbol, siphon.SellWaypointSymbol, tanks.GetValueOrDefault(ship))));
            }
        }

        var coverage = new List<MineralCoverage>();
        foreach (var system in fleet
            .Where(ship => (FleetRoles.IsMiningDrone(ship) || FleetRoles.IsSiphoner(ship)) && !string.IsNullOrWhiteSpace(ship.SystemSymbol))
            .GroupBy(ship => ship.SystemSymbol!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(system => system.Key, StringComparer.Ordinal))
        {
            var context = await miningContexts.ReadAsync(system.Key, cancellationToken);
            coverage.AddRange(Minerals(
                context.Map,
                FleetRole.Mine,
                [.. system.Where(FleetRoles.IsMiningDrone)],
                drone => MiningPlanner.ScarceOres(context, drone),
                mining));
            coverage.AddRange(Minerals(
                context.Map,
                FleetRole.Siphon,
                [.. system.Where(FleetRoles.IsSiphoner)],
                drone => SiphonPlanner.ScarceGases(context.Map, drone),
                siphoning));
        }

        return coverage;
    }

    /// <summary>
    /// Each mineral and area some drone could serve a market short of (D53), with those drones and the ships whose trip
    /// covers it. The areas are the drones' own: markets they fly between in CRUISE, with the smallest of their tanks.
    /// </summary>
    private static IEnumerable<MineralCoverage> Minerals(
        TradeMarketMap map,
        FleetRole role,
        IReadOnlyList<ShipModel> drones,
        Func<ShipModel, IReadOnlyList<MineralArea>> scarce,
        IReadOnlyList<(string Ship, CoveringTrip Trip)> working)
    {
        if (drones.Count == 0)
        {
            return [];
        }

        var serves = drones.ToDictionary(drone => drone.Symbol, scarce, StringComparer.OrdinalIgnoreCase);
        bool Serves(ShipModel drone, MineralArea area)
            => serves[drone.Symbol].Any(served => served.Good.Equals(area.Good, StringComparison.OrdinalIgnoreCase)
                && served.MarketSymbols.Any(market => area.MarketSymbols.Contains(market, StringComparer.OrdinalIgnoreCase)));

        return MiningPlanner.Areas(map, drones.Min(drone => drone.FuelCapacity), serves.Values.SelectMany(areas => areas))
            .Select(area => new MineralCoverage(
                area.Good,
                role,
                [.. drones.Where(drone => Serves(drone, area)).Select(drone => drone.Symbol).Order(StringComparer.Ordinal)],
                [.. working.Where(worker => area.MarketSymbols.Any(market => worker.Trip.Covers(map, area.Good, market))).Select(worker => worker.Ship)])
            {
                MarketSymbols = area.MarketSymbols,
            });
    }

    /// <summary>Takes the rates the state kept from before a restart, for ships with no extraction since.</summary>
    private void SeedRates(RolePlanState state)
    {
        foreach (var ship in state.Ships)
        {
            foreach (var rate in ship.Rates)
            {
                rates.Seed(ship.ShipSymbol, rate.Kind, new GatheringRate(rate.UnitsPerAction, rate.SecondsPerAction, rate.Observed));
            }
        }
    }

    /// <summary>Writes the board and journals every role that changed.</summary>
    private async Task SaveAsync(
        RolePlanState? previous,
        IReadOnlyList<RoleCandidate> candidates,
        IReadOnlyList<RoleDecision> decisions,
        string conditions,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var before = (previous?.Ships ?? [])
            .GroupBy(ship => ship.ShipSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var byShip = candidates.ToDictionary(candidate => candidate.Ship.Symbol, StringComparer.OrdinalIgnoreCase);
        var shipStates = new List<RoleShipState>();
        foreach (var decision in decisions)
        {
            var candidate = byShip[decision.ShipSymbol];
            var known = before.GetValueOrDefault(decision.ShipSymbol);
            var changed = known is null || known.Role != decision.Role;
            shipStates.Add(new RoleShipState
            {
                ShipSymbol = decision.ShipSymbol,
                Role = decision.Role,
                Reason = decision.Reason,
                Since = changed ? now : known!.Since,
                Job = decision.Option?.Job ?? string.Empty,
                CreditsPerHour = (long)Math.Round(decision.Option?.CreditsPerHour ?? 0),
                Estimates = Estimates(candidate),
                Rates = Rates(candidate.Ship),
            });

            if (changed && (known is not null || decision.Role != FleetRole.None))
            {
                LogChange(decision, known?.Role ?? FleetRole.None);
            }
        }

        await plans.UpsertAsync(
            PlanTypes.Roles,
            new RolePlanState { EvaluatedAt = now, Conditions = conditions, Ships = shipStates },
            cancellationToken);
    }

    /// <summary>For each role a ship could take but surveying and building, which have no estimate, its best trip per hour.</summary>
    private static IReadOnlyList<RoleEstimateState> Estimates(RoleCandidate candidate)
        => [.. candidate.Roles
            .Where(role => role is not FleetRole.Survey and not FleetRole.Construct)
            .Select(role => candidate.Options.Where(option => option.Role == role).MaxBy(option => option.CreditsPerHour) is { } best
                ? new RoleEstimateState { Role = role, CreditsPerHour = (long)Math.Round(best.CreditsPerHour), Job = best.Job }
                : new RoleEstimateState { Role = role, CreditsPerHour = 0 })];

    /// <summary>The rates the ship's mining and siphon estimates use.</summary>
    private IReadOnlyList<RoleRateState> Rates(ShipModel ship)
    {
        var kept = new List<RoleRateState>();
        foreach (var kind in Kinds(ship))
        {
            var rate = rates.For(ship.Symbol, kind);
            kept.Add(new RoleRateState
            {
                Kind = kind,
                UnitsPerAction = Math.Round(rate.UnitsPerAction, 1),
                SecondsPerAction = Math.Round(rate.SecondsPerAction, 1),
                Observed = rate.Observed,
            });
        }

        return kept;
    }

    private static IEnumerable<GatheringKind> Kinds(ShipModel ship)
    {
        if (FleetRoles.CanMine(ship))
        {
            yield return GatheringKind.Mining;
        }

        if (FleetRoles.CanSiphon(ship))
        {
            yield return GatheringKind.Siphoning;
        }
    }

    private void LogChange(RoleDecision decision, FleetRole oldRole)
    {
        if (decision.Option is { } option)
        {
            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} changes its role from {OldRole} to {NewRole} ({Reason}): about {CreditsPerHour} credits an hour, {Job}.",
                JournalEvents.RoleChanged,
                decision.ShipSymbol,
                oldRole,
                decision.Role,
                decision.Reason,
                (long)Math.Round(option.CreditsPerHour),
                option.Job);
        }
        else
        {
            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} changes its role from {OldRole} to {NewRole} ({Reason}).",
                JournalEvents.RoleChanged,
                decision.ShipSymbol,
                oldRole,
                decision.Role,
                decision.Reason);
        }
    }
}

/// <summary>
/// What the role board remembers between passes, in memory (PLAN.md slice 6.9): when it last weighed the roles, and
/// since when each ship has had no work. A restart forgets both, so the first pass after it weighs the roles again.
/// </summary>
/// <remarks>A singleton; thread-safe.</remarks>
public sealed class RoleBoardMemory
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _freeSince = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _lastEvaluatedAt;

    /// <summary>When the board last weighed the roles in this process; null before the first time.</summary>
    public DateTimeOffset? LastEvaluatedAt
    {
        get
        {
            lock (_gate)
            {
                return _lastEvaluatedAt;
            }
        }
    }

    /// <summary>Records an evaluation.</summary>
    /// <param name="at">When it ran.</param>
    public void Evaluated(DateTimeOffset at)
    {
        lock (_gate)
        {
            _lastEvaluatedAt = at;
        }
    }

    /// <summary>Records whether a ship is free now, and says for how long it has been.</summary>
    /// <param name="shipSymbol">The ship.</param>
    /// <param name="free">Whether it is free now.</param>
    /// <param name="now">The time.</param>
    /// <returns>How long it has been free; zero for a busy ship.</returns>
    public TimeSpan FreeFor(string shipSymbol, bool free, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!free)
            {
                _freeSince.Remove(shipSymbol);
                return TimeSpan.Zero;
            }

            if (!_freeSince.TryGetValue(shipSymbol, out var since))
            {
                since = now;
                _freeSince[shipSymbol] = since;
            }

            return now - since;
        }
    }
}
