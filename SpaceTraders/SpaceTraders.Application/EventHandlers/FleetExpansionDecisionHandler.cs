using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Fleet;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.EventHandlers;

public sealed class FleetExpansionDecisionHandler(
    IAgentRepository agents,
    IShipRepository ships,
    IWaypointRepository waypoints,
    IShipAssignmentRepository assignments,
    IShipyardRepository shipyards,
    ISettingsRepository settings,
    IMessageBus bus,
    ILogger<FleetExpansionDecisionHandler> logger)
{
    private const string MarketProbeAssignmentType = "MarketProbe";

    /// <summary>
    /// Phase 13d: fleet expansion is now triggered by the Tier 1 <see cref="GoalCompletedEvent"/>
    /// instead of the Tier 2 <see cref="ShipCargoSoldEvent"/>.
    /// </summary>
    public async Task Handle(GoalCompletedEvent @event, CancellationToken cancellationToken)
        => await EvaluateExpansionAsync(cancellationToken);

    public async Task Handle(ContractFulfilledEvent @event, CancellationToken cancellationToken)
        => await EvaluateExpansionAsync(cancellationToken);

    private async Task EvaluateExpansionAsync(CancellationToken cancellationToken)
    {
        var agent = await agents.GetAsync(cancellationToken);
        if (agent is null)
        {
            return;
        }

        var maxShips = await settings.GetAsync<int>("FleetExpansion.MaxShips", cancellationToken);
        if (maxShips > 0 && agent.ShipCount >= maxShips)
        {
            logger.LogDebug("Fleet at cap ({ShipCount}/{MaxShips}); skipping expansion check.", agent.ShipCount, maxShips);
            return;
        }

        var minCreditReserve = await settings.GetAsync<long>("FleetExpansion.MinCreditReserve", cancellationToken);
        if (agent.Credits <= minCreditReserve)
        {
            logger.LogDebug(
                "Credits {Credits} at or below reserve {Reserve}; skipping expansion check.",
                agent.Credits,
                minCreditReserve);
            return;
        }

        var uncoveredMarket = await FindUncoveredMarketAsync(cancellationToken);
        if (uncoveredMarket is not null)
        {
            var probePurchase = await TryBuildMarketProbePurchaseAsync(uncoveredMarket.Symbol, cancellationToken);
            if (probePurchase is not null)
            {
                logger.LogInformation(
                    "Fleet expansion: purchasing {ShipType} at {Waypoint} for uncovered market {MarketWaypoint} (credits: {Credits}, ships: {ShipCount}/{MaxShips}).",
                    probePurchase.ShipType,
                    probePurchase.ShipyardWaypoint,
                    uncoveredMarket.Symbol,
                    agent.Credits,
                    agent.ShipCount,
                    maxShips);

                await bus.SendAsync(probePurchase);
                return;
            }
        }

        var preferredType = await settings.GetAsync<string>("FleetExpansion.PreferredShipType", cancellationToken)
                            ?? "SHIP_MINING_DRONE";

        var shipyardWaypoint = await shipyards.FindShipyardForTypeAsync(preferredType, cancellationToken);
        if (shipyardWaypoint is null)
        {
            logger.LogInformation("No known shipyard carries {ShipType}; cannot expand fleet yet.", preferredType);
            return;
        }

        logger.LogInformation(
            "Fleet expansion: purchasing {ShipType} at {Waypoint} (credits: {Credits}, ships: {ShipCount}/{MaxShips}).",
            preferredType,
            shipyardWaypoint,
            agent.Credits,
            agent.ShipCount,
            maxShips);

        await bus.SendAsync(new PurchaseShipCommand(preferredType, shipyardWaypoint));
    }

    private async Task<WaypointCacheModel?> FindUncoveredMarketAsync(CancellationToken cancellationToken)
    {
        var fleet = await ships.GetAllAsync(cancellationToken);
        if (fleet.Count == 0)
        {
            return null;
        }

        var systems = fleet
            .Select(s => s.SystemSymbol)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (systems.Count == 0)
        {
            return null;
        }

        var knownMarkets = new List<WaypointCacheModel>();
        foreach (var system in systems)
        {
            var systemWaypoints = await waypoints.GetBySystemAsync(system!, cancellationToken);
            knownMarkets.AddRange(systemWaypoints.Where(w => w.HasMarket));
        }

        if (knownMarkets.Count == 0)
        {
            return null;
        }

        var activeAssignments = await assignments.GetAllActiveAsync(cancellationToken);
        var coveredMarkets = activeAssignments
            .Where(a =>
                a.AssignmentType.Equals(MarketProbeAssignmentType, StringComparison.OrdinalIgnoreCase)
                && !a.CompletedAt.HasValue
                && !string.IsNullOrWhiteSpace(a.OriginWaypoint))
            .Select(a => a.OriginWaypoint!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return knownMarkets
            .Where(m => !coveredMarkets.Contains(m.Symbol))
            .OrderBy(m => m.LastObservedAt)
            .FirstOrDefault();
    }

    private async Task<PurchaseShipCommand?> TryBuildMarketProbePurchaseAsync(string marketWaypoint, CancellationToken cancellationToken)
    {
        var probeShipyard = await shipyards.FindShipyardForTypeAsync("SHIP_PROBE", cancellationToken);
        if (!string.IsNullOrWhiteSpace(probeShipyard))
        {
            return new PurchaseShipCommand(
                "SHIP_PROBE",
                probeShipyard,
                TargetAssignmentType: MarketProbeAssignmentType,
                TargetOriginWaypoint: marketWaypoint);
        }

        var satelliteShipyard = await shipyards.FindShipyardForTypeAsync("SHIP_SATELLITE", cancellationToken);
        if (!string.IsNullOrWhiteSpace(satelliteShipyard))
        {
            return new PurchaseShipCommand(
                "SHIP_SATELLITE",
                satelliteShipyard,
                TargetAssignmentType: MarketProbeAssignmentType,
                TargetOriginWaypoint: marketWaypoint);
        }

        return null;
    }
}
