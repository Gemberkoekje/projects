using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record OrbitShipCommand
{
    public required string ShipSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public OrbitShipCommand(string ShipSymbol)
    {
        this.ShipSymbol = ShipSymbol;
    }
}

public sealed class OrbitShipHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IMessageBus bus,
    ILogger<OrbitShipHandler> logger)
{
    public Task Handle(OrbitShipCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(command, cancellationToken);

    public async Task<ShipCommandResult> ExecuteAsync(OrbitShipCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "CommandHandler {Handler}: {Command} for ship {Symbol}.",
            nameof(OrbitShipHandler),
            nameof(OrbitShipCommand),
            command.ShipSymbol);

        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        var currentStatus = ship?.LocalStatus ?? ShipLocalStatus.None;

        if (currentStatus != ShipLocalStatus.Docked)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(OrbitShipCommand),
                "DOCKED",
                ship?.Status ?? "UNKNOWN",
                "Ship must be docked before orbiting.");

            logger.LogWarning("Skipping orbit for ship {Symbol}: expected DOCKED but was {Status}.", command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship?.SystemSymbol ?? string.Empty,
                ship?.WaypointSymbol ?? string.Empty);
        }

        var nav = await port.OrbitShipAsync(command.ShipSymbol, cancellationToken);
        await ships.UpdateNavAsync(command.ShipSymbol, nav, null, cancellationToken);

        var publishedAt = TimeProvider.System.GetUtcNow();

        if (!string.IsNullOrWhiteSpace(nav.WaypointSymbol))
        {
            await bus.PublishAsync(new ShipInOrbitEvent(
                command.ShipSymbol,
                nav.SystemSymbol,
                nav.WaypointSymbol,
                Guid.Empty,
                Guid.Empty,
                publishedAt));

            // Phase 7c: publish ShipInOrbitEvent + explicit automation tick (ShipUndockedEvent deleted).
            await bus.PublishAsync(new ShipAutomationTickEvent(
                command.ShipSymbol,
                "Undocked",
                publishedAt,
                Guid.NewGuid(),
                Guid.Empty));
        }

        logger.LogInformation(
            "CommandHandler {Handler}: {Command} handled; Ship {Symbol} in orbit at {Waypoint}.",
            nameof(OrbitShipHandler),
            nameof(OrbitShipCommand),
            command.ShipSymbol,
            nav.WaypointSymbol);

        return new ShipCommandResult(
            command.ShipSymbol,
            ShipLocalStatusMapper.FromApiStatus(nav.Status),
            nav.SystemSymbol,
            nav.WaypointSymbol,
            ArrivesAt: nav.ArrivesAt ?? default,
            FuelCurrent: ship?.FuelCurrent ?? 0,
            FuelCapacity: ship?.FuelCapacity ?? 0,
            CargoCurrent: ship?.CargoCurrent ?? 0,
            CargoCapacity: ship?.CargoCapacity ?? 0);
    }
}
