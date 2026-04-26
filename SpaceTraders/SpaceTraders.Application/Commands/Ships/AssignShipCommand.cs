using Microsoft.Extensions.Logging;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record AssignShipCommand
{
    public required string ShipSymbol { get; init; }

    public required string AssignmentType { get; init; }

    public string? OriginWaypoint { get; init; }

    public string? DestWaypoint { get; init; }

    public string? CargoSymbol { get; init; }

    public string? ContractId { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public AssignShipCommand(
        string ShipSymbol,
        string AssignmentType,
        string? OriginWaypoint = null,
        string? DestWaypoint = null,
        string? CargoSymbol = null,
        string? ContractId = null)
    {
        this.ShipSymbol = ShipSymbol;
        this.AssignmentType = AssignmentType;
        this.OriginWaypoint = OriginWaypoint;
        this.DestWaypoint = DestWaypoint;
        this.CargoSymbol = CargoSymbol;
        this.ContractId = ContractId;
    }
}

public sealed class AssignShipHandler(
    IShipAssignmentRepository assignments,
    IMessageBus bus,
    ILogger<AssignShipHandler> logger)
{
    public async Task Handle(AssignShipCommand command, CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();
        var dto = new ShipAssignmentDto(
            command.ShipSymbol,
            command.AssignmentType,
            command.OriginWaypoint,
            command.DestWaypoint,
            command.CargoSymbol,
            command.ContractId,
            0,
            now,
            null,
            0);
        await assignments.UpsertAsync(dto, cancellationToken);
        await bus.PublishAsync(new ShipAssignedEvent(command.ShipSymbol, command.AssignmentType));
        logger.LogInformation("Ship {Symbol} assigned to {Type}.", command.ShipSymbol, command.AssignmentType);
    }
}
