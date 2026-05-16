using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;

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
    IMarketRepository markets,
    IRefuelSubCommand refuel,
    ILogger<OrbitSubCommand> logger) : IOrbitSubCommand
{
    public async Task ExecuteAsync(string shipSymbol, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(shipSymbol, cancellationToken);

        if (ship is not null && ship.LocalStatus == ShipLocalStatus.Docked && await ShouldRefuelBeforeUndockingAsync(ship, cancellationToken))
        {
            if (ship.FuelCapacity > 0 && ship.FuelCurrent < ship.FuelCapacity)
            {
                logger.LogInformation(
                    "OrbitSubCommand: refueling ship {Symbol} to full before undocking from fuel market {Waypoint}.",
                    shipSymbol,
                    ship.WaypointSymbol ?? string.Empty);

                await refuel.ExecuteAsync(shipSymbol, fromCargo: false, cancellationToken);
                ship = await ships.FindAsync(shipSymbol, cancellationToken) ?? ship;
            }

            if (ship.FuelCapacity > 0 && ship.FuelCurrent < ship.FuelCapacity)
            {
                throw new InvalidOperationException($"OrbitSubCommand: ship {shipSymbol} is not fully refueled and cannot undock from fuel market {ship.WaypointSymbol}.");
            }
        }

        logger.LogInformation("OrbitSubCommand: orbiting ship {Symbol}.", shipSymbol);
        var nav = await port.OrbitShipAsync(shipSymbol, cancellationToken);
        await ships.UpdateNavAsync(shipSymbol, nav, null, cancellationToken);
        logger.LogInformation("OrbitSubCommand: ship {Symbol} now in orbit at {Waypoint}.", shipSymbol, nav.WaypointSymbol);
    }

    private async Task<bool> ShouldRefuelBeforeUndockingAsync(ShipModel ship, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ship.WaypointSymbol))
        {
            return false;
        }

        var snapshot = await markets.FindSnapshotByWaypointAsync(ship.WaypointSymbol, cancellationToken);
        return snapshot is not null && SellsFuel(snapshot);
    }

    private static bool SellsFuel(MarketSnapshot snapshot)
    {
        if (snapshot.Exports.Any(IsFuelSymbol) || snapshot.Exchange.Any(IsFuelSymbol))
        {
            return true;
        }

        return snapshot.TradeGoods.Any(g => IsFuelSymbol(g.Symbol) && !g.Type.Equals("IMPORT", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsFuelSymbol(string symbol)
        => symbol.Equals("FUEL", StringComparison.OrdinalIgnoreCase);
}
