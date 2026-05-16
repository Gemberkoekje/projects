using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using Wolverine;

namespace SpaceTraders.Application.Automation;

public interface IProbeDeploymentPlanService
{
    Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Advances the active probe deployment plan after a probe has been deployed to a waypoint.
    /// Marks the waypoint as deployed and completes the plan when all targets are covered.
    /// </summary>
    Task AdvanceAsync(string waypointSymbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-evaluates deferred Phase 1 deployment when agent credits changed.
    /// </summary>
    Task OnCreditsChangedAsync(CancellationToken cancellationToken = default);
}

public sealed class ProbeDeploymentPlanService(
    IProbeDeploymentPlanRepository probeDeploymentPlans,
    IShipRepository ships,
    IWaypointRepository waypoints,
    IAgentRepository agents,
    IShipyardRepository shipyards,
    IBudgetPolicy budget,
    ISpaceTradersPort port,
    IMessageBus bus,
    ILogger<ProbeDeploymentPlanService> logger) : IProbeDeploymentPlanService
{
    private const string ProbeShipType = "SHIP_PROBE";
    private const long Phase1CapitalThreshold = 200_000;

    public async Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default)
    {
        var existingPlan = await probeDeploymentPlans.GetAsync(cancellationToken);
        if (existingPlan is not null)
        {
            var reconciledPlan = await ReconcileTargetsAsync(existingPlan, cancellationToken);
            if (reconciledPlan.Status == ProbeDeploymentPlanStatus.Active && !reconciledPlan.WaitingForPhase1Credits)
            {
                await ResumeIfDeploymentsPendingAsync(reconciledPlan, cancellationToken);
            }

            return;
        }

        var agent = await agents.GetAsync(cancellationToken);
        if (agent is null || string.IsNullOrWhiteSpace(agent.HeadquartersSymbol))
        {
            logger.LogWarning("Probe deployment plan bootstrap skipped: agent or headquarters not available.");
            return;
        }

        var systemSymbol = ExtractSystemSymbol(agent.HeadquartersSymbol);
        var systemWaypoints = await waypoints.GetBySystemAsync(systemSymbol, cancellationToken);
        var targets = systemWaypoints
            .Where(w => w.HasMarket || w.HasShipyard)
            .Select(w => w.Symbol)
            .ToList();

        var shipyardTargets = systemWaypoints
            .Where(w => w.HasShipyard)
            .Select(w => w.Symbol)
            .ToList();

        if (targets.Count == 0)
        {
            logger.LogWarning(
                "Probe deployment plan bootstrap skipped: no market or shipyard waypoints found in system {System}.",
                systemSymbol);
            return;
        }

        var now = TimeProvider.System.GetUtcNow();
        var plan = new ProbeDeploymentPlanState
        {
            PlanId = Guid.NewGuid(),
            SystemSymbol = systemSymbol,
            TargetWaypointSymbols = targets,
            ShipyardWaypointSymbols = shipyardTargets,
            DeployedWaypointSymbols = [],
            Status = ProbeDeploymentPlanStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await probeDeploymentPlans.UpsertAsync(plan, cancellationToken);

        logger.LogInformation(
            "Probe deployment plan bootstrapped for system {System} with {Count} target waypoints.",
            systemSymbol,
            targets.Count);

        await ResumeIfDeploymentsPendingAsync(plan, cancellationToken);
    }

    public async Task AdvanceAsync(string waypointSymbol, CancellationToken cancellationToken = default)
    {
        var plan = await probeDeploymentPlans.GetAsync(cancellationToken);
        if (plan is null)
        {
            logger.LogWarning(
                "Probe deployment plan advance requested for waypoint {Waypoint} but no active plan found.",
                waypointSymbol);
            return;
        }

        if (plan.Status != ProbeDeploymentPlanStatus.Active)
        {
            logger.LogInformation(
                "Probe deployment plan advance requested for waypoint {Waypoint} but plan status is {Status}; ignoring.",
                waypointSymbol,
                plan.Status);
            return;
        }

        if (plan.DeployedWaypointSymbols.Contains(waypointSymbol, StringComparer.OrdinalIgnoreCase))
        {
            logger.LogDebug(
                "Probe deployment plan advance skipped: waypoint {Waypoint} is already marked deployed.",
                waypointSymbol);
            return;
        }

        var now = TimeProvider.System.GetUtcNow();
        var deployed = plan.DeployedWaypointSymbols
            .Append(waypointSymbol)
            .ToList();

        var allDeployed = plan.TargetWaypointSymbols
            .All(t => deployed.Contains(t, StringComparer.OrdinalIgnoreCase));

        var updated = plan with
        {
            DeployedWaypointSymbols = deployed,
            Status = allDeployed ? ProbeDeploymentPlanStatus.Completed : ProbeDeploymentPlanStatus.Active,
            UpdatedAt = now,
        };

        await probeDeploymentPlans.UpsertAsync(updated, cancellationToken);

        if (allDeployed)
        {
            logger.LogInformation(
                "Probe deployment plan completed: all {Count} waypoints in system {System} are covered.",
                plan.TargetWaypointSymbols.Count,
                plan.SystemSymbol);
            return;
        }

        logger.LogInformation(
            "Probe deployment plan advanced: {Waypoint} marked deployed ({Deployed}/{Total}).",
            waypointSymbol,
            deployed.Count,
            plan.TargetWaypointSymbols.Count);

        await DispatchNextProbeAsync(updated, [], cancellationToken);
    }

    public async Task OnCreditsChangedAsync(CancellationToken cancellationToken = default)
    {
        var plan = await probeDeploymentPlans.GetAsync(cancellationToken);
        if (plan is null
            || plan.Status != ProbeDeploymentPlanStatus.Active
            || !plan.WaitingForPhase1Credits)
        {
            return;
        }

        var resumedPlan = plan with
        {
            WaitingForPhase1Credits = false,
            UpdatedAt = TimeProvider.System.GetUtcNow(),
        };

        await probeDeploymentPlans.UpsertAsync(resumedPlan, cancellationToken);
        await ResumeIfDeploymentsPendingAsync(resumedPlan, cancellationToken);
    }

    private async Task ResumeIfDeploymentsPendingAsync(
        ProbeDeploymentPlanState plan,
        CancellationToken cancellationToken)
    {
        var remaining = plan.TargetWaypointSymbols
            .Where(t => !plan.DeployedWaypointSymbols.Contains(t, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (remaining.Count == 0)
        {
            return;
        }

        // Dispatch one probe per idle available probe, up to the number of remaining targets.
        // Each dispatch mutates the in-memory plan view (tracks which probes are in-flight)
        // so the same probe is never assigned twice in a single resume pass.
        var inFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < remaining.Count; i++)
        {
            var progressed = await DispatchNextProbeAsync(plan, inFlight, cancellationToken);
            if (!progressed)
            {
                break;
            }
        }
    }

    private async Task<ProbeDeploymentPlanState> ReconcileTargetsAsync(
        ProbeDeploymentPlanState plan,
        CancellationToken cancellationToken)
    {
        var systemWaypoints = await waypoints.GetBySystemAsync(plan.SystemSymbol, cancellationToken);
        if (systemWaypoints.Count == 0)
        {
            return plan;
        }

        var discoveredTargets = systemWaypoints
            .Where(w => w.HasMarket || w.HasShipyard)
            .Select(w => w.Symbol)
            .ToList();

        if (discoveredTargets.Count == 0)
        {
            return plan;
        }

        var discoveredShipyards = systemWaypoints
            .Where(w => w.HasShipyard)
            .Select(w => w.Symbol)
            .ToList();

        var currentTargets = plan.TargetWaypointSymbols.ToList();
        var currentShipyards = (plan.ShipyardWaypointSymbols ?? []).ToList();

        var hasNewTargets = false;
        foreach (var target in discoveredTargets)
        {
            if (!currentTargets.Contains(target, StringComparer.OrdinalIgnoreCase))
            {
                currentTargets.Add(target);
                hasNewTargets = true;
            }
        }

        var hasNewShipyards = false;
        foreach (var shipyard in discoveredShipyards)
        {
            if (!currentShipyards.Contains(shipyard, StringComparer.OrdinalIgnoreCase))
            {
                currentShipyards.Add(shipyard);
                hasNewShipyards = true;
            }
        }

        if (!hasNewTargets && !hasNewShipyards)
        {
            return plan;
        }

        var now = TimeProvider.System.GetUtcNow();
        var allDeployed = currentTargets
            .All(t => plan.DeployedWaypointSymbols.Contains(t, StringComparer.OrdinalIgnoreCase));

        var reconciled = plan with
        {
            TargetWaypointSymbols = currentTargets,
            ShipyardWaypointSymbols = currentShipyards,
            Status = allDeployed ? ProbeDeploymentPlanStatus.Completed : ProbeDeploymentPlanStatus.Active,
            UpdatedAt = now,
        };

        await probeDeploymentPlans.UpsertAsync(reconciled, cancellationToken);

        logger.LogInformation(
            "Probe deployment plan reconciled for system {System}: +{TargetCount} targets, +{ShipyardCount} shipyards.",
            plan.SystemSymbol,
            currentTargets.Count - plan.TargetWaypointSymbols.Count,
            currentShipyards.Count - (plan.ShipyardWaypointSymbols ?? []).Count);

        return reconciled;
    }

    private async Task<bool> DispatchNextProbeAsync(
        ProbeDeploymentPlanState plan,
        HashSet<string> inFlight,
        CancellationToken cancellationToken)
    {
        var nextTarget = await SelectNextTargetAsync(plan, inFlight, cancellationToken);

        if (nextTarget is null)
        {
            return false;
        }

        var availableProbe = await FindAvailableProbeAsync(plan, inFlight, cancellationToken);
        if (availableProbe is null)
        {
            logger.LogInformation(
                "Probe deployment plan: no available probe for {Waypoint}; attempting to purchase one.",
                nextTarget);
            await TryPurchaseProbeAsync(plan.SystemSymbol, nextTarget, cancellationToken);
            // Mark this target as in-flight for the current resume pass so we do not
            // repeatedly attempt/log the same purchase when no ship is available at a shipyard.
            inFlight.Add(nextTarget);
            return true;
        }

        inFlight.Add(availableProbe.Symbol);
        inFlight.Add(nextTarget);

        logger.LogInformation(
            "Probe deployment plan: dispatching probe {Symbol} to {Waypoint}.",
            availableProbe.Symbol,
            nextTarget);

        await bus.PublishAsync(new DeployProbeCommand(availableProbe.Symbol, nextTarget));
        return true;
    }

    /// <summary>
    /// Selects the next waypoint to deploy to, respecting phase order and capital gates.
    /// Phase 0 (shipyard waypoints) always dispatches first.
    /// Phase 1 (market-only waypoints) only dispatches when credits reach <see cref="Phase1CapitalThreshold"/>.
    /// </summary>
    private async Task<string?> SelectNextTargetAsync(
        ProbeDeploymentPlanState plan,
        CancellationToken cancellationToken)
    {
        return await SelectNextTargetAsync(plan, [], cancellationToken);
    }

    private async Task<string?> SelectNextTargetAsync(
        ProbeDeploymentPlanState plan,
        HashSet<string> inFlight,
        CancellationToken cancellationToken)
    {
        // Phase 0: shipyard waypoints — no capital gate.
        // Guard against plans persisted before ShipyardWaypointSymbols was introduced.
        var shipyardTargets = plan.ShipyardWaypointSymbols ?? [];
        var nextShipyard = shipyardTargets
            .FirstOrDefault(t =>
                !plan.DeployedWaypointSymbols.Contains(t, StringComparer.OrdinalIgnoreCase)
                && !inFlight.Contains(t, StringComparer.OrdinalIgnoreCase));

        if (nextShipyard is not null)
        {
            return nextShipyard;
        }

        // Phase 1: market-only waypoints — require Phase1CapitalThreshold credits.
        var nextMarket = plan.TargetWaypointSymbols
            .FirstOrDefault(t =>
                !shipyardTargets.Contains(t, StringComparer.OrdinalIgnoreCase)
                && !plan.DeployedWaypointSymbols.Contains(t, StringComparer.OrdinalIgnoreCase)
                && !inFlight.Contains(t, StringComparer.OrdinalIgnoreCase));

        if (nextMarket is null)
        {
            return null;
        }

        var agent = await agents.GetAsync(cancellationToken);
        if (agent is null || agent.Credits < Phase1CapitalThreshold)
        {
            if (!plan.WaitingForPhase1Credits)
            {
                logger.LogInformation(
                    "Probe deployment plan: Phase 1 deferred — credits {Credits} below threshold {Threshold}.",
                    agent?.Credits ?? 0,
                    Phase1CapitalThreshold);

                var deferredPlan = plan with
                {
                    WaitingForPhase1Credits = true,
                    UpdatedAt = TimeProvider.System.GetUtcNow(),
                };

                await probeDeploymentPlans.UpsertAsync(deferredPlan, cancellationToken);
            }

            return null;
        }

        return nextMarket;
    }

    private async Task<ShipModel?> FindAvailableProbeAsync(
        ProbeDeploymentPlanState plan,
        HashSet<string> inFlight,
        CancellationToken cancellationToken)
    {
        var allShips = await ships.GetAllAsync(cancellationToken);
        return allShips.FirstOrDefault(s =>
            IsProbeShip(s)
            && s.LocalStatus != ShipLocalStatus.InTransit
            && !inFlight.Contains(s.Symbol)
            && !plan.DeployedWaypointSymbols.Contains(s.WaypointSymbol ?? string.Empty, StringComparer.OrdinalIgnoreCase));
    }

    private async Task TryPurchaseProbeAsync(string systemSymbol, string targetWaypoint, CancellationToken cancellationToken)
    {
        var shipyardList = await shipyards.GetAllAsync(cancellationToken);

        var shipyard = shipyardList
            .FirstOrDefault(s => s.SystemSymbol.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase)
                && s.ShipTypes.Contains(ProbeShipType, StringComparer.OrdinalIgnoreCase));

        if (shipyard is null)
        {
            logger.LogWarning(
                "Probe deployment plan: no known shipyard in system {System} sells {Type}; will retry.",
                systemSymbol,
                ProbeShipType);
            return;
        }

        var estimatedCost = shipyard.Ships
            .FirstOrDefault(s => ProbeShipType.Equals(s.Type, StringComparison.OrdinalIgnoreCase))
            ?.PurchasePrice ?? 0;

        var decision = await budget.EvaluateAsync(estimatedCost, cancellationToken);
        if (!decision.CanAfford)
        {
            logger.LogInformation(
                "Probe deployment plan: cannot afford probe at {Shipyard} — {Reason}",
                shipyard.WaypointSymbol,
                decision.Reason);
            return;
        }

        logger.LogInformation(
            "Probe deployment plan: purchasing {Type} at {Shipyard} (estimated cost {Cost}).",
            ProbeShipType,
            shipyard.WaypointSymbol,
            estimatedCost);

        var result = await port.PurchaseShipAsync(ProbeShipType, shipyard.WaypointSymbol, cancellationToken);

        await agents.UpsertAsync(result.Agent, cancellationToken);

        var newShip = new ShipModel(
            result.ShipSymbol,
            result.ShipNav.SystemSymbol,
            result.ShipNav.WaypointSymbol,
            result.ShipNav.Status,
            result.ShipNav.FlightMode,
            result.ShipFuel.Current,
            result.ShipFuel.Capacity,
            result.ShipNav.ArrivesAt,
            result.ShipNav.DestWaypointSymbol);

        await ships.UpsertAsync(newShip, cancellationToken);

        logger.LogInformation(
            "Probe deployment plan: purchased {Symbol}; dispatching to {Waypoint}.",
            result.ShipSymbol,
            targetWaypoint);

        await bus.PublishAsync(new DeployProbeCommand(result.ShipSymbol, targetWaypoint));
    }

    private static bool IsProbeShip(ShipModel ship)
        => ship.ShipType.Equals(ProbeShipType, StringComparison.OrdinalIgnoreCase)
           || ship.Symbol.Contains("PROBE", StringComparison.OrdinalIgnoreCase)
           || ship.Symbol.Contains("SATELLITE", StringComparison.OrdinalIgnoreCase);

    private static string ExtractSystemSymbol(string waypointSymbol)
    {
        var parts = waypointSymbol.Split('-');
        return parts.Length >= 2 ? $"{parts[0]}-{parts[1]}" : waypointSymbol;
    }
}
