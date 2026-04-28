using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record JettisonCargoCommand
{
    public required string ShipSymbol { get; init; }

    public required string TradeSymbol { get; init; }

    public required int Units { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public JettisonCargoCommand(string ShipSymbol, string TradeSymbol, int Units)
    {
        this.ShipSymbol = ShipSymbol;
        this.TradeSymbol = TradeSymbol;
        this.Units = Units;
    }
}

public sealed class JettisonCargoHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    ILogger<JettisonCargoHandler> logger)
{
    public async Task Handle(JettisonCargoCommand command, CancellationToken cancellationToken)
    {
        var result = await port.JettisonCargoAsync(command.ShipSymbol, command.TradeSymbol, command.Units, cancellationToken);
        await ships.UpdateCargoAsync(command.ShipSymbol, result.Cargo, cancellationToken);
        logger.LogInformation("Ship {Symbol} jettisoned {Units}x {Trade}. Cargo now {Current}/{Capacity}.",
            command.ShipSymbol, command.Units, command.TradeSymbol, result.Cargo.Units, result.Cargo.Capacity);
    }
}
