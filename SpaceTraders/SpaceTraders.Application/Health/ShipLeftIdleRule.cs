using System.Globalization;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Roles;

namespace SpaceTraders.Application.Health;

/// <summary>
/// A ship without a goal is idle for at most N minutes (PLAN.md 3.2), while work waits for it (D13): a
/// ship with no goal and no assignment, not in transit, doesn't stay so for longer than
/// <c>Health.Ship.MaxIdleMinutes</c> (10) while a plan that is on has work it could give that ship. The
/// subject is the ship.
/// </summary>
/// <remarks>
/// <para>
/// Work that waits, per plan, mirroring which ships each plan treats as free:
/// </para>
/// <list type="bullet">
///   <item>scout: the active plan's next stop, for the plan's ship;</item>
///   <item>contract: the active plan's contract, for the plan's ship and, while units remain, for any
///   other miner (D23, slice 6.4); or, while the plan waits for a ship or budget, any miner;</item>
///   <item>probes: a market whose prices are due, with no probe at it or on its way and no other ship of
///   ours at it, for any probe (slice 6.3, D29); the starting probe is one (B25);</item>
///   <item>survey: a target to survey, for a ship that can survey (D20, slice 6.4), or, with the role board on, the ship
///   with the survey role (slice 6.9), that the plan lists as able to reach it (B55);</item>
///   <item>mining: an opening in low supply without a ship (Pending), for a miner the plan lists as able
///   to reach it (slice 6.4);</item>
///   <item>siphon: likewise, a gas in low supply without a ship, for a siphoner the plan lists as able to
///   reach its gas giant (slice 6.7);</item>
///   <item>trading: a lucrative route without a trader (Pending), for a ship the plan lists as able to
///   take it (slice 6.5);</item>
///   <item>spare time: a place to mine or siphon, for a ship the plan lists with one (slice 6.8): the command ship
///   with nothing to survey or trade;</item>
///   <item>construction: a load of the home system's jump gate's materials, for a builder the plan lists as one a load waits
///   for (slice 6.6). The plan gives a free one its load at once, so only a plan that has stopped leaves it idle.</item>
/// </list>
/// <para>
/// Without such work an idle ship is idle by design: in the first run (D9, D1) the starting probe, the
/// command ship after scouting and the drone after its contract. The clock is the monitor's own (at
/// most since it started), so work that turns up while a ship is idle starts it afresh.
/// </para>
/// </remarks>
public sealed class ShipLeftIdleRule(
    HealthFleet fleet,
    IScoutPlanRepository scoutPlans,
    IContractMineralPlanRepository contractPlans,
    IProbeDeploymentPlanRepository probePlans,
    IPlanRepository plans,
    ISettingsRepository settings) : IHealthRule
{
    /// <summary>The setting that holds the minutes a ship may stay idle while work waits for it.</summary>
    public const string Setting = "Health.Ship.MaxIdleMinutes";

    /// <summary>The minutes when the setting gives none.</summary>
    public const int DefaultMinutes = 10;

    /// <inheritdoc />
    public string Name => "ShipLeftIdle";

    /// <inheritdoc />
    public async Task<IReadOnlyList<HealthViolation>> EvaluateAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var waiting = await WorkWaitingAsync(context, cancellationToken);
        var ships = await fleet.LoadAsync(cancellationToken);
        var limit = TimeSpan.FromMinutes(await settings.ThresholdAsync(Setting, DefaultMinutes, cancellationToken));
        var violations = new List<HealthViolation>();
        foreach (var ship in ships)
        {
            var work = ship.IsIdle && !ship.InTransitAt(context.Now)
                ? waiting.FirstOrDefault(candidate => candidate.CanTake(ship))
                : null;
            var idle = context.Elapsed(context.HeldSince($"idle:{ship.Symbol}", work is not null));
            if (work is not null && idle > limit)
            {
                violations.Add(new HealthViolation(
                    ship.Symbol,
                    string.Create(CultureInfo.InvariantCulture,
                        $"it has had no goal and no assignment for {idle.TotalMinutes:0} minutes, while the {work.Plan} plan has work it could do: {work.What}; it is {ship.Whereabouts}; limit {limit.TotalMinutes:0} minutes ({Setting})")));
            }
        }

        return violations;
    }

    private async Task<IReadOnlyList<WaitingWork>> WorkWaitingAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var waiting = new List<WaitingWork>();

        if (context.IsOn(AutomationPlan.Scout)
            && await scoutPlans.GetAsync(cancellationToken) is { Status: ScoutPlanStatus.Active } scout)
        {
            waiting.Add(new WaitingWork(
                AutomationPlan.Scout,
                "its next scouting stop",
                ship => ship.Symbol.Equals(scout.ShipSymbol, StringComparison.OrdinalIgnoreCase)));
        }

        var surveyOn = context.IsOn(AutomationPlan.Survey);
        var board = await FleetRoleBoard.ReadAsync(settings, plans, cancellationToken, surveyOn);
        if (context.IsOn(AutomationPlan.Contract) && await contractPlans.GetAsync(cancellationToken) is { } contract)
        {
            if (contract.Status == ContractMineralPlanStatus.Active)
            {
                var unitsLeft = contract.UnitsFulfilled < contract.UnitsRequired;
                waiting.Add(new WaitingWork(
                    AutomationPlan.Contract,
                    $"contract {contract.ContractId}",
                    ship => ship.Symbol.Equals(contract.ShipSymbol, StringComparison.OrdinalIgnoreCase)
                        || (unitsLeft && board.MinesForContract(ship.Ship))));
            }
            else if (contract.Status == ContractMineralPlanStatus.PendingBudget)
            {
                waiting.Add(new WaitingWork(
                    AutomationPlan.Contract,
                    $"contract {contract.ContractId} waits for a mining ship",
                    ship => board.MinesForContract(ship.Ship)));
            }
        }

        // Only the targets short of their stock of surveys: with the stock for every ore, a surveyor waits (D27). And only for
        // the surveyors that can reach them, which the plan lists: a designated surveyor's tank doesn't reach every asteroid
        // a miner works (B55).
        if (surveyOn && await plans.GetAsync<SurveyPlanState>(PlanTypes.Survey, cancellationToken) is { } survey)
        {
            var toSurvey = survey.Targets.Where(target => target.NeedsSurvey && target.CandidateShipSymbols.Count > 0).ToList();
            if (toSurvey.Count > 0)
            {
                waiting.Add(new WaitingWork(
                    AutomationPlan.Survey,
                    string.Create(CultureInfo.InvariantCulture, $"{toSurvey.Count} targets to survey"),
                    ship => board.IsSurveyor(ship.Ship) && Lists(toSurvey, ship)));
            }

            // A ship that can only survey surveys on once every ore has its stock (D52): any target it reaches is work.
            var surveyable = survey.Targets.Where(target => target.CandidateShipSymbols.Count > 0).ToList();
            if (surveyable.Count > 0)
            {
                waiting.Add(new WaitingWork(
                    AutomationPlan.Survey,
                    string.Create(CultureInfo.InvariantCulture, $"{surveyable.Count} targets to survey on"),
                    ship => board.IsSurveyor(ship.Ship) && FleetRoles.CanOnlySurvey(ship.Ship) && Lists(surveyable, ship)));
            }
        }

        // The plan gives every free probe a due market that nothing watches (D29), so a probe can only be left
        // idle while one waits when the plan has stopped. A probe that stays at a shipyard for a purchase
        // (D30) waits seconds: a call lasts two minutes after the last attempt.
        if (context.IsOn(AutomationPlan.ProbeDeployment)
            && await probePlans.GetAsync(cancellationToken) is { } probes
            && probes.Markets.Count(market => market.IsUnwatched && market.DueAt <= context.Now) is > 0 and var unwatched)
        {
            waiting.Add(new WaitingWork(
                AutomationPlan.ProbeDeployment,
                string.Create(CultureInfo.InvariantCulture, $"{unwatched} markets that no probe or ship watches are due"),
                ship => FleetRoles.IsProbe(ship.Ship)));
        }

        if (context.IsOn(AutomationPlan.Mining)
            && await plans.GetAsync<MiningAutomationPlanState>(PlanTypes.MiningAutomation, cancellationToken) is { } mining)
        {
            // An opening is work only for a miner that can reach its asteroid: the plan lists those as its
            // candidates (slice 6.4). A drone isn't idle by mistake while the openings are beyond its tank.
            var open = mining.Opportunities
                .Where(opportunity => opportunity.Status == MarketAutomationOpportunityStatus.Pending)
                .ToList();
            if (open.Count > 0)
            {
                waiting.Add(new WaitingWork(
                    AutomationPlan.Mining,
                    string.Create(CultureInfo.InvariantCulture, $"{open.Count} mining opportunities without a ship"),
                    ship => open.Any(opportunity => opportunity.CandidateShipSymbols.Contains(ship.Symbol, StringComparer.OrdinalIgnoreCase))));
            }
        }

        if (context.IsOn(AutomationPlan.Siphon)
            && await plans.GetAsync<MiningAutomationPlanState>(PlanTypes.SiphonAutomation, cancellationToken) is { } siphon)
        {
            // As for the miners: an opening is work only for a siphoner that can reach its gas giant (slice 6.7).
            var open = siphon.Opportunities
                .Where(opportunity => opportunity.Status == MarketAutomationOpportunityStatus.Pending)
                .ToList();
            if (open.Count > 0)
            {
                waiting.Add(new WaitingWork(
                    AutomationPlan.Siphon,
                    string.Create(CultureInfo.InvariantCulture, $"{open.Count} siphon opportunities without a ship"),
                    ship => open.Any(opportunity => opportunity.CandidateShipSymbols.Contains(ship.Symbol, StringComparer.OrdinalIgnoreCase))));
            }
        }

        if (context.IsOn(AutomationPlan.Trading)
            && await plans.GetAsync<TradingAutomationPlanState>(PlanTypes.TradingAutomation, cancellationToken) is { } trading)
        {
            // A lucrative route is work only for a ship that can fly it and finds it lucrative from
            // where it is: the plan lists those as its candidates (slice 6.5). A drone with a small
            // tank isn't idle by mistake while the open routes are beyond its reach.
            var open = trading.Opportunities
                .Where(opportunity => opportunity.Status == MarketAutomationOpportunityStatus.Pending)
                .ToList();
            if (open.Count > 0)
            {
                waiting.Add(new WaitingWork(
                    AutomationPlan.Trading,
                    string.Create(CultureInfo.InvariantCulture, $"{open.Count} lucrative trade routes without a ship"),
                    ship => open.Any(opportunity => opportunity.CandidateShipSymbols.Contains(ship.Symbol, StringComparer.OrdinalIgnoreCase))));
            }
        }

        // The plan gives a trip at once to every free ship it lists with a place to gather at (slice 6.8), so one can
        // only be left idle while the plan has stopped. A ship it lists as waiting has nowhere to gather and sell.
        if (context.IsOn(AutomationPlan.SpareTime)
            && await plans.GetAsync<SpareTimePlanState>(PlanTypes.SpareTime, cancellationToken) is { } spareTime)
        {
            var withSource = spareTime.Ships
                .Where(ship => ship.SourceWaypointSymbol.Length > 0)
                .Select(ship => ship.ShipSymbol)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (withSource.Count > 0)
            {
                waiting.Add(new WaitingWork(
                    AutomationPlan.SpareTime,
                    "a place to mine or siphon in its spare time",
                    ship => withSource.Contains(ship.Symbol)));
            }
        }

        // The plan gives a free builder a load waits for at once (slice 6.6), so one can only be left idle while the plan has
        // stopped.
        if (context.IsOn(AutomationPlan.Construction)
            && await plans.GetAsync<ConstructionPlanState>(PlanTypes.Construction, cancellationToken) is { ReadyShipSymbols.Count: > 0 } construction)
        {
            var ready = construction.ReadyShipSymbols.ToHashSet(StringComparer.OrdinalIgnoreCase);
            waiting.Add(new WaitingWork(
                AutomationPlan.Construction,
                "a load of materials for the jump gate",
                ship => ready.Contains(ship.Symbol)));
        }

        return waiting;
    }

    /// <summary>Whether a ship is one the survey plan lists as able to reach one of the targets (B55).</summary>
    private static bool Lists(IReadOnlyList<SurveyPlanTarget> targets, FleetShip ship)
        => targets.Any(target => target.CandidateShipSymbols.Contains(ship.Symbol, StringComparer.OrdinalIgnoreCase));

    /// <summary>Work a plan has waiting, and which ships could take it.</summary>
    private sealed record WaitingWork(AutomationPlan Plan, string What, Func<FleetShip, bool> CanTake);
}
