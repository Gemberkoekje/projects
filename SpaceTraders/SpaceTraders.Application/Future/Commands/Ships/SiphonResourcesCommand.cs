using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record SiphonResourcesCommand
{
    public required string ShipSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SiphonResourcesCommand(string ShipSymbol)
    {
        this.ShipSymbol = ShipSymbol;
    }
}

public sealed class SiphonResourcesHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IShipGoalRepository goals,
    IShipEventScheduler scheduler,
    IWaypointRepository waypoints,
    IMessageBus bus,
    ILogger<SiphonResourcesHandler> logger)
{
    public Task Handle(SiphonResourcesCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(command, cancellationToken);

    public async Task<ShipCommandResult> ExecuteAsync(SiphonResourcesCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        var currentStatus = ship?.LocalStatus ?? ShipLocalStatus.None;

        if (currentStatus != ShipLocalStatus.InOrbit)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(SiphonResourcesCommand),
                "IN_ORBIT",
                ship?.Status ?? "UNKNOWN",
                "Ship must be in orbit to siphon.");

            logger.LogWarning("Skipping siphon for ship {Symbol}: expected IN_ORBIT but was {Status}.",
                command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship?.SystemSymbol ?? string.Empty,
                ship?.WaypointSymbol ?? string.Empty);
        }

        if (!ship!.HasGasSiphonEquipment || !ship.HasGasProcessor)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(SiphonResourcesCommand),
                "IN_ORBIT_WITH_GAS_SIPHON_AND_PROCESSOR",
                "IN_ORBIT_WITHOUT_REQUIRED_SIPHON_CAPABILITY",
                "Ship must have gas siphon equipment and a gas processor to siphon.");

            logger.LogWarning("Skipping siphon for ship {Symbol}: missing gas siphon or gas processor capability.", command.ShipSymbol);
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship.SystemSymbol ?? string.Empty,
                ship.WaypointSymbol ?? string.Empty);
        }

        if (string.IsNullOrWhiteSpace(ship.WaypointSymbol))
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(SiphonResourcesCommand),
                "IN_ORBIT_AT_GAS_GIANT",
                "IN_ORBIT_AT_UNKNOWN_WAYPOINT",
                "Ship waypoint is unknown, cannot validate siphon location.");

            logger.LogWarning("Skipping siphon for ship {Symbol}: ship waypoint unknown.", command.ShipSymbol);
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship.SystemSymbol ?? string.Empty,
                string.Empty);
        }

        var waypoint = await waypoints.FindAsync(ship.WaypointSymbol, cancellationToken);
        if (waypoint is null || !waypoint.Type.Contains("GAS_GIANT", StringComparison.OrdinalIgnoreCase))
        {
            var actual = waypoint?.Type ?? "UNKNOWN";
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(SiphonResourcesCommand),
                "IN_ORBIT_AT_GAS_GIANT",
                $"IN_ORBIT_AT_{actual}",
                "Ship is not at a gas giant waypoint.");

            logger.LogWarning("Skipping siphon for ship {Symbol}: waypoint {Waypoint} type {Type} is not a gas giant.", command.ShipSymbol, ship.WaypointSymbol, actual);
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship.SystemSymbol ?? string.Empty,
                ship.WaypointSymbol);
        }

        var result = await port.SiphonResourcesAsync(command.ShipSymbol, cancellationToken);
        await ships.UpdateCargoAsync(command.ShipSymbol, result.Cargo, cancellationToken);

        // Phase 14c: persist cooldown and schedule the wake-up event via the persistent scheduler.
        var now = TimeProvider.System.GetUtcNow();
        var cooldownExpiresAt = result.CooldownExpiresAt ?? now.AddSeconds(result.CooldownSeconds);
        await ships.UpdateCooldownAsync(command.ShipSymbol, cooldownExpiresAt, cancellationToken);

        var activeGoal = await goals.GetActiveGoalAsync(command.ShipSymbol, cancellationToken);
        if (activeGoal is not null)
        {
            await scheduler.ScheduleCooldownExpiryAsync(command.ShipSymbol, activeGoal.GoalId, cooldownExpiresAt, cancellationToken);
        }

        logger.LogInformation("Ship {Symbol} siphoned {Units}x {Symbol2}, cooldown expires {Cooldown}.",
            command.ShipSymbol, result.YieldUnits, result.YieldSymbol, cooldownExpiresAt);

        return new ShipCommandResult(
            command.ShipSymbol,
            currentStatus,
            ship.SystemSymbol ?? string.Empty,
            ship.WaypointSymbol,
            FuelCurrent: ship.FuelCurrent,
            FuelCapacity: ship.FuelCapacity,
            CargoCurrent: result.Cargo.Units,
            CargoCapacity: result.Cargo.Capacity);
    }
}
