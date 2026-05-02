using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Fleet;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
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

        var deliverable = GetFirstPendingDeliverable(contract.DeliverablesJson);
        if (deliverable is null)
        {
            logger.LogDebug("Contract {ContractId} has no pending deliverables; skipping emergency reassign.", @event.ContractId);
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

                var remaining = Math.Max(0, deliverable.UnitsRequired - deliverable.UnitsFulfilled);
                var contractGoal = new FleetGoal(
                    Kind: FleetGoalKind.Contract,
                    Description: $"Emergency: deliver {remaining} {deliverable.TradeSymbol} for contract {@event.ContractId}.",
                    Priority: FleetGoalPriority.Contract,
                    ContractId: @event.ContractId,
                    TradeSymbol: deliverable.TradeSymbol,
                    RemainingUnits: remaining,
                    Deadline: contract.TermsDeadline ?? contract.Expiration);

                await bus.SendAsync(new AssignShipToGoalCommand(ship.Symbol, contractGoal));

                // Reassign only one ship per event to avoid over-allocating
                return;
            }
        }

        logger.LogWarning(
            "Contract {ContractId} deadline critical but no eligible ship found for emergency reassignment.",
            @event.ContractId);
    }

    private static ContractDeliverableDto? GetFirstPendingDeliverable(string? deliverablesJson)
    {
        if (string.IsNullOrWhiteSpace(deliverablesJson))
        {
            return null;
        }

        try
        {
            var deliverables = JsonSerializer.Deserialize<List<ContractDeliverableDto>>(deliverablesJson) ?? [];
            return deliverables.FirstOrDefault(d => d.UnitsRequired > d.UnitsFulfilled);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
