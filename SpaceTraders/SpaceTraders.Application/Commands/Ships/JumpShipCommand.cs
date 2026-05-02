using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record JumpShipCommand
{
    public required string ShipSymbol { get; init; }

    public required string DestinationSystem { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public JumpShipCommand(string ShipSymbol, string DestinationSystem)
    {
        this.ShipSymbol = ShipSymbol;
        this.DestinationSystem = DestinationSystem;
    }
}

public sealed class JumpShipHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IShipGoalRepository goals,
    IShipEventScheduler scheduler,
    IMessageBus bus,
    IJumpGateCacheService jumpGates,
    ILogger<JumpShipHandler> logger)
{
    private sealed class NoopJumpGateCacheService : IJumpGateCacheService
    {
        public Task TryRefreshAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public JumpShipHandler(
        ISpaceTradersPort port,
        IShipRepository ships,
        IShipGoalRepository goals,
        IShipEventScheduler scheduler,
        IMessageBus bus,
        ILogger<JumpShipHandler> logger)
        : this(port, ships, goals, scheduler, bus, new NoopJumpGateCacheService(), logger)
    {
    }

    public Task Handle(JumpShipCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(command, cancellationToken);

    public async Task<ShipCommandResult> ExecuteAsync(JumpShipCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        var currentStatus = ship?.LocalStatus ?? ShipLocalStatus.None;

        if (currentStatus != ShipLocalStatus.InOrbit)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(JumpShipCommand),
                "IN_ORBIT",
                ship?.Status ?? "UNKNOWN",
                "Ship must be in orbit to jump.");

            logger.LogWarning("Skipping jump for ship {Symbol}: expected IN_ORBIT but was {Status}.",
                command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship?.SystemSymbol ?? string.Empty,
                ship?.WaypointSymbol ?? string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(ship!.SystemSymbol) && !string.IsNullOrWhiteSpace(ship.WaypointSymbol))
        {
            await jumpGates.TryRefreshAsync(ship.SystemSymbol, ship.WaypointSymbol, cancellationToken);
        }

        var nowJump = TimeProvider.System.GetUtcNow();
        var result = await port.JumpShipAsync(command.ShipSymbol, command.DestinationSystem, cancellationToken);
        await ships.UpdateNavAsync(command.ShipSymbol, result.Nav, null, cancellationToken);

        var arrivalTime = result.Nav.ArrivesAt ?? nowJump;

        var inTransitEvent = new ShipInTransitEvent(
            command.ShipSymbol,
            ship.WaypointSymbol ?? result.Nav.WaypointSymbol,
            result.Nav.DestWaypointSymbol ?? command.DestinationSystem,
            arrivalTime,
            Guid.Empty,
            Guid.Empty,
            nowJump);

        await bus.PublishAsync(inTransitEvent);

        // Phase 14c: schedule the arrival event through the persistent scheduler.
        var activeGoal = await goals.GetActiveGoalAsync(command.ShipSymbol, cancellationToken);
        if (activeGoal is not null)
        {
            await scheduler.ScheduleArrivalAsync(command.ShipSymbol, activeGoal.GoalId, arrivalTime, cancellationToken);
        }

        logger.LogInformation("Ship {Symbol} jumping to system {System}, cooldown {Seconds}s.",
            command.ShipSymbol, command.DestinationSystem, result.CooldownSeconds);

        return new ShipCommandResult(
            command.ShipSymbol,
            ShipLocalStatusMapper.FromApiStatus(result.Nav.Status),
            result.Nav.SystemSymbol,
            result.Nav.WaypointSymbol,
            ArrivesAt: arrivalTime,
            FuelCurrent: ship.FuelCurrent,
            FuelCapacity: ship.FuelCapacity,
            CargoCurrent: ship.CargoCurrent,
            CargoCapacity: ship.CargoCapacity);
    }
}
