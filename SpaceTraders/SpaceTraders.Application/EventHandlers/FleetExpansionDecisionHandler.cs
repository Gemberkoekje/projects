using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Fleet;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.EventHandlers;

public sealed class FleetExpansionDecisionHandler(
    IAgentRepository agents,
    IShipRepository ships,
    IShipyardRepository shipyards,
    ISettingsRepository settings,
    IMessageBus bus,
    ILogger<FleetExpansionDecisionHandler> logger)
{
    public async Task Handle(ShipCargoSoldEvent @event, CancellationToken cancellationToken)
        => await EvaluateExpansionAsync(cancellationToken);

    public async Task Handle(ContractFulfilledEvent @event, CancellationToken cancellationToken)
        => await EvaluateExpansionAsync(cancellationToken);

    private async Task EvaluateExpansionAsync(CancellationToken cancellationToken)
    {
        var agent = await agents.GetAsync(cancellationToken);
        if (agent is null) return;

        var maxShips = await settings.GetAsync<int>("FleetExpansion.MaxShips", cancellationToken);
        if (maxShips > 0 && agent.ShipCount >= maxShips)
        {
            logger.LogDebug("Fleet at cap ({ShipCount}/{MaxShips}); skipping expansion check.", agent.ShipCount, maxShips);
            return;
        }

        var minCreditReserve = await settings.GetAsync<long>("FleetExpansion.MinCreditReserve", cancellationToken);
        if (agent.Credits <= minCreditReserve)
        {
            logger.LogDebug("Credits {Credits} at or below reserve {Reserve}; skipping expansion check.",
                agent.Credits, minCreditReserve);
            return;
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
            preferredType, shipyardWaypoint, agent.Credits, agent.ShipCount, maxShips);

        await bus.SendAsync(new PurchaseShipCommand(preferredType, shipyardWaypoint));
    }
}
