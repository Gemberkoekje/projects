using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events.Ships;
using SpaceTraders.Domain.ValueObjects;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record DockShipCommand
{
    public required string ShipSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public DockShipCommand(string ShipSymbol)
    {
        this.ShipSymbol = ShipSymbol;
    }
}

public sealed class DockShipHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IMessageBus bus,
    ILogger<DockShipHandler> logger)
{
    public Task Handle(DockShipCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(command, cancellationToken);

    public async Task<ShipCommandResult> ExecuteAsync(DockShipCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "CommandHandler {Handler}: {Command} for ship {Symbol}.",
            nameof(DockShipHandler),
            nameof(DockShipCommand),
            command.ShipSymbol);

        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        var currentStatus = ship?.LocalStatus ?? ShipLocalStatus.None;

        if (currentStatus != ShipLocalStatus.InOrbit)
        {
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(DockShipCommand),
                "IN_ORBIT",
                ship?.Status ?? "UNKNOWN",
                "Ship must be in orbit before docking.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning("Skipping dock for ship {Symbol}: expected IN_ORBIT but was {Status}.", command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship?.SystemSymbol ?? string.Empty,
                ship?.WaypointSymbol ?? string.Empty);
        }

        var nav = await port.DockShipAsync(command.ShipSymbol, cancellationToken);
        await ships.UpdateNavAsync(command.ShipSymbol, nav, null, cancellationToken);

        var publishedAt = TimeProvider.System.GetUtcNow();

        if (!string.IsNullOrWhiteSpace(nav.WaypointSymbol))
        {
            await bus.PublishAsync(new ShipDockedEvent(
                command.ShipSymbol,
                nav.SystemSymbol,
                nav.WaypointSymbol,
                Guid.Empty,
                Guid.Empty,
                publishedAt));

            // Phase 5b: explicit automation tick so the planner-driven flow runs without
            // depending on chain-of-command inheritance routing.
            await bus.PublishAsync(new ShipAutomationTickEvent(
                command.ShipSymbol,
                "Docked",
                publishedAt,
                Guid.NewGuid(),
                Guid.Empty));
        }

        logger.LogInformation(
            "CommandHandler {Handler}: {Command} handled; Ship {Symbol} docked at {Waypoint}.",
            nameof(DockShipHandler),
            nameof(DockShipCommand),
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
