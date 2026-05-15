using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record CreateChartCommand
{
    public required string ShipSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public CreateChartCommand(string ShipSymbol)
    {
        this.ShipSymbol = ShipSymbol;
    }
}

public sealed class CreateChartHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    ILogger<CreateChartHandler> logger)
{
    public async Task Handle(CreateChartCommand command, CancellationToken cancellationToken)
    {
        var result = await port.CreateChartAsync(command.ShipSymbol, cancellationToken);
        logger.LogInformation("Ship {Symbol} charted waypoint {Waypoint} ({Type}).",
            command.ShipSymbol, result.WaypointSymbol, result.WaypointType);
    }
}
