using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
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
    IMessageBus bus,
    ILogger<JettisonCargoHandler> logger)
{
    public Task Handle(JettisonCargoCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(command, cancellationToken);

    public async Task<ShipCommandResult> ExecuteAsync(JettisonCargoCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        var currentStatus = ship?.LocalStatus ?? ShipLocalStatus.None;

        if (currentStatus == ShipLocalStatus.InTransit || currentStatus == ShipLocalStatus.None)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(JettisonCargoCommand),
                "DOCKED_OR_IN_ORBIT",
                ship?.Status ?? "UNKNOWN",
                "Ship must be docked or in orbit before jettisoning cargo.");

            logger.LogWarning("Skipping jettison for ship {Symbol}: expected DOCKED or IN_ORBIT but was {Status}.",
                command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship?.SystemSymbol ?? string.Empty,
                ship?.WaypointSymbol ?? string.Empty);
        }

        var result = await port.JettisonCargoAsync(command.ShipSymbol, command.TradeSymbol, command.Units, cancellationToken);
        await ships.UpdateCargoAsync(command.ShipSymbol, result.Cargo, cancellationToken);
        logger.LogInformation("Ship {Symbol} jettisoned {Units}x {Trade}. Cargo now {Current}/{Capacity}.",
            command.ShipSymbol, command.Units, command.TradeSymbol, result.Cargo.Units, result.Cargo.Capacity);

        return new ShipCommandResult(
            command.ShipSymbol,
            currentStatus,
            ship!.SystemSymbol ?? string.Empty,
            ship.WaypointSymbol ?? string.Empty,
            FuelCurrent: ship.FuelCurrent,
            FuelCapacity: ship.FuelCapacity,
            CargoCurrent: result.Cargo.Units,
            CargoCapacity: result.Cargo.Capacity);
    }
}
