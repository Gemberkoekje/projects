using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

/// <summary>
/// Sets the flight mode for a ship (e.g., to "DRIFT" for solar-powered navigation).
/// Used by probes and other ships without fuel capacity to operate indefinitely.
/// </summary>
public sealed record PatchShipNavCommand
{
    public required string ShipSymbol { get; init; }

    public required string FlightMode { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public PatchShipNavCommand(string ShipSymbol, string FlightMode)
    {
        this.ShipSymbol = ShipSymbol;
        this.FlightMode = FlightMode;
    }
}

public sealed class PatchShipNavHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    ILogger<PatchShipNavHandler> logger)
{
    public async Task Handle(PatchShipNavCommand command, CancellationToken cancellationToken)
    {
        var nav = await port.PatchShipNavAsync(command.ShipSymbol, command.FlightMode, cancellationToken);
        await ships.UpdateNavAsync(command.ShipSymbol, nav, null, cancellationToken);
        logger.LogInformation(
            "PatchShipNavHandler: ship {Symbol} flight mode set to {FlightMode}.",
            command.ShipSymbol,
            command.FlightMode);
    }
}
