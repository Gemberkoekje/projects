using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Handles <see cref="ContractDeadlineApproachingEvent"/> by performing an emergency
/// reassignment: finds ships on non-contract assignments and redirects them to fulfill
/// the approaching-deadline contract.
/// Only acts when the deadline is critically close (≤ 6 hours remaining).
/// </summary>
public sealed class ContractPriorityHandler(
    IShipAssignmentRepository assignments,
    IShipRepository ships,
    IContractRepository contracts,
    IMessageBus bus,
    ILogger<ContractPriorityHandler> logger)
{
    private static readonly TimeSpan CriticalThreshold = TimeSpan.FromHours(6);

    public async Task Handle(ContractDeadlineApproachingEvent @event, CancellationToken cancellationToken)
    {
        if (@event.Remaining > CriticalThreshold)
        {
            logger.LogDebug("Contract {ContractId} deadline in {Remaining} – not yet critical; skipping emergency reassign.", @event.ContractId, @event.Remaining);
            return;
        }

        var contract = (await contracts.GetActiveAsync(cancellationToken))
            .FirstOrDefault(c => c.Id == @event.ContractId);

        if (contract is null || contract.IsFulfilled)
        {
            logger.LogDebug("Contract {ContractId} not found or already fulfilled; skipping.", @event.ContractId);
            return;
        }

        var allShips = await ships.GetAllAsync(cancellationToken);

        foreach (var ship in allShips.Where(s => !s.IsInTransit))
        {
            var assignment = await assignments.FindAsync(ship.Symbol, cancellationToken);

            // Skip ships already working on this contract
            if (assignment is not null &&
                !assignment.CompletedAt.HasValue &&
                assignment.AssignmentType.Equals("Contract", StringComparison.OrdinalIgnoreCase) &&
                assignment.ContractId == @event.ContractId)
            {
                continue;
            }

            // Only redirect idle or trade ships – do not interrupt mining/scouting of other contracts
            if (assignment is null || assignment.CompletedAt.HasValue ||
                assignment.AssignmentType.Equals("Idle", StringComparison.OrdinalIgnoreCase) ||
                assignment.AssignmentType.Equals("Trade", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Contract {ContractId} deadline critical ({Remaining} remaining). Emergency reassigning ship {Symbol}.",
                    @event.ContractId, @event.Remaining, ship.Symbol);

                await bus.SendAsync(new AssignShipCommand(
                    ship.Symbol,
                    "Contract",
                    ContractId: @event.ContractId,
                    RequiredUnits: assignment?.RequiredUnits ?? 0));

                // Reassign only one ship per event to avoid over-allocating
                return;
            }
        }

        logger.LogWarning(
            "Contract {ContractId} deadline critical but no eligible ship found for emergency reassignment.",
            @event.ContractId);
    }
}
