using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record WarpShipCommand
{
    public required string ShipSymbol { get; init; }

    public required string DestinationWaypoint { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public WarpShipCommand(string ShipSymbol, string DestinationWaypoint)
    {
        this.ShipSymbol = ShipSymbol;
        this.DestinationWaypoint = DestinationWaypoint;
    }
}

public sealed class WarpShipHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IShipGoalRepository goals,
    IShipEventScheduler scheduler,
    IMessageBus bus,
    ILogger<WarpShipHandler> logger)
{
    public Task Handle(WarpShipCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(command, cancellationToken);

    public async Task<ShipCommandResult> ExecuteAsync(WarpShipCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        var currentStatus = ship?.LocalStatus ?? ShipLocalStatus.None;

        if (currentStatus != ShipLocalStatus.InOrbit)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(WarpShipCommand),
                "IN_ORBIT",
                ship?.Status ?? "UNKNOWN",
                "Ship must be in orbit to warp.");

            logger.LogWarning("Skipping warp for ship {Symbol}: expected IN_ORBIT but was {Status}.",
                command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship?.SystemSymbol ?? string.Empty,
                ship?.WaypointSymbol ?? string.Empty);
        }

        var nowWarp = TimeProvider.System.GetUtcNow();
        var result = await port.WarpShipAsync(command.ShipSymbol, command.DestinationWaypoint, cancellationToken);
        await ships.UpdateNavAsync(command.ShipSymbol, result.Nav, result.Fuel, cancellationToken);

        var arrivalTime = result.Nav.ArrivesAt ?? nowWarp;

        var inTransitEvent = new ShipInTransitEvent(
            command.ShipSymbol,
            ship!.WaypointSymbol ?? result.Nav.WaypointSymbol,
            command.DestinationWaypoint,
            arrivalTime,
            Guid.Empty,
            Guid.Empty,
            nowWarp);

        await bus.PublishAsync(inTransitEvent);

        // Phase 14c: schedule the arrival event through the persistent scheduler.
        var activeGoal = await goals.GetActiveGoalAsync(command.ShipSymbol, cancellationToken);
        if (activeGoal is not null)
        {
            await scheduler.ScheduleArrivalAsync(command.ShipSymbol, activeGoal.GoalId, arrivalTime, cancellationToken);
        }

        logger.LogInformation("Ship {Symbol} warping to {Waypoint}, arrives at {Arrival}.",
            command.ShipSymbol, command.DestinationWaypoint, arrivalTime);

        return new ShipCommandResult(
            command.ShipSymbol,
            ShipLocalStatusMapper.FromApiStatus(result.Nav.Status),
            result.Nav.SystemSymbol,
            result.Nav.WaypointSymbol,
            ArrivesAt: arrivalTime,
            FuelCurrent: result.Fuel?.Current ?? ship.FuelCurrent,
            FuelCapacity: result.Fuel?.Capacity ?? ship.FuelCapacity,
            CargoCurrent: ship.CargoCurrent,
            CargoCapacity: ship.CargoCapacity);
    }
}
