using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Commands.Ships.SubCommands;

/// <summary>
/// Subcommand that transitions a docked ship to orbit by calling the SpaceTraders API.
/// Must only be called when the ship is docked.
/// </summary>
public interface IOrbitSubCommand
{
    Task ExecuteAsync(string shipSymbol, CancellationToken cancellationToken);
}

public sealed class OrbitSubCommand(
    ISpaceTradersPort port,
    IShipRepository ships,
    ILogger<OrbitSubCommand> logger) : IOrbitSubCommand
{
    public async Task ExecuteAsync(string shipSymbol, CancellationToken cancellationToken)
    {
        logger.LogInformation("OrbitSubCommand: orbiting ship {Symbol}.", shipSymbol);
        var nav = await port.OrbitShipAsync(shipSymbol, cancellationToken);
        await ships.UpdateNavAsync(shipSymbol, nav, null, cancellationToken);
        logger.LogInformation("OrbitSubCommand: ship {Symbol} now in orbit at {Waypoint}.", shipSymbol, nav.WaypointSymbol);
    }
}
