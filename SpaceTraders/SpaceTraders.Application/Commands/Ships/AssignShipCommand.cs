using Microsoft.Extensions.Logging;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Events.Ships;
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

    public int RequiredUnits { get; init; }

    public bool SupplyCompleted { get; init; }

    public string SystemSymbol { get; init; }

    public string WaypointSymbol { get; init; }

    public Guid CorrelationId { get; init; }

    public Guid CausationId { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public AssignShipCommand(
        string ShipSymbol,
        string AssignmentType,
        string? OriginWaypoint = null,
        string? DestWaypoint = null,
        string? CargoSymbol = null,
        string? ContractId = null,
        string SystemSymbol = "",
        string WaypointSymbol = "",
        Guid CorrelationId = default,
        Guid CausationId = default,
        int RequiredUnits = 0,
        bool SupplyCompleted = false)
    {
        this.ShipSymbol = ShipSymbol;
        this.AssignmentType = AssignmentType;
        this.OriginWaypoint = OriginWaypoint;
        this.DestWaypoint = DestWaypoint;
        this.CargoSymbol = CargoSymbol;
        this.ContractId = ContractId;
        this.SystemSymbol = SystemSymbol;
        this.WaypointSymbol = WaypointSymbol;
        this.CorrelationId = CorrelationId;
        this.CausationId = CausationId;
        this.RequiredUnits = RequiredUnits;
        this.SupplyCompleted = SupplyCompleted;
    }
}

public sealed class AssignShipHandler(
    IShipAssignmentRepository assignments,
    IShipRepository ships,
    IMessageBus bus,
    ILogger<AssignShipHandler> logger)
{
    public async Task Handle(AssignShipCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "CommandHandler {Handler}: {Command} for ship {Symbol} to assignment {Assignment}.",
            nameof(AssignShipHandler),
            nameof(AssignShipCommand),
            command.ShipSymbol,
            command.AssignmentType);

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
            0,
            command.RequiredUnits,
            command.SupplyCompleted);
        await assignments.UpsertAsync(dto, cancellationToken);
        await bus.PublishAsync(new ShipAssignedEvent(command.ShipSymbol, command.AssignmentType));

        logger.LogInformation(
            "CommandHandler {Handler}: {Command} handled; Ship {Symbol} assigned to {Assignment}.",
            nameof(AssignShipHandler),
            nameof(AssignShipCommand),
            command.ShipSymbol,
            command.AssignmentType);

        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        var systemSymbol = string.IsNullOrWhiteSpace(command.SystemSymbol)
            ? (ship?.SystemSymbol ?? string.Empty)
            : command.SystemSymbol;
        var waypointSymbol = string.IsNullOrWhiteSpace(command.WaypointSymbol)
            ? (ship?.WaypointSymbol ?? string.Empty)
            : command.WaypointSymbol;
        var correlationId = command.CorrelationId == Guid.Empty ? Guid.NewGuid() : command.CorrelationId;
        var causationId = command.CausationId;

        await bus.PublishAsync(new ShipAssignmentTypeSetEvent(
            command.ShipSymbol,
            systemSymbol,
            waypointSymbol,
            command.AssignmentType,
            correlationId,
            causationId,
            now));
    }
}
