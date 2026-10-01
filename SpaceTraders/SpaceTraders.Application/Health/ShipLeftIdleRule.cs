using System.Globalization;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;

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
///   <item>contract: the active plan's contract, for the plan's ship; or, while the plan waits for a
///   ship or budget, any mining-capable ship;</item>
///   <item>probe deployment: a target without a probe, while the plan isn't waiting for credits, for a
///   probe that isn't parked at a deployed waypoint. A probe is what the plan recognises, or a ship
///   whose cached type is <c>SATELLITE</c>: the starting probe, which the plan misses (B25);</item>
///   <item>mining and trading: an opportunity without a ship (Pending), for a ship that plan can use.</item>
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

    private const string SatelliteType = "SATELLITE";

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

    /// <summary>A probe as the probe plan recognises it, or the starting probe, which it misses (B25).</summary>
    private static bool IsProbe(FleetShip ship)
        => ProbeDeploymentPlanService.IsProbeShip(ship.Ship)
           || ship.Ship.ShipType.Equals(SatelliteType, StringComparison.OrdinalIgnoreCase);

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

        if (context.IsOn(AutomationPlan.Contract) && await contractPlans.GetAsync(cancellationToken) is { } contract)
        {
            if (contract.Status == ContractMineralPlanStatus.Active)
            {
                waiting.Add(new WaitingWork(
                    AutomationPlan.Contract,
                    $"contract {contract.ContractId}",
                    ship => ship.Symbol.Equals(contract.ShipSymbol, StringComparison.OrdinalIgnoreCase)));
            }
            else if (contract.Status == ContractMineralPlanStatus.PendingBudget)
            {
                waiting.Add(new WaitingWork(
                    AutomationPlan.Contract,
                    $"contract {contract.ContractId} waits for a mining ship",
                    ship => ship.Ship.IsMiningCapable));
            }
        }

        if (context.IsOn(AutomationPlan.ProbeDeployment)
            && await probePlans.GetAsync(cancellationToken) is { Status: ProbeDeploymentPlanStatus.Active, WaitingForPhase1Credits: false } probe)
        {
            var deployed = probe.DeployedWaypointSymbols.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var targetsLeft = probe.TargetWaypointSymbols.Count(target => !deployed.Contains(target));
            if (targetsLeft > 0)
            {
                waiting.Add(new WaitingWork(
                    AutomationPlan.ProbeDeployment,
                    string.Create(CultureInfo.InvariantCulture, $"{targetsLeft} target waypoints without a probe"),
                    ship => IsProbe(ship) && !deployed.Contains(ship.Ship.WaypointSymbol ?? string.Empty)));
            }
        }

        if (context.IsOn(AutomationPlan.Mining)
            && await plans.GetAsync<MiningAutomationPlanState>(PlanTypes.MiningAutomation, cancellationToken) is { } mining)
        {
            var open = mining.Opportunities.Count(opportunity => opportunity.Status == MarketAutomationOpportunityStatus.Pending);
            if (open > 0)
            {
                waiting.Add(new WaitingWork(
                    AutomationPlan.Mining,
                    string.Create(CultureInfo.InvariantCulture, $"{open} mining opportunities without a ship"),
                    ship => ship.Ship.IsMiningCapable));
            }
        }

        if (context.IsOn(AutomationPlan.Trading)
            && await plans.GetAsync<TradingAutomationPlanState>(PlanTypes.TradingAutomation, cancellationToken) is { } trading)
        {
            var open = trading.Opportunities.Count(opportunity => opportunity.Status == MarketAutomationOpportunityStatus.Pending);
            if (open > 0)
            {
                waiting.Add(new WaitingWork(
                    AutomationPlan.Trading,
                    string.Create(CultureInfo.InvariantCulture, $"{open} trading opportunities without a ship"),
                    ship => ship.Ship.IsTradingCapable
                        && ship.Ship.FuelCurrent > 0
                        && ship.Ship.CargoCapacity > ship.Ship.CargoCurrent));
            }
        }

        return waiting;
    }

    /// <summary>Work a plan has waiting, and which ships could take it.</summary>
    private sealed record WaitingWork(AutomationPlan Plan, string What, Func<FleetShip, bool> CanTake);
}
