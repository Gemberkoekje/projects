using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Application.Commands.Ships;

public record AssignShipCommand(
    string ShipSymbol,
    string AssignmentType,
    string? OriginWaypoint = null,
    string? DestWaypoint = null,
    string? CargoSymbol = null,
    string? ContractId = null);

public sealed class AssignShipHandler(
    SpaceTradersDbContext db,
    ILogger<AssignShipHandler> logger)
{
    public async Task Handle(AssignShipCommand command, CancellationToken cancellationToken)
    {
        var existing = await db.ShipAssignments
            .FirstOrDefaultAsync(x => x.ShipSymbol == command.ShipSymbol, cancellationToken);

        if (existing is null)
        {
            db.ShipAssignments.Add(new ShipAssignmentRecord
            {
                ShipSymbol = command.ShipSymbol,
                Type = command.AssignmentType,
                OriginWaypoint = command.OriginWaypoint,
                DestWaypoint = command.DestWaypoint,
                CargoSymbol = command.CargoSymbol,
                ContractId = command.ContractId,
                StepIndex = 0,
                AssignedAt = DateTimeOffset.UtcNow,
                CompletedAt = null
            });
        }
        else
        {
            existing.Type = command.AssignmentType;
            existing.OriginWaypoint = command.OriginWaypoint;
            existing.DestWaypoint = command.DestWaypoint;
            existing.CargoSymbol = command.CargoSymbol;
            existing.ContractId = command.ContractId;
            existing.StepIndex = 0;
            existing.AssignedAt = DateTimeOffset.UtcNow;
            existing.CompletedAt = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Ship {Symbol} assigned to {Type}.", command.ShipSymbol, command.AssignmentType);
    }
}
