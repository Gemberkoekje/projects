using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Sync;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record NavigateShipCommand
{
    public required string ShipSymbol { get; init; }

    public required string DestinationWaypoint { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public NavigateShipCommand(string ShipSymbol, string DestinationWaypoint)
    {
        this.ShipSymbol = ShipSymbol;
        this.DestinationWaypoint = DestinationWaypoint;
    }
}

public sealed class NavigateShipHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IMessageBus bus,
    ILogger<NavigateShipHandler> logger)
{
    public Task Handle(NavigateShipCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(command, cancellationToken);

    public async Task<ShipCommandResult> ExecuteAsync(NavigateShipCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "CommandHandler {Handler}: {Command} for ship {Symbol} to {Destination}.",
            nameof(NavigateShipHandler),
            nameof(NavigateShipCommand),
            command.ShipSymbol,
            command.DestinationWaypoint);

        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        var currentStatus = ship?.LocalStatus ?? ShipLocalStatus.None;

        if (currentStatus != ShipLocalStatus.InOrbit)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(NavigateShipCommand),
                "IN_ORBIT",
                ship?.Status ?? "UNKNOWN",
                "Ship must be in orbit before navigation.");

            logger.LogWarning(
                "Skipping navigation for ship {Symbol} to {Waypoint}: expected IN_ORBIT but was {Status}.",
                command.ShipSymbol,
                command.DestinationWaypoint,
                ship?.Status ?? "UNKNOWN");
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship?.SystemSymbol ?? string.Empty,
                ship?.WaypointSymbol ?? string.Empty);
        }

        var nowNavigate = TimeProvider.System.GetUtcNow();

        try
        {
            var result = await port.NavigateShipAsync(command.ShipSymbol, command.DestinationWaypoint, cancellationToken);
            await ships.UpdateNavAsync(command.ShipSymbol, result.Nav, result.Fuel, cancellationToken);

            var inTransitEvent = new ShipInTransitEvent(
                command.ShipSymbol,
                ship?.WaypointSymbol ?? result.Nav.WaypointSymbol,
                command.DestinationWaypoint,
                result.Nav.ArrivesAt ?? nowNavigate,
                Guid.Empty,
                Guid.Empty,
                nowNavigate);

            await bus.PublishAsync(inTransitEvent);

            var destinationSystem = ExtractSystemSymbol(command.DestinationWaypoint);
            await bus.SendAsync(new RefreshSystemDataCommand(destinationSystem));

            logger.LogInformation(
                "CommandHandler {Handler}: {Command} handled; Ship {Symbol} navigating to {Waypoint}, arrives at {Arrival}.",
                nameof(NavigateShipHandler),
                nameof(NavigateShipCommand),
                command.ShipSymbol,
                command.DestinationWaypoint,
                result.Nav.ArrivesAt);

            return new ShipCommandResult(
                command.ShipSymbol,
                ShipLocalStatusMapper.FromApiStatus(result.Nav.Status),
                result.Nav.SystemSymbol,
                result.Nav.WaypointSymbol,
                ArrivesAt: result.Nav.ArrivesAt ?? nowNavigate,
                FuelCurrent: result.Fuel?.Current ?? ship?.FuelCurrent ?? 0,
                FuelCapacity: result.Fuel?.Capacity ?? ship?.FuelCapacity ?? 0,
                CargoCurrent: ship?.CargoCurrent ?? 0,
                CargoCapacity: ship?.CargoCapacity ?? 0);
        }
        catch (Exception exception) when (LooksLikeNotInOrbitError(exception))
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(NavigateShipCommand),
                "IN_ORBIT",
                "DOCKED",
                "Navigation was rejected by API because ship is not in orbit.");

            logger.LogWarning(
                exception,
                "Skipping navigation for ship {Symbol} to {Waypoint}: upstream rejected command because ship is not in orbit.",
                command.ShipSymbol,
                command.DestinationWaypoint);

            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                ShipLocalStatus.Docked,
                ship?.SystemSymbol ?? string.Empty,
                ship?.WaypointSymbol ?? string.Empty);
        }
        catch (Exception exception) when (LooksLikeInsufficientFuelError(exception))
        {
            logger.LogWarning(
                exception,
                "Navigation for ship {Symbol} to {Waypoint} rejected due to insufficient fuel; docking for refuel.",
                command.ShipSymbol,
                command.DestinationWaypoint);

            await bus.SendAsync(new DockShipCommand(command.ShipSymbol));

            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship?.SystemSymbol ?? string.Empty,
                ship?.WaypointSymbol ?? string.Empty);
        }
    }

    private static bool LooksLikeInsufficientFuelError(Exception exception)
        => !string.IsNullOrWhiteSpace(exception.Message)
            && exception.Message.Contains("requires", StringComparison.OrdinalIgnoreCase)
            && exception.Message.Contains("more fuel", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeNotInOrbitError(Exception exception)
        => !string.IsNullOrWhiteSpace(exception.Message)
            && exception.Message.Contains("not currently in orbit", StringComparison.OrdinalIgnoreCase);

    private static string ExtractSystemSymbol(string waypointSymbol)
    {
        var lastDash = waypointSymbol.LastIndexOf('-');
        return lastDash > 0 ? waypointSymbol[..lastDash] : waypointSymbol;
    }
}
