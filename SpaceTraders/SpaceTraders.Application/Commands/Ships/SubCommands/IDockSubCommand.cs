using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Commands.Ships.SubCommands;

/// <summary>
/// Subcommand that docks an in-orbit ship by calling the SpaceTraders API.
/// Must only be called when the ship is in orbit.
/// </summary>
public interface IDockSubCommand
{
    Task ExecuteAsync(string shipSymbol, CancellationToken cancellationToken);
}

public sealed class DockSubCommand(
    ISpaceTradersPort port,
    IShipRepository ships,
    IDashboardNotifier dashboardNotifier,
    ILogger<DockSubCommand> logger) : IDockSubCommand
{
    public async Task ExecuteAsync(string shipSymbol, CancellationToken cancellationToken)
    {
        logger.LogInformation("DockSubCommand: docking ship {Symbol}.", shipSymbol);
        var nav = await port.DockShipAsync(shipSymbol, cancellationToken);
        await ships.UpdateNavAsync(shipSymbol, nav, null, cancellationToken);
        dashboardNotifier.Notify("ships", shipSymbol);
        dashboardNotifier.Notify("fleet-activity", shipSymbol);
        dashboardNotifier.Notify("activity", shipSymbol);
        logger.LogInformation("DockSubCommand: ship {Symbol} docked at {Waypoint}.", shipSymbol, nav.WaypointSymbol);
    }
}
