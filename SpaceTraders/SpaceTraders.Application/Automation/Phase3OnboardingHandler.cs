using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Contracts;
using SpaceTraders.Application.Commands.Fleet;
using SpaceTraders.Application.Commands.Sync;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Sync;
using Wolverine;

namespace SpaceTraders.Application.Automation;

public sealed class Phase3OnboardingHandler(
    IAgentRepository agents,
    IContractRepository contracts,
    IShipRepository ships,
    IWaypointRepository waypoints,
    IShipyardRepository shipyards,
    IMessageBus bus,
    ILogger<Phase3OnboardingHandler> logger)
{
    public async Task Handle(RunPhase3OnboardingCommand command, CancellationToken cancellationToken)
    {
        var agent = await agents.GetAsync(cancellationToken);
        if (agent is null)
        {
            logger.LogWarning("Phase 3 onboarding skipped: agent not found in cache.");
            return;
        }

        var allShips = await ships.GetAllAsync(cancellationToken);
        if (allShips.Count == 0)
        {
            logger.LogWarning("Phase 3 onboarding skipped: no ships found.");
            return;
        }

        var primaryShip = allShips
            .OrderBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase)
            .First();

        if (!string.IsNullOrWhiteSpace(primaryShip.SystemSymbol))
        {
            await bus.InvokeAsync(new RefreshSystemDataCommand(primaryShip.SystemSymbol, ForceRefresh: true), cancellationToken);
        }

        var activeContracts = await contracts.GetActiveAsync(cancellationToken);
        var starterContract = activeContracts
            .Where(c => !c.IsFulfilled)
            .OrderBy(c => c.DeadlineToAccept ?? DateTimeOffset.MaxValue)
            .FirstOrDefault();

        if (starterContract is not null && !starterContract.IsAccepted)
        {
            await bus.InvokeAsync(new AcceptContractCommand(starterContract.Id), cancellationToken);
            starterContract = await contracts.FindAsync(starterContract.Id, cancellationToken) ?? starterContract;
        }

        if (starterContract is null)
        {
            if (string.Equals(primaryShip.Status, "DOCKED", StringComparison.OrdinalIgnoreCase))
            {
                await bus.InvokeAsync(new NegotiateContractCommand(primaryShip.Symbol), cancellationToken);
            }

            starterContract = (await contracts.GetActiveAsync(cancellationToken))
                .Where(c => c.IsAccepted && !c.IsFulfilled)
                .OrderBy(c => c.TermsDeadline ?? c.Expiration ?? DateTimeOffset.MaxValue)
                .FirstOrDefault();
        }

        if (starterContract is null)
        {
            logger.LogInformation("Phase 3 onboarding: no contract available after negotiation attempt.");
            return;
        }

        if (starterContract.Expiration.HasValue && starterContract.Expiration.Value <= TimeProvider.System.GetUtcNow())
        {
            if (string.Equals(primaryShip.Status, "DOCKED", StringComparison.OrdinalIgnoreCase))
            {
                await bus.InvokeAsync(new NegotiateContractCommand(primaryShip.Symbol), cancellationToken);
            }

            return;
        }

        await EnsureFleetExpansionForContractAsync(agent, primaryShip, cancellationToken);

        var deliverable = ResolvePendingDeliverable(starterContract);
        if (deliverable is null)
        {
            await bus.InvokeAsync(new FulfillContractCommand(starterContract.Id), cancellationToken);
            return;
        }

        await bus.InvokeAsync(new Commands.Ships.AssignShipCommand(
            primaryShip.Symbol,
            "Contract",
            DestWaypoint: deliverable.DestinationSymbol,
            CargoSymbol: deliverable.TradeSymbol,
            ContractId: starterContract.Id,
            RequiredUnits: Math.Max(0, deliverable.UnitsRequired - deliverable.UnitsFulfilled),
            SystemSymbol: primaryShip.SystemSymbol ?? string.Empty,
            WaypointSymbol: primaryShip.WaypointSymbol ?? string.Empty), cancellationToken);

        logger.LogInformation("Phase 3 onboarding assignment emitted for ship {Ship} and contract {ContractId}.", primaryShip.Symbol, starterContract.Id);
    }

    private async Task EnsureFleetExpansionForContractAsync(AgentModel agent, ShipModel ship, CancellationToken cancellationToken)
    {
        if (agent.Credits <= 0)
        {
            return;
        }

        var knownShipyard = await shipyards.FindShipyardForTypeAsync("SHIP_MINING_DRONE", cancellationToken);
        if (knownShipyard is null && !string.IsNullOrWhiteSpace(ship.SystemSymbol))
        {
            var systemWaypoints = await waypoints.GetBySystemAsync(ship.SystemSymbol, cancellationToken);
            var firstShipyard = systemWaypoints.FirstOrDefault(w => w.HasShipyard)?.Symbol;
            if (!string.IsNullOrWhiteSpace(firstShipyard))
            {
                knownShipyard = firstShipyard;
            }
        }

        if (string.IsNullOrWhiteSpace(knownShipyard))
        {
            return;
        }

        await bus.InvokeAsync(new PurchaseShipCommand("SHIP_MINING_DRONE", knownShipyard), cancellationToken);
    }

    private static ContractDeliverableDto? ResolvePendingDeliverable(ContractDto contract)
    {
        if (string.IsNullOrWhiteSpace(contract.DeliverablesJson))
        {
            return null;
        }

        try
        {
            var deliverables = JsonSerializer.Deserialize<List<ContractDeliverableDto>>(contract.DeliverablesJson) ?? [];
            return deliverables.FirstOrDefault(d => d.UnitsRequired > d.UnitsFulfilled);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
