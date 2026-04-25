using Microsoft.Extensions.Logging;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Commands.Ships;

public record AssignShipCommand(
    string ShipSymbol,
    string AssignmentType,
    string? OriginWaypoint = null,
    string? DestWaypoint = null,
    string? CargoSymbol = null,
    string? ContractId = null);

public sealed class AssignShipHandler(
    IShipAssignmentRepository assignments,
    ILogger<AssignShipHandler> logger)
{
    public async Task Handle(AssignShipCommand command, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var dto = new ShipAssignmentDto(
            command.ShipSymbol,
            command.AssignmentType,
            command.OriginWaypoint,
            command.DestWaypoint,
            command.CargoSymbol,
            command.ContractId,
            0,
            now,
            null);
        await assignments.UpsertAsync(dto, cancellationToken);
        logger.LogInformation("Ship {Symbol} assigned to {Type}.", command.ShipSymbol, command.AssignmentType);
    }
}
