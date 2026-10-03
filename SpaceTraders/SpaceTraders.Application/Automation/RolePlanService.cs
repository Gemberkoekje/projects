using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Domain.Enums;

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
/// journaled (<c>RoleChanged</c>); the state lists every ship's role, why, and what each role would earn it.
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
        var contractWantsOre = roleSettings.Switches.Contains(AutomationPlan.Contract)
            && await contractPlans.GetAsync(cancellationToken) is { Status: ContractMineralPlanStatus.Active } contract
            && contract.UnitsFulfilled < contract.UnitsRequired;
        var conditions = Conditions(roleSettings.Switches, contractWantsOre);

        var state = await plans.GetAsync<RolePlanState>(PlanTypes.Roles, cancellationToken);
        var surveying = (state?.Ships ?? [])
            .Where(ship => ship.Role == FleetRole.Survey)
            .Select(ship => ship.ShipSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A ship that surveys waits for surveys by design; any other ship with a choice of roles that stays without work
        // shows that its role had nothing for it.
        var idle = TimeSpan.Zero;
        foreach (var ship in fleet)
        {
            var free = FleetRoles.IsFree(ship, await goals.GetActiveGoalAsync(ship.Symbol, cancellationToken), withAssignment.Contains(ship.Symbol));
            var freeFor = memory.FreeFor(ship.Symbol, free, now);
            if (!surveying.Contains(ship.Symbol) && roleSettings.Available(ship, contractWantsOre).Count > 1 && freeFor > idle)
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

        var candidates = await CandidatesAsync(fleet, roleSettings, contractWantsOre, state, cancellationToken);
        var decisions = RolePlanner.Decide(candidates, contractWantsOre, roleSettings.HeadStart);
        await SaveAsync(state, candidates, decisions, conditions, now, cancellationToken);
        memory.Evaluated(now);
        logger.LogDebug("Role board: weighed the roles of {Ships} ships ({Trigger}).", decisions.Count, trigger);
    }

    /// <summary>What an evaluation weighs besides the ships, as text: a change weighs the roles again.</summary>
    private static string Conditions(IReadOnlySet<AutomationPlan> switches, bool contractWantsOre)
        => string.Join(',', switches.Order().Select(plan => plan.ToString())) + (contractWantsOre ? "|contract wants ore" : string.Empty);

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

    /// <summary>Every ship but the probes, with the roles it could take and its best trips in each, system by system.</summary>
    private async Task<IReadOnlyList<RoleCandidate>> CandidatesAsync(
        IReadOnlyList<ShipModel> fleet,
        RoleSettings roleSettings,
        bool contractWantsOre,
        RolePlanState? state,
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
                var roles = roleSettings.Available(ship, contractWantsOre);
                var options = new List<RoleOption>();
                if (roles.Any(role => role != FleetRole.Survey) && system.Key.Length > 0)
                {
                    if (context is null)
                    {
                        var mining = await miningContexts.ReadAsync(system.Key, cancellationToken);
                        context = new RoleContext(
                            mining,
                            roleSettings.MinProfitPerUnit,
                            roleSettings.FuelReserveCredits,
                            new ChainValues(mining.Map, roleSettings.ChainShare),
                            rates);
                    }

                    foreach (var role in roles.Where(role => role != FleetRole.Survey))
                    {
                        options.AddRange(RoleEstimator.Options(context, ship, role, RolePlanner.MaxOptionsPerRole));
                    }
                }

                candidates.Add(new RoleCandidate(ship, roles, current.GetValueOrDefault(ship.Symbol, FleetRole.None), options));
            }
        }

        return candidates;
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

    /// <summary>For each role a ship could take but surveying, its best trip per hour.</summary>
    private static IReadOnlyList<RoleEstimateState> Estimates(RoleCandidate candidate)
        => [.. candidate.Roles
            .Where(role => role != FleetRole.Survey)
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
